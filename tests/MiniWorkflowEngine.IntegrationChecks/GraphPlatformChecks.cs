using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.Services.Workflow;

internal static partial class AuthenticationChecks
{
    private static async Task CheckGraphPlatformAsync(WebApplicationFactory<ApplicationDbContext> factory,
        HttpClient admin, HttpClient requester)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var approver = await db.Users.AsNoTracking().SingleAsync(x => x.Email == "screen-approver@example.invalid");
        var employee = await db.Users.AsNoTracking().SingleAsync(x => x.Email == "employee@example.invalid");
        const string customRole = "Grafik görev kontrolü";
        Require((await PostFormAsync(admin, "/Users/RoleCatalog", "/Users/AddRole", new() { ["name"] = customRole })).StatusCode == HttpStatusCode.Redirect,
            "Özel görev rolü oluşturulamadı.");
        var role = await db.Roles.AsNoTracking().SingleAsync(x => x.Name == customRole);
        var rolesPage = await admin.GetStringAsync($"/Users/Roles/{approver.Id}");
        Require((await PostRolesAsync(admin, approver.Id, rolesPage, [ApplicationRoles.Approver, customRole])).StatusCode == HttpStatusCode.Redirect &&
            await db.UserRoles.AnyAsync(x => x.UserId == approver.Id && x.RoleId == role.Id),
            "Özel görev rolü kullanıcıya atanamadı.");
        Require((await PostFormAsync(admin, "/Departments", "/Departments/Save", new() { ["name"] = "Grafik Test Birimi" })).StatusCode == HttpStatusCode.Redirect,
            "Departman oluşturulamadı.");
        var department = await db.Departments.AsNoTracking().SingleAsync(x => x.Name == "Grafik Test Birimi");
        foreach (var user in new[] { approver, employee })
        {
            Require((await PostFormAsync(admin, "/Departments", "/Departments/Assign",
                new() { ["userId"] = user.Id, ["departmentId"] = department.Id.ToString() })).StatusCode == HttpStatusCode.Redirect,
                "Departman üyeliği kaydedilemedi.");
        }
        var start = Node(NodeKind.Start, "Başla");
        var decision = Node(NodeKind.Decision, "Önceliği kontrol et");
        var task = Node(NodeKind.Task, "Belgeleri incele") with { RoleId = role.Id, UserId = approver.Id, DepartmentId = department.Id };
        var approval = Node(NodeKind.Approval, "Son karar") with { RoleId = role.Id, DepartmentId = department.Id, AllowReturn = true };
        var end = Node(NodeKind.End, "Tamamlandı");
        var reject = Node(NodeKind.End, "Reddedildi") with { Outcome = EndOutcome.Rejected };
        var graph = new WorkflowGraph([start, decision, task, approval, end, reject],
            [new(start.Id, decision.Id, RouteKind.Next), new(decision.Id, task.Id, RouteKind.Match, "priority", ConditionOperator.Equals, "Acil"),
             new(decision.Id, end.Id, RouteKind.Otherwise), new(task.Id, approval.Id, RouteKind.Complete),
             new(approval.Id, end.Id, RouteKind.Approve), new(approval.Id, reject.Id, RouteKind.Reject)],
            [new("priority", "Öncelik", FieldKind.Choice, true, ["Acil", "Normal"])]);
        Require(GraphRules.Validate(graph, publishing: true) is null &&
            GraphRules.Validate(graph with { Routes = graph.Routes.Where(x => x.Kind != RouteKind.Otherwise).ToArray() }, publishing: true) is not null &&
            GraphRules.Validate(graph with { Nodes = [.. graph.Nodes, Node(NodeKind.End, "Kopuk bitiş")] }, publishing: true) is not null,
            "Grafik doğrulayıcı geçerli tasarımı kabul etmeli, eksik varsayılan yolu ve kopuk düğümü reddetmeli.");
        var designer = new Dictionary<string, string> { ["Id"] = "0", ["Code"] = "GRAPH_CHECK",
            ["Name"] = "Grafik entegrasyon süreci", ["GraphJson"] = graph.Write() };
        Require((await PostFormAsync(admin, "/Workflows/Designer", "/Workflows/Designer", designer)).StatusCode == HttpStatusCode.Redirect,
            "Görsel taslak kaydedilemedi.");
        var workflow = await db.WorkflowDefinitions.AsNoTracking().SingleAsync(x => x.Code == "GRAPH_CHECK");
        var definitionUrl = $"/Workflows/Details/{workflow.Id}";
        var previewUrl = $"/Workflows/GraphPreview/{workflow.Id}";
        var preview = await PostFormAsync(admin, previewUrl, previewUrl, new() { ["FormValues[priority]"] = "Acil" });
        Require(WebUtility.HtmlDecode(await preview.Content.ReadAsStringAsync()).Contains("İlk görev: Belgeleri incele"),
            "Önizleme, yayımlanacak grafiğin form koşulunu gerçek motorla aynı şekilde değerlendirmeli.");
        var detail = await admin.GetStringAsync(definitionUrl);
        Require((await WorkflowCommandAsync(admin, workflow.Id, "Publish", HiddenValue(detail, "RowVersion"))).StatusCode == HttpStatusCode.Redirect &&
            await db.WorkflowDefinitions.AnyAsync(x => x.Id == workflow.Id && x.IsActive), "Grafik yayımlanamadı.");
        Require(await db.WorkflowSteps.CountAsync(x => x.WorkflowDefinitionId == workflow.Id && x.GraphNodeId != null) == 2,
            "Görevler yayımlanmış düğümlere bağlanmalı.");

