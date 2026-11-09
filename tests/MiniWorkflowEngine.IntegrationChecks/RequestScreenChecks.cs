using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Domain.Enums;

internal static partial class AuthenticationChecks
{
    private static async Task CheckRequestScreensAsync(WebApplicationFactory<ApplicationDbContext> factory,
        HttpClient adminClient, HttpClient ownerClient)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await users.FindByEmailAsync("employee@example.invalid");
        Require((await users.AddToRoleAsync(owner!, ApplicationRoles.Approver)).Succeeded, "Kendi talebi onay engeli hazırlanamadı.");
        var approver = new ApplicationUser { UserName = "screen-approver@example.invalid", Email = "screen-approver@example.invalid", FullName = "Ekran Onaylayıcı" };
        var outsider = new ApplicationUser { UserName = "screen-outsider@example.invalid", Email = "screen-outsider@example.invalid", FullName = "Diğer Çalışan" };
        Require((await users.CreateAsync(approver, Password)).Succeeded && (await users.AddToRoleAsync(approver, ApplicationRoles.Approver)).Succeeded, "Ekran onaylayıcısı oluşturulamadı.");
        Require((await users.CreateAsync(outsider, Password)).Succeeded && (await users.AddToRoleAsync(outsider, ApplicationRoles.Employee)).Succeeded, "Diğer çalışan oluşturulamadı.");
        using var approverClient = NewClient(factory);
        using var outsiderClient = NewClient(factory);
        await LoginAsync(approverClient, approver.Email!, Password);
        await LoginAsync(outsiderClient, outsider.Email!, Password);
        using var anonymous = NewClient(factory);
        foreach (var path in new[] { "/Requests", "/Requests/Create", "/Requests/Inbox" })
            Require((await anonymous.GetAsync(path)).Headers.Location!.ToString().Contains("/Account/Login"), "Talep ekranları giriş gerektirmeli.");
        Require(WebUtility.HtmlDecode(await ownerClient.GetStringAsync("/Requests/Create")).Contains("Henüz etkin"), "Etkin süreç yokken açıklayıcı boş durum gösterilmeli.");

