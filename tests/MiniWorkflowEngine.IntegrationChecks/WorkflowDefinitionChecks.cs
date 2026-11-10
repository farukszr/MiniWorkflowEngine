using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;

internal static partial class AuthenticationChecks
{
    private static async Task CheckWorkflowDefinitionsAsync(WebApplicationFactory<ApplicationDbContext> factory,
        HttpClient adminClient, HttpClient employeeClient)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleId = await db.Roles.Where(x => x.Name == ApplicationRoles.Approver).Select(x => x.Id).SingleAsync();
        var fields = WorkflowFields(roleId);
        using var anonymous = NewClient(factory);
        Require((await anonymous.GetAsync("/Workflows")).StatusCode == HttpStatusCode.Redirect, "Anonim süreç yönetimine erişememeli.");
        Require((await employeeClient.GetAsync("/Workflows/Create")).Headers.Location!.ToString().Contains("AccessDenied"), "Çalışan süreç oluşturamamalı.");
        var unauthorized = await PostFormAsync(employeeClient, "/", "/Workflows/Create", WorkflowFields(roleId));
        Require(unauthorized.Headers.Location!.ToString().Contains("AccessDenied"), "Çalışan doğrudan POST ile süreç oluşturamamalı.");
        Require((await adminClient.PostAsync("/Workflows/Create", new FormUrlEncodedContent(fields))).StatusCode == HttpStatusCode.BadRequest,
            "Süreç oluşturma CSRF korumalı olmalı.");
        var bad = WorkflowFields(roleId);
        bad["Steps[1].Conditions[1].Operator"] = "2";
        Require((await PostFormAsync(adminClient, "/Workflows/Create", "/Workflows/Create", bad)).StatusCode == HttpStatusCode.OK &&
            !await db.WorkflowDefinitions.AnyAsync(), "Departman için büyüktür karşılaştırması reddedilmeli.");
        bad = WorkflowFields(roleId);
        bad["Steps[1].Conditions[0].Value"] = "1.000,50";
        await PostFormAsync(adminClient, "/Workflows/Create", "/Workflows/Create", bad);
        Require(!await db.WorkflowDefinitions.AnyAsync(), "Belirsiz/binlik ayırıcılı tutar reddedilmeli.");
        bad = WorkflowFields("unknown-role");
        await PostFormAsync(adminClient, "/Workflows/Create", "/Workflows/Create", bad);
        Require(!await db.WorkflowDefinitions.AnyAsync(), "Geçersiz onaylayıcı rolü reddedilmeli.");
        bad = WorkflowFields(roleId);
        bad["Steps[1].Conditions[0].Field"] = "99";
        await PostFormAsync(adminClient, "/Workflows/Create", "/Workflows/Create", bad);
        Require(!await db.WorkflowDefinitions.AnyAsync(), "Tanımsız koşul alanı reddedilmeli.");
        Console.WriteLine("BAŞARILI: Süreç yönetiminde yetki, CSRF, tutar/departman ve rol doğrulaması.");