        const string title = "Grafik iade ve yeniden gönderim";
        var form = RequestFields(workflow.Id, title);
        form["FormValues[priority]"] = "Acil";
        Require((await PostFormAsync(requester, "/Requests/Create", "/Requests/Create", form)).StatusCode == HttpStatusCode.Redirect,
            "Dinamik form kaydedilemedi.");
        var requestId = await db.WorkflowRequests.Where(x => x.Title == title).Select(x => x.Id).SingleAsync();
        Require((await PostRequestActionAsync(requester, requestId, "Submit")).StatusCode == HttpStatusCode.Redirect,
            "Grafik talebi başlatılamadı.");
        using var assignee = NewClient(factory);
        await LoginAsync(assignee, approver.Email!, Password);
        var taskUrl = $"/Requests/Details/{requestId}";
        Require(WebUtility.HtmlDecode(await assignee.GetStringAsync(taskUrl)).Contains("Görevi tamamla"),
            "Koşul form değişkenini okuyarak atanmış kullanıcı görevine ulaşmalı.");
        Require((await PostRequestActionAsync(assignee, requestId, "CompleteTask")).StatusCode == HttpStatusCode.Redirect &&
            (await PostRequestActionAsync(assignee, requestId, "Return", "Belgeyi düzeltin.")).StatusCode == HttpStatusCode.Redirect,
            "Görev tamamlanıp onay adımından iade edilebilmeli.");
        var returned = WebUtility.HtmlDecode(await requester.GetStringAsync(taskUrl));
        Require(returned.Contains("Talebi düzelt") && returned.Contains("Belgeyi düzeltin."), "İade gerekçesi talep sahibine gösterilmeli.");
        Require(WebUtility.HtmlDecode(await requester.GetStringAsync("/Requests/Inbox")).Contains(title),
            "Düzeltme görevi talep sahibinin kutusunda olmalı.");
        Require((await PostRequestActionAsync(requester, requestId, "Submit")).StatusCode == HttpStatusCode.Redirect &&
            (await PostRequestActionAsync(assignee, requestId, "Approve")).StatusCode == HttpStatusCode.Redirect,
            "Yeniden gönderim iade eden onaya dönmeli.");
        var finished = await db.WorkflowRequests.AsNoTracking().Include(x => x.Actions).SingleAsync(x => x.Id == requestId);
        Require(finished.Status == RequestStatus.Approved && finished.RequesterDepartmentId == department.Id &&
            finished.Actions.Any(x => x.ActionType == RequestActionType.RouteSelected) &&
            finished.Actions.Any(x => x.ActionType == RequestActionType.Resubmitted), "Grafik sonucu, departman ve geçmiş birlikte korunmalı.");
        var report = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administration/Reports?departmentId={department.Id}&code=GRAPH_CHECK"));
        Require(report.Contains("Grafik entegrasyon süreci") && !report.Contains("Ekran Masraf Süreci"), "Rapor departman ve süreç filtresini uygulamalı.");
        Require((await requester.GetAsync("/Administration/Reports")).StatusCode == HttpStatusCode.Redirect &&
            (await assignee.GetAsync("/Administration/Reports")).StatusCode == HttpStatusCode.Redirect,
            "Rapor yalnız yetkili kullanıcıya açılmalı.");
        Console.WriteLine("BAŞARILI: Görsel grafik, form değişkeni, departman ataması, görev, iade ve rapor uçtan uca.");

        static GraphNode Node(NodeKind kind, string name) => new(Guid.NewGuid(), kind, name, 0, 0);
    }
}