        var approvalRole = await db.Roles.Where(x => x.Name == ApplicationRoles.Approver).Select(x => x.Id).SingleAsync();
        var workflow = new WorkflowDefinition { Code = "SCREEN_TEST", Name = "Ekran Masraf Süreci", Version = 1, IsActive = true, PublishedAt = DateTimeOffset.UtcNow,
            Steps = new List<WorkflowStep> { new() { Name = "Ekran Onayı", Order = 1, ApproverRoleId = approvalRole,
                Conditions = new List<StepCondition> { new() { Field = ConditionField.Amount, Operator = ConditionOperator.GreaterThan, Value = "10.00" },
                    new() { Field = ConditionField.Department, Operator = ConditionOperator.Equals, Value = "Finans" } } } } };
        db.Add(workflow);
        await db.SaveChangesAsync();
        var form = await ownerClient.GetStringAsync("/Requests/Create");
        Require(form.Contains($"<option value=\"{workflow.Id}\" selected=\"selected\"") ||
            System.Text.RegularExpressions.Regex.IsMatch(form, $"<option value=\"{workflow.Id}\"[^>]*selected"),
            "Tek etkin süreç talep formunda seçili gelmeli.");
        var previewUrl = $"/Workflows/Preview/{workflow.Id}?evaluate=true";
        var skippedPreview = WebUtility.HtmlDecode(await adminClient.GetStringAsync(previewUrl + "&amount=9&department=Finans"));
        Require(skippedPreview.Contains("Talep otomatik onaylanır") && skippedPreview.Contains("Atlanır"),
            "Eşiğin altındaki örnek tutar otomatik onay öngörmeli.");
        var waitingPreview = WebUtility.HtmlDecode(await adminClient.GetStringAsync(previewUrl + "&amount=10,01&department=Finans"));
        Require(waitingPreview.Contains("<strong>Onaylayıcı</strong>") && waitingPreview.Contains("Onay beklenir"),
            "Koşulu sağlayan tutar ve departman onay gerektirmeli.");
        var incompletePreview = WebUtility.HtmlDecode(await adminClient.GetStringAsync(previewUrl + "&amount=15"));
        Require(incompletePreview.Contains("Örnek departmanı girin") && !incompletePreview.Contains("Beklenen sonuç"),
            "Eksik koşul alanıyla önizleme sonucu gösterilmemeli.");
        Require((await ownerClient.GetAsync(previewUrl)).Headers.Location?.ToString().Contains("/Account/AccessDenied") == true,
            "Yönetici olmayan kullanıcı süreç önizlemesine erişememeli.");
        Require(!await db.WorkflowRequests.AnyAsync(x => x.WorkflowDefinitionId == workflow.Id), "Önizleme kayıt oluşturmamalı.");
        var title = "Ekran talebi - masraf";
        var fields = RequestFields(workflow.Id, title);
        Require((await ownerClient.PostAsync("/Requests/Create", new FormUrlEncodedContent(fields))).StatusCode == HttpStatusCode.BadRequest, "Talep oluşturma CSRF gerektirmeli.");
        foreach (var badAmount in new[] { "1.000,50", "1e3", "-1", "2,001" })
        {
            var invalid = RequestFields(workflow.Id, "Geçersiz tutar");
            invalid["Amount"] = badAmount;
            Require((await PostFormAsync(ownerClient, "/Requests/Create", "/Requests/Create", invalid)).StatusCode == HttpStatusCode.OK,
                "Hatalı tutar formda gösterilmeli.");
        }
        Require(!await db.WorkflowRequests.AnyAsync(x => x.Title == "Geçersiz tutar"), "Hatalı tutarlı talep kaydedilmemeli.");
        fields["RequesterId"] = outsider.Id;
        fields["Status"] = "Approved";
        fields["CurrentStepId"] = "999";
        fields["RowVersion"] = "";
        var created = await PostFormAsync(ownerClient, "/Requests/Create", "/Requests/Create", fields);
        Require(created.StatusCode == HttpStatusCode.Redirect, "Talep formundan taslak oluşturulamadı.");
        var request = await db.WorkflowRequests.AsNoTracking().SingleAsync(x => x.Title == title);
        var id = request.Id;
        var ownerHome = WebUtility.HtmlDecode(await ownerClient.GetStringAsync("/"));
        Require(ownerHome.Contains("Taslağınıza devam edin") && ownerHome.Contains($"/Requests/Details/{id}?scope=Mine"),
            "Kullanıcının ana sayfası kendi açık taslağına bağlantı vermeli.");
        Require(request.RequesterId == owner!.Id && request.Status == RequestStatus.Draft && request.CurrentStepId is null && request.Amount == 1000.50m,
            "Formdan sahip/durum atanamamalı; noktalı tutar doğru ayrıştırılmalı.");
        var detail = await ownerClient.GetStringAsync($"/Requests/Details/{id}");
        Require(detail.Contains("v1") && !detail.Contains("v@"), "Talep detayında sürüm Razor metni yerine v1 gösterilmeli.");
        Require(!detail.Contains("<script>alert('x')</script>") && detail.Contains("&lt;script&gt;"), "Talep açıklaması HTML olarak çalıştırılmamalı.");
        Require((await outsiderClient.GetAsync($"/Requests/Details/{id}")).StatusCode == HttpStatusCode.NotFound, "İlgisiz kullanıcı detay okuyamamalı.");
        Require((await outsiderClient.GetAsync($"/Requests/Edit/{id}")).StatusCode == HttpStatusCode.NotFound, "İlgisiz kullanıcı taslak düzenleyememeli.");
        Require((await adminClient.GetAsync($"/Requests/Edit/{id}")).StatusCode == HttpStatusCode.Forbidden, "Yönetici başka kullanıcının taslağını düzenleyememeli.");
        var editPage = await ownerClient.GetStringAsync($"/Requests/Edit/{id}");
        Require(editPage.Contains("v1") && !editPage.Contains("v@"), "Talep düzenleyicisinde süreç sürümü gösterilmeli.");
        var edit = RequestFields(workflow.Id, title);
        edit["Id"] = id.ToString();
        edit["RowVersion"] = HiddenValue(editPage, "RowVersion");
        edit["Amount"] = "2000,75";
        Require((await PostFormAsync(ownerClient, $"/Requests/Edit/{id}", $"/Requests/Edit/{id}", edit)).StatusCode == HttpStatusCode.Redirect, "Taslak düzenlenemedi.");
        Require((await PostFormAsync(ownerClient, $"/Requests/Edit/{id}", $"/Requests/Edit/{id}", edit)).StatusCode == HttpStatusCode.Conflict, "Eski taslak formu 409 dönmeli.");
        Require((await db.WorkflowRequests.AsNoTracking().SingleAsync(x => x.Id == id)).Amount == 2000.75m, "Virgüllü tutar doğru kaydedilmeli.");
        var requestList = await ownerClient.GetStringAsync("/Requests?search=Ekran%20talebi&status=0&page=999");
        Require(requestList.Contains(title) && requestList.Contains("v1") && !requestList.Contains("v@"), "Talep arama, durum filtresi, sayfa sınırı ve sürüm etiketi çalışmalı.");
        Require(!(await outsiderClient.GetStringAsync("/Requests")).Contains(title), "Taleplerim başka kullanıcının kaydını göstermemeli.");
        Console.WriteLine("BAŞARILI: Talep ekranlarında giriş/CSRF, tutar kültürü, güvenli sahiplik, HTML kodlama ve taslak düzenleme.");