        fields["RowVersion"] = "";
        var created = await PostFormAsync(adminClient, "/Workflows/Create", "/Workflows/Create", fields);
        Require(created.StatusCode == HttpStatusCode.Redirect, "Süreç oluşturulamadı.");
        var entity = await db.WorkflowDefinitions.AsNoTracking().Include(x => x.Steps).ThenInclude(x => x.Conditions).SingleAsync();
        var id = entity.Id;
        var draftHome = WebUtility.HtmlDecode(await adminClient.GetStringAsync("/"));
        Require(draftHome.Contains($"/Workflows/Edit/{id}") && draftHome.Contains("Süreç taslağına devam et"),
            "İlk süreç taslakken ana sayfa doğrudan taslağın düzenleyicisini açmalı.");
        Require(entity.Code == "EXPENSE_APPROVAL" && entity.Version == 1 && !entity.IsActive && entity.PublishedAt is null &&
            entity.Steps.Count == 2 && entity.Steps.Single(x => x.Order == 2).Conditions.Any(x => x.Value == "1000.50"),
            "Taslak, sıralı adımlar ve normalize koşullar kaydedilmeli.");
        await PostFormAsync(adminClient, "/Workflows/Create", "/Workflows/Create", WorkflowFields(roleId));
        Require(await db.WorkflowDefinitions.CountAsync() == 1, "Yinelenen süreç kodu reddedilmeli.");
        var editPage = await adminClient.GetStringAsync($"/Workflows/Edit/{id}");
        Require(editPage.Contains("v1") && !editPage.Contains("v@"), "Süreç düzenleyicisinde sürüm etiketi işlenmeli.");
        var oldStamp = HiddenValue(editPage, "RowVersion");
        var updated = WorkflowFields(roleId);
        updated["Id"] = id.ToString();
        updated["RowVersion"] = oldStamp;
        updated["Steps[0].Name"] = "Finans önce";
        updated["Steps[1].Name"] = "Yönetici sonra";
        Require((await PostFormAsync(adminClient, $"/Workflows/Edit/{id}", $"/Workflows/Edit/{id}", updated)).StatusCode == HttpStatusCode.Redirect,
            "Taslak güncellenemedi.");
        Require(await db.WorkflowSteps.CountAsync(x => x.WorkflowDefinitionId == id) == 2 && await db.StepConditions.CountAsync() == 2,
            "Taslak güncellemesi eski alt kayıtları bırakmamalı.");
        updated["Name"] = "Eski form değişikliği";
        var staleEdit = await PostFormAsync(adminClient, $"/Workflows/Edit/{id}", $"/Workflows/Edit/{id}", updated);
        Require(staleEdit.StatusCode == HttpStatusCode.OK && !(await db.WorkflowDefinitions.AsNoTracking().SingleAsync()).Name.Contains("Eski form"),
            "Eski taslak formu yeni sürümü ezmemeli.");
        Require((await adminClient.GetStringAsync("/Workflows?search=EXPENSE_APPROVAL&page=999")).Contains("EXPENSE_APPROVAL"), "Süreç arama/sayfalama çalışmalı.");
        Console.WriteLine("BAŞARILI: Taslak oluşturma/düzenleme, alt kayıt yenileme, yinelenen kod ve eski form engeli.");

        await WorkflowCommandAsync(adminClient, id, "Publish", oldStamp);
        Require(!await db.WorkflowDefinitions.AnyAsync(x => x.IsActive), "Eski sürüm bilgisiyle yayımlama reddedilmeli.");
        var details = await adminClient.GetStringAsync($"/Workflows/Details/{id}");
        Require(details.Contains("v1") && !details.Contains("v@"), "Süreç detayında sürüm etiketi işlenmeli.");
        await WorkflowCommandAsync(adminClient, id, "Publish", HiddenValue(details, "RowVersion"));
        entity = await db.WorkflowDefinitions.AsNoTracking().SingleAsync();
        Require(entity.IsActive && entity.PublishedAt is not null, "Taslak yayımlanmalı.");
        updated["RowVersion"] = Convert.ToBase64String(entity.RowVersion);
        updated["Name"] = "Yayımlanmış tanımı boz";
        await PostFormAsync(adminClient, $"/Workflows/Details/{id}", $"/Workflows/Edit/{id}", updated);
        Require(!(await db.WorkflowDefinitions.AsNoTracking().SingleAsync()).Name.Contains("boz"), "Yayımlanmış süreç düzenlenememeli.");

