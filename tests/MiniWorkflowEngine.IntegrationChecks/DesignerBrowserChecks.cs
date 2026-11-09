using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Services.Workflow;

internal static partial class AuthenticationChecks
{
    private static async Task CheckDesignerBrowserAsync(HttpClient admin)
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe")
        };
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
        {
            Console.WriteLine("ATLANDI: Tasarımcı tarayıcı kontrolü için Chrome veya Edge bulunamadı.");
            return;
        }

        var html = await admin.GetStringAsync("/Workflows/Designer");
        var editor = await admin.GetStringAsync("/js/graph-designer.js");
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var temporary = Path.Combine(tempRoot, "mwe-designer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var document = Path.Combine(temporary, "designer.html");
            var fixture = Regex.Replace(html, "<script[^>]*src=[^>]*></script>", "");
            await File.WriteAllTextAsync(document, fixture.Replace("</body>", $"<script>{editor}</script>{BrowserScenario}</body>"));
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
            foreach (var argument in new[] { "--headless", "--no-first-run", "--disable-background-networking",
                         "--disable-gpu", "--dump-dom", "--user-data-dir=" + Path.Combine(temporary, "browser"), new Uri(document).AbsoluteUri })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Tasarımcı testi için tarayıcı başlatılamadı.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new InvalidOperationException("Tasarımcı tarayıcı testi zaman aşımına uğradı.");
            }
            var result = Regex.Match(await stdout, "<output id=\"designer-check\">([^<]*)</output>");
            var error = await stderr;
            Require(process.ExitCode == 0 && result.Success, $"Tarayıcı tasarımcı çıktısı alınamadı. Çıkış: {process.ExitCode}. {error}");
            var json = WebUtility.HtmlDecode(result.Groups[1].Value);
            var graph = WorkflowGraph.Read(json);
            Require(graph is not null && GraphRules.Validate(graph, publishing: true) is null &&
                graph.Nodes.First().X > 28 && graph.Fields.Length == 2 && graph.Fields[0].Key == "alan_2",
                "Tarayıcıda düğüm taşıma, bağlama ve form alanı sıralama geçerli grafik üretmeli.");
            var draft = await PostFormAsync(admin, "/Workflows/Designer", "/Workflows/Designer", new()
            {
                ["Id"] = "0", ["Code"] = $"UI_{Guid.NewGuid():N}", ["Name"] = "Tarayıcı testi", ["GraphJson"] = json
            });
            Require(draft.StatusCode == HttpStatusCode.Redirect, "Tarayıcıda oluşturulan grafik sunucuda kaydedilebilmeli.");
            Console.WriteLine("BAŞARILI: Chromium ile düğüm taşıma, bağlama, form sıralama ve gerçek taslak kaydı.");
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(temporary)) == tempRoot.TrimEnd(Path.DirectorySeparatorChar) && Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
        }
    }

    private const string BrowserScenario = """
        <script>
        const editorForm = document.getElementById('graph-editor');
        const lookup = selector => document.querySelector(selector);
        lookup('[data-add-node="start"]').click();
        lookup('[data-add-node="end"]').click();
        lookup('.graph-node[data-kind="start"]').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
        lookup('#route-pick').click();
        lookup('.graph-node[data-kind="start"]').click();
        lookup('.graph-node[data-kind="end"]').click();
        lookup('#route-save').click();
        lookup('#field-add').click();
        lookup('#field-add').click();
        lookup('.graph-field:nth-child(2) button').click();
        editorForm.dispatchEvent(new Event('submit', { cancelable: true }));
        const output = document.createElement('output');
        output.id = 'designer-check';
        output.textContent = lookup('#GraphJson').value;
        document.body.append(output);
        </script>
        """;
}