        Require((await PostRequestActionAsync(ownerClient, id, "Submit")).StatusCode == HttpStatusCode.Redirect, "Taslak onaya gönderilemedi.");
        Require(await PendingCountAsync(approverClient) == 1 && await PendingCountAsync(ownerClient) == 0 &&
            await PendingCountAsync(adminClient) == 0 && await PendingCountAsync(outsiderClient) == 0,
            "Bildirim sayısı yalnızca kullanıcının karar verebileceği talepleri içermeli.");
        var home = WebUtility.HtmlDecode(await approverClient.GetStringAsync("/"));
        Require(home.Contains("data-approval-url=\"/Requests/PendingCount\"") &&
            home.Contains("Bekleyen onaylar") && home.Contains("Kararınızı bekleyen 1 talep var") && !home.Contains("approval-indicator"),
            "Onay sayacı ana menüde bulunmalı, üst çubukta ayrı bir onay düğmesi olmamalı.");
        Require(!(await ownerClient.GetStringAsync("/Requests/Inbox")).Contains(title), "Kendi talebi onay kutusunda görünmemeli.");
        var inboxDetailUrl = $"/Requests/Details/{id}?scope=Inbox";
        var inboxPage = await approverClient.GetStringAsync("/Requests/Inbox?search=Ekran%20talebi");
        Require(inboxPage.Contains(title) && inboxPage.Contains(inboxDetailUrl), "Onay kutusu detay bağlantısında liste bağlamını korumalı.");
        Require(!(await outsiderClient.GetStringAsync("/Requests/Inbox")).Contains(title), "İlgisiz rolün kutusu talebi göstermemeli.");
        Require((await PostRequestActionAsync(ownerClient, id, "Approve")).StatusCode == HttpStatusCode.Forbidden, "Talep sahibi HTTP üzerinden kendi talebine karar verememeli.");
        var pendingDetail = await approverClient.GetStringAsync(inboxDetailUrl);
        Require(WebUtility.HtmlDecode(pendingDetail).Contains("← Bekleyen onaylar") &&
            System.Text.RegularExpressions.Regex.IsMatch(pendingDetail,
                "<a class=\"workspace-link is-active\"[^>]*href=\"/Requests/Inbox\""),
            "Onay detayından bekleyen onaylara dönülebilmeli ve menü seçimi korunmalı.");
        var wrongVersion = new Dictionary<string, string> { ["Id"] = id.ToString(), ["RowVersion"] = "invalid-base64" };
        Require((await PostFormAsync(approverClient, $"/Requests/Details/{id}", $"/Requests/Approve/{id}", wrongVersion)).StatusCode == HttpStatusCode.Conflict,
            "Hatalı sürüm 409 dönmeli.");
        var missingCsrf = await approverClient.PostAsync($"/Requests/Approve/{id}", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Id"] = id.ToString(), ["RowVersion"] = HiddenValue(pendingDetail, "RowVersion") }));
        Require(missingCsrf.StatusCode == HttpStatusCode.BadRequest, "Karar endpoint'i CSRF gerektirmeli.");
        var mismatch = new Dictionary<string, string> { ["Id"] = "2147483647", ["RowVersion"] = HiddenValue(pendingDetail, "RowVersion") };
        Require((await PostFormAsync(approverClient, $"/Requests/Details/{id}", $"/Requests/Approve/{id}", mismatch)).StatusCode == HttpStatusCode.BadRequest,
            "URL/form kimliği eşleşmeli.");
        var approval = await PostFormAsync(approverClient, inboxDetailUrl, $"/Requests/Approve/{id}",
            new Dictionary<string, string> { ["Id"] = id.ToString(), ["RowVersion"] = HiddenValue(pendingDetail, "RowVersion"),
                ["Scope"] = HiddenValue(pendingDetail, "Scope"), ["Comment"] = "Uygun." });
        Require(approval.StatusCode == HttpStatusCode.Redirect && approval.Headers.Location?.ToString() == inboxDetailUrl,
            "Onaydan sonra detay sayfası bekleyen onaylar bağlamında kalmalı.");
        var done = await ownerClient.GetStringAsync($"/Requests/Details/{id}");
        Require(WebUtility.HtmlDecode(done).Contains("Süreç tamamlandı") && !done.Contains(">Onaya gönder<"), "Tamamlanan talep geçmişi ve eylemleri doğru gösterilmeli.");
        Require(!(await approverClient.GetStringAsync("/Requests/Inbox")).Contains(title), "Tamamlanan talep kutudan çıkmalı.");
        Require(await PendingCountAsync(approverClient) == 0, "Tamamlanan talep bildirim sayısından düşmeli.");
        Console.WriteLine("BAŞARILI: Rol bazlı onay kutusu, kendi talebine karar engeli, eski sürüm ve HTTP onay/geçmiş akışı.");

        var rejectedId = await CreateScreenDraftAsync(db, ownerClient, workflow.Id, "Ekran ret talebi");
        await PostRequestActionAsync(ownerClient, rejectedId, "Submit");
        var missingReason = await PostRequestActionAsync(approverClient, rejectedId, "Reject");
        Require(missingReason.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await missingReason.Content.ReadAsStringAsync()).Contains("Ret gerekçesi girin"),
            "Ret gerekçesi hatası detayda gösterilmeli.");
        Require((await db.WorkflowRequests.AsNoTracking().SingleAsync(x => x.Id == rejectedId)).Status == RequestStatus.Pending, "Boş gerekçeli ret durumu değiştirmemeli.");
        await PostRequestActionAsync(approverClient, rejectedId, "Reject", "Eksik belge.");
        Require((await db.WorkflowRequests.AsNoTracking().SingleAsync(x => x.Id == rejectedId)).Status == RequestStatus.Rejected, "Gerekçeli ret kaydedilmeli.");