        var employeeId = await db.Users.Where(x => x.Email == "employee@example.invalid").Select(x => x.Id).SingleAsync();
        db.WorkflowRequests.Add(new WorkflowRequest { WorkflowDefinitionId = id, RequesterId = employeeId,
            Title = "Sürüm koruma testi", Description = "Eski talep eski tanıma bağlı kalmalıdır." });
        await db.SaveChangesAsync();
        var cloneResponse = await WorkflowCommandAsync(adminClient, id, "NewVersion", Convert.ToBase64String(entity.RowVersion));
        Require(cloneResponse.StatusCode == HttpStatusCode.Redirect && cloneResponse.Headers.Location!.ToString().Contains("/Edit/"), "Yeni taslak sürüm oluşturulmalı.");
        var clone = await db.WorkflowDefinitions.AsNoTracking().Include(x => x.Steps).ThenInclude(x => x.Conditions).SingleAsync(x => x.Version == 2);
        Require(!clone.IsActive && clone.PublishedAt is null && clone.Steps.Count == 2 && clone.Steps.Sum(x => x.Conditions.Count) == 2,
            "Yeni sürüm adım ve koşulları bağımsız kopyalamalı.");
        var activeList = await adminClient.GetStringAsync("/Workflows?state=Active");
        var draftList = await adminClient.GetStringAsync("/Workflows?state=Draft&search=EXPENSE_APPROVAL");
        Require(activeList.Contains($"href=\"/Workflows/Details/{id}\"") && !activeList.Contains($"href=\"/Workflows/Details/{clone.Id}\"") &&
            draftList.Contains($"href=\"/Workflows/Details/{clone.Id}\"") && !draftList.Contains($"href=\"/Workflows/Details/{id}\""),
            "Etkin ve taslak süreç filtreleri aramayla birlikte doğru sürümleri göstermeli.");
        await WorkflowCommandAsync(adminClient, id, "NewVersion", Convert.ToBase64String(entity.RowVersion));
        Require(await db.WorkflowDefinitions.CountAsync() == 2, "Bir süreç için ikinci açık taslak oluşturulmamalı.");
        await WorkflowCommandAsync(adminClient, clone.Id, "Publish", Convert.ToBase64String(clone.RowVersion));
        Require(await db.WorkflowDefinitions.CountAsync(x => x.IsActive) == 1 && await db.WorkflowDefinitions.AnyAsync(x => x.Id == clone.Id && x.IsActive),
            "Yayımlama eski sürümü arşivleyip tek etkin sürümü korumalı.");
        var archivedList = await adminClient.GetStringAsync("/Workflows?state=Archived");
        Require(archivedList.Contains($"href=\"/Workflows/Details/{id}\"") && !archivedList.Contains($"href=\"/Workflows/Details/{clone.Id}\""),
            "Arşiv filtresi yalnızca arşivlenen sürümü göstermeli.");
        Require(await db.WorkflowRequests.AnyAsync(x => x.WorkflowDefinitionId == id), "Eski talep eski sürümünde kalmalı.");
        Console.WriteLine("BAŞARILI: Yayımlama, değişmez tanım, bağımsız yeni sürüm, tek etkin sürüm ve eski talep bağlantısı.");

        clone = await db.WorkflowDefinitions.AsNoTracking().SingleAsync(x => x.Id == clone.Id);
        await WorkflowCommandAsync(adminClient, clone.Id, "Deactivate", Convert.ToBase64String(clone.RowVersion));
        Require(!await db.WorkflowDefinitions.AnyAsync(x => x.IsActive), "Devre dışı bırakma başarısız.");
        var inactive = await adminClient.GetAsync($"/Workflows/Edit/{clone.Id}");
        Require(inactive.StatusCode == HttpStatusCode.Redirect, "Arşivlenmiş süreç taslağa dönüşmemeli.");
        var inactiveEntity = await db.WorkflowDefinitions.AsNoTracking().SingleAsync(x => x.Id == clone.Id);
        await WorkflowCommandAsync(adminClient, clone.Id, "Publish", Convert.ToBase64String(inactiveEntity.RowVersion));
        Require(!await db.WorkflowDefinitions.AnyAsync(x => x.IsActive), "Arşiv sürüm tekrar yayımlanmamalı; yeni sürüm gerekir.");
        Require((await adminClient.GetAsync("/Workflows/Details/2147483647")).StatusCode == HttpStatusCode.NotFound, "Bulunamayan süreç 404 dönmeli.");
        Console.WriteLine("BAŞARILI: Devre dışı bırakılan tanımın değişmezliği ve bulunamayan süreç davranışı.");
    }

    private static Dictionary<string, string> WorkflowFields(string roleId) => new()
    {
        ["Id"] = "0", ["Code"] = "expense_approval", ["Name"] = "Masraf Onayı", ["Description"] = "Koşullu masraf süreci",
        ["Steps[0].Name"] = "Yönetici Onayı", ["Steps[0].ApproverRoleId"] = roleId,
        ["Steps[1].Name"] = "Finans Onayı", ["Steps[1].ApproverRoleId"] = roleId,
        ["Steps[1].Conditions[0].Field"] = "0", ["Steps[1].Conditions[0].Operator"] = "2", ["Steps[1].Conditions[0].Value"] = "1000,50",
        ["Steps[1].Conditions[1].Field"] = "1", ["Steps[1].Conditions[1].Operator"] = "0", ["Steps[1].Conditions[1].Value"] = "Finans"
    };

    private static Task<HttpResponseMessage> WorkflowCommandAsync(HttpClient client, int id, string action, string stamp)
        => PostFormAsync(client, $"/Workflows/Details/{id}", $"/Workflows/{action}/{id}",
            new Dictionary<string, string> { ["Id"] = id.ToString(), ["RowVersion"] = stamp });
}
