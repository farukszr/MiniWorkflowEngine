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
        var bootstrap = await admin.GetStringAsync("/lib/bootstrap/dist/css/bootstrap.min.css");
        var styles = await admin.GetStringAsync("/css/site.css");
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var temporary = Path.Combine(tempRoot, "mwe-designer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var document = Path.Combine(temporary, "designer.html");
            var fixture = Regex.Replace(html, "<script[^>]*src=[^>]*></script>", "");
            await File.WriteAllTextAsync(document, fixture.Replace("</head>", $"<style>{bootstrap}{styles}</style></head>")
                .Replace("</body>", $"<script>{editor}</script>{BrowserScenario}</body>"));
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
            foreach (var argument in new[] { "--headless", "--no-first-run", "--disable-background-networking",
                         "--disable-gpu", "--window-size=500,850", "--dump-dom", "--user-data-dir=" + Path.Combine(temporary, "browser"), new Uri(document).AbsoluteUri })
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
            var browserHtml = await stdout;
            var result = Regex.Match(browserHtml, "<output id=\"designer-check\">([^<]*)</output>");
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
            var example = Regex.Match(browserHtml, "<output id=\"designer-expense\">([^<]*)</output>");
            Require(example.Success, "Masraf örneği tasarımcıda hazırlanamadı.");
            var savedExample = await PostFormAsync(admin, "/Workflows/Designer", "/Workflows/Designer", new()
            {
                ["Id"] = "0", ["Name"] = "Koşullu masraf onayı",
                ["GraphJson"] = WebUtility.HtmlDecode(example.Groups[1].Value), ["next"] = "preview"
            });
            var previewPath = savedExample.Headers.Location?.ToString();
            Require(savedExample.StatusCode == HttpStatusCode.Redirect && previewPath?.Contains("/GraphPreview/") == true,
                "Hazır masraf örneği süreç kodu girmeden kaydedilip önizlenebilmeli.");
            var above = await PostFormAsync(admin, previewPath!, previewPath!, new() { ["Amount"] = "15000" });
            var boundary = await PostFormAsync(admin, previewPath!, previewPath!, new() { ["Amount"] = "10000" });
            var preview = WebUtility.HtmlDecode(await above.Content.ReadAsStringAsync());
            Require(preview.Contains("İlk onay: Masrafı incele") && preview.Contains("Yayımlamaya geç") &&
                WebUtility.HtmlDecode(await boundary.Content.ReadAsStringAsync()).Contains("Süreç doğrudan tamamlanır"),
                "Masraf örneği 15.000'i onaya, 10.000'i varsayılan bitişe yönlendirmeli.");
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
        if (getComputedStyle(document.body).backgroundColor !== 'rgb(17, 25, 37)')
            throw new Error('Genel koyu tema yüklenmedi.');
        if (getComputedStyle(lookup('.graph-workspace')).gridTemplateColumns.trim().split(/\s+/).length !== 1)
            throw new Error('Dar ekranda tuval ve özellik paneli alt alta yerleşmeli.');
        lookup('#graph-start-expense').click();
        if (lookup('#decision-edit-rule').hidden || document.querySelectorAll('.graph-node').length !== 5 ||
            document.querySelectorAll('#graph-routes li').length !== 5) throw new Error('Masraf örneği eksik.');
        lookup('#decision-edit-rule').click();
        if (!lookup('#route-preview').textContent.includes('Tutar > 10000 ise')) throw new Error('Örnek koşulu görünmüyor.');
        if (editorForm.dispatchEvent(new Event('submit', { cancelable: true }))) throw new Error('Açık koşul düzenlemesi kayıtta uyarılmalı.');
        lookup('#route-cancel').click();
        lookup('#decision-match-target').click();
        lookup('.graph-node[data-outcome="rejected"]').click();
        if (lookup('#route-value').value !== '10000') throw new Error('Hedef değişirken mevcut koşul korunmalı.');
        lookup('#route-save').click();
        if (document.querySelectorAll('#graph-routes li').length !== 5 || !lookup('#decision-match-status').textContent.includes('Reddedildi'))
            throw new Error('Koşul hedefi ek bir bağlantı oluşturmadan değiştirilmeli.');
        lookup('#graph-undo').click();
        editorForm.dispatchEvent(new Event('submit', { cancelable: true }));
        const expense = document.createElement('output');
        expense.id = 'designer-expense';
        expense.textContent = lookup('#GraphJson').value;
        document.body.append(expense);
        lookup('#graph-undo').click();
        lookup('#graph-start-approval').click();
        if (document.querySelectorAll('.graph-node').length !== 4 ||
            document.querySelectorAll('#graph-routes li').length !== 3 ||
            document.activeElement !== lookup('#node-role')) throw new Error('Hazır onay akışı eksik.');
        lookup('#graph-undo').click();
        lookup('[data-add-node="start"]').click();
        lookup('#graph-append-kind').value = 'end';
        lookup('#graph-append-add').click();
        if (document.querySelectorAll('.graph-node').length !== 2 || document.querySelectorAll('#graph-routes li').length !== 1)
            throw new Error('Bağlı adım eklenmedi.');
        lookup('#graph-undo').click();
        lookup('[data-add-node="approval"]').click();
        lookup('[data-add-node="decision"]').click();
        lookup('[data-add-node="end"]:not([data-outcome])').click();
        lookup('[data-add-node="end"][data-outcome="rejected"]').click();
        lookup('#graph-zoom').value = '150';
        lookup('#graph-zoom').dispatchEvent(new Event('input'));
        lookup('#graph-fit').click();
        if (Number(lookup('#graph-zoom').value) > 100) throw new Error('Tuvale sığdır çalışmadı.');
        lookup('.graph-node[data-kind="start"]').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
        const port = lookup('.graph-port[data-route="next"]');
        const approval = lookup('.graph-node[data-kind="approval"]');
        port.setPointerCapture = () => {};
        port.dispatchEvent(new PointerEvent('pointerdown', { pointerId: 1, button: 0, clientX: 10 }));
        port.dispatchEvent(new PointerEvent('pointermove', { pointerId: 1, clientX: 45 }));
        if (!lookup('.graph-drag-line')) throw new Error('Bağlantı önizgisi çizilmedi.');
        const originalHitTest = document.elementFromPoint;
        document.elementFromPoint = () => approval;
        port.dispatchEvent(new PointerEvent('pointerup', { pointerId: 1, clientX: 45 }));
        document.elementFromPoint = originalHitTest;
        if (!lookup('#graph-connection-settings').hidden || document.querySelectorAll('#graph-routes li').length !== 1)
            throw new Error('İlerle bağlantısı sürükleme sonunda doğrudan kaydedilmedi.');
        lookup('.graph-node[data-kind="approval"]').click();
        lookup('#node-role').selectedIndex = 1;
        lookup('#node-role').dispatchEvent(new Event('change'));
        const link = (route, target) => {
            lookup('.graph-port[data-route="' + route + '"]').click();
            lookup(target).click();
        };
        const acceptedEnd = '.graph-node[data-outcome="completed"]';
        link('approve', '.graph-node[data-kind="decision"]');
        link('reject', acceptedEnd);
        if (document.querySelectorAll('#graph-routes li').length !== 2) throw new Error('Ret çıkışı hatalı hedefe bağlandı.');
        lookup('.graph-node[data-outcome="rejected"]').click();
        lookup('.graph-node[data-kind="decision"]').click();
        if (lookup('#decision-guide').hidden || !lookup('#decision-match-status').textContent.includes('henüz'))
            throw new Error('Karar yolları kılavuzu görünmüyor.');
        lookup('[data-decision-output="match"]').click();
        lookup(acceptedEnd).click();
        if (lookup('#graph-connection-settings').hidden || document.querySelectorAll('#graph-routes li').length !== 3)
            throw new Error('Koşul için ayar paneli açılmadı.');
        lookup('#route-variable').value = 'department';
        lookup('#route-variable').dispatchEvent(new Event('change'));
        if (lookup('#route-operator').options.length !== 2) throw new Error('Metin alanında geçersiz karşılaştırma sunuldu.');
        lookup('#route-variable').value = 'amount';
        lookup('#route-variable').dispatchEvent(new Event('change'));
        if (lookup('#route-operator').value !== 'greaterThan') throw new Error('Tutar için başlangıç karşılaştırması seçilmedi.');
        lookup('#route-value').value = '10000';
        lookup('#route-value').dispatchEvent(new Event('input'));
        if (!lookup('#route-preview').textContent.includes('Tutar > 10000 ise')) throw new Error('Koşul özeti güncellenmedi.');
        lookup('#route-save').click();
        lookup('[data-decision-output="otherwise"]').click();
        const targetPicker = lookup('#graph-target');
        if (lookup('#graph-target-picker').hidden || !lookup('#graph-target-connect').disabled)
            throw new Error('Hedef listesi hedef seçilene kadar Bağla düğmesini kapalı tutmalı.');
        targetPicker.value = lookup(acceptedEnd).dataset.id;
        targetPicker.dispatchEvent(new Event('change'));
        lookup('#graph-target-connect').click();
        lookup('.graph-node[data-kind="decision"]').click();
        if (!lookup('#decision-match-status').textContent.includes('Tamamlandı') ||
            !lookup('#decision-default-status').textContent.includes('Tamamlandı')) throw new Error('Karar yolları bağlı olarak gösterilmedi.');
        lookup('#designer-tab-form').click();
        if (lookup('#designer-form').hidden || !lookup('#designer-flow').hidden) throw new Error('Form sekmesi açılmadı.');
        lookup('#field-add').click();
        lookup('#field-add').click();
        lookup('.graph-field:nth-child(2) button').click();
        lookup('#designer-tab-form').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
        if (lookup('#designer-test').hidden || lookup('#graph-test-save').disabled) throw new Error('Test sekmesi açılmadı.');
        lookup('#designer-tab-flow').click();
        lookup('#graph-open-test').click();
        if (lookup('#designer-test').hidden || lookup('#studio-stage-preview').getAttribute('aria-current') !== 'step')
            throw new Error('Teste geç düğmesi test adımını göstermedi.');
        editorForm.dispatchEvent(new Event('submit', { cancelable: true }));
        const output = document.createElement('output');
        output.id = 'designer-check';
        output.textContent = lookup('#GraphJson').value;
        document.body.append(output);
        </script>
        """;
}