        var allRequests = await adminClient.GetStringAsync("/Requests/All");
        Require(allRequests.Contains(title) && WebUtility.HtmlDecode(allRequests).Contains("Ekran ret talebi"),
            "Yönetici diğer kullanıcıların onaylanan ve reddedilen taleplerini görebilmeli.");
        var approvedRequests = await adminClient.GetStringAsync($"/Requests/All?status={(int)RequestStatus.Approved}");
        Require(approvedRequests.Contains(title) && !WebUtility.HtmlDecode(approvedRequests).Contains("Ekran ret talebi"),
            "Onaylandı filtresi reddedilen talepleri dışarıda bırakmalı.");
        var rejectedRequests = await adminClient.GetStringAsync($"/Requests/All?status={(int)RequestStatus.Rejected}");
        Require(!rejectedRequests.Contains(title) && WebUtility.HtmlDecode(rejectedRequests).Contains("Ekran ret talebi"),
            "Reddedildi filtresi onaylanan talepleri dışarıda bırakmalı.");
        var deniedAllRequests = await ownerClient.GetAsync("/Requests/All");
        Require(deniedAllRequests.StatusCode == HttpStatusCode.Redirect && deniedAllRequests.Headers.Location?.ToString().Contains("/Account/AccessDenied") == true,
            "Yönetici olmayan kullanıcı tüm talep listesine erişememeli.");
        var rejectedDetails = await adminClient.GetStringAsync($"/Requests/Details/{rejectedId}");
        Require(WebUtility.HtmlDecode(rejectedDetails).Contains("Eksik belge."), "Yönetici talebin ret gerekçesini görebilmeli.");
        var cancelId = await CreateScreenDraftAsync(db, ownerClient, workflow.Id, "Ekran iptal talebi");
        await PostRequestActionAsync(ownerClient, cancelId, "Cancel", "Vazgeçildi.");
        Require((await db.WorkflowRequests.AsNoTracking().SingleAsync(x => x.Id == cancelId)).Status == RequestStatus.Cancelled, "HTTP iptal akışı çalışmalı.");
        var missingId = await CreateScreenDraftAsync(db, ownerClient, workflow.Id, "Ekran eksik departman", missingDepartment: true);
        Require((await PostRequestActionAsync(ownerClient, missingId, "Submit")).StatusCode == HttpStatusCode.OK &&
            (await db.WorkflowRequests.AsNoTracking().SingleAsync(x => x.Id == missingId)).Status == RequestStatus.Draft,
            "Koşul alanı eksikse gönderim hata verip taslağı korumalı.");
        Require((await ownerClient.GetAsync("/Requests/Details/2147483647")).StatusCode == HttpStatusCode.NotFound, "Olmayan talep 404 dönmeli.");
        Console.WriteLine("BAŞARILI: Talep detayından gerekçeli ret, iptal, eksik koşul alanı ve bulunamayan kayıt davranışı.");
    }

    private static async Task<int> PendingCountAsync(HttpClient client)
    {
        var summary = await client.GetFromJsonAsync<Dictionary<string, int>>("/Requests/PendingCount");
        return summary!["count"];
    }

    private static Dictionary<string, string> RequestFields(int workflowId, string title) => new()
    {
        ["Id"] = "0", ["WorkflowDefinitionId"] = workflowId.ToString(), ["Title"] = title,
        ["Description"] = "<script>alert('x')</script>", ["Amount"] = "1000.50", ["Department"] = "Finans"
    };
    private static async Task<int> CreateScreenDraftAsync(ApplicationDbContext db, HttpClient owner, int workflowId, string title, bool missingDepartment = false)
    {
        var fields = RequestFields(workflowId, title);
        if (missingDepartment) fields["Department"] = "";
        Require((await PostFormAsync(owner, "/Requests/Create", "/Requests/Create", fields)).StatusCode == HttpStatusCode.Redirect, "Ekran test taslağı oluşturulamadı.");
        return await db.WorkflowRequests.Where(x => x.Title == title).Select(x => x.Id).SingleAsync();
    }
    private static async Task<HttpResponseMessage> PostRequestActionAsync(HttpClient client, int id, string action, string? comment = null)
    {
        var page = await client.GetStringAsync($"/Requests/Details/{id}");
        return await client.PostAsync($"/Requests/{action}/{id}", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Id"] = id.ToString(), ["RowVersion"] = HiddenValue(page, "RowVersion"), ["Comment"] = comment ?? "", ["__RequestVerificationToken"] = Token(page) }));
    }
}
