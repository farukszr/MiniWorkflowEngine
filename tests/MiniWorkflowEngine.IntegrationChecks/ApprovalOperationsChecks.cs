using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Domain.Enums;

internal static partial class AuthenticationChecks
{
    private static async Task CheckApprovalOperationsAsync(WebApplicationFactory<ApplicationDbContext> factory,
        HttpClient admin, HttpClient requester)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var outsider = (await users.FindByEmailAsync("screen-outsider@example.invalid"))!;
        var administrator = (await users.FindByEmailAsync("admin@example.invalid"))!;
        var workflowId = await db.WorkflowDefinitions.Where(x => x.Code == "SCREEN_TEST").Select(x => x.Id).SingleAsync();
        using var outsiderClient = NewClient(factory);
        await LoginAsync(outsiderClient, outsider.Email!, Password);

        const string lateTitle = "Geciken yönetici testi";
        var late = await CreateScreenDraftAsync(db, requester, workflowId, lateTitle);
        var fresh = await CreateScreenDraftAsync(db, requester, workflowId, "Yeni yönetici testi");
        await PostRequestActionAsync(requester, late, "Submit");
        await PostRequestActionAsync(requester, fresh, "Submit");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE RequestActions SET CreatedAt = {DateTimeOffset.UtcNow.AddHours(-25)}
            WHERE WorkflowRequestId = {late} AND ActionType = {"Submitted"}
            """);
        var overdue = WebUtility.HtmlDecode(await admin.GetStringAsync("/Requests/All?overdue=true"));
        Require(overdue.Contains(lateTitle) && overdue.Contains("Gecikti") && !overdue.Contains("Yeni yönetici testi"),
            "Geciken filtresi talep oluşturulmasını değil, onay adımının başlangıcını kullanmalı.");

        Require(await PendingCountAsync(outsiderClient) == 0 &&
            (await outsiderClient.GetAsync($"/Requests/Details/{late}")).StatusCode == HttpStatusCode.NotFound,
            "İlgisiz kullanıcı talebi okuyamamalı.");
        Require(await PendingCountAsync(admin) == 0 &&
            WebUtility.HtmlDecode(await admin.GetStringAsync($"/Requests/Details/{late}?scope=All")).Contains("yönetici olarak karar veriyorsunuz"),
            "Yönetici müdahalesi detayda görünmeli ve rol bazlı kutuya eklenmemeli.");
        Require((await PostRequestActionAsync(admin, late, "Approve")).StatusCode == HttpStatusCode.Redirect &&
            await db.RequestActions.AnyAsync(x => x.WorkflowRequestId == late && x.ActionType == RequestActionType.AdministratorApproved &&
                x.ActorId == administrator.Id), "Yönetici onayı kendi adıyla kaydedilmeli.");
        Require((await PostRequestActionAsync(admin, fresh, "Reject")).StatusCode == HttpStatusCode.OK,
            "Yönetici reddinde de gerekçe zorunlu olmalı.");
        Require((await PostRequestActionAsync(admin, fresh, "Reject", "Belge eksik.")).StatusCode == HttpStatusCode.Redirect &&
            await db.RequestActions.AnyAsync(x => x.WorkflowRequestId == fresh && x.ActionType == RequestActionType.AdministratorRejected &&
                x.ActorId == administrator.Id && x.Comment == "Belge eksik."), "Yönetici reddi gerekçesiyle kaydedilmeli.");

        Require((await outsiderClient.GetAsync($"/Users/RecoveryCode/{outsider.Id}")).Headers.Location?.ToString().Contains("/Account/AccessDenied") == true,
            "Kurtarma kodu ekranı yöneticiye özel olmalı.");
        var issue = await PostFormAsync(admin, $"/Users/RecoveryCode/{outsider.Id}", $"/Users/IssueRecoveryCode/{outsider.Id}", []);
        var codePage = await issue.Content.ReadAsStringAsync();
        var match = Regex.Match(codePage, "id=\"recovery-code\"[^>]*value=\"([^\"]+)\"");
        Require(match.Success && issue.Headers.CacheControl?.NoStore == true, "Kurtarma kodu yalnızca önbelleğe alınmayan yönetici yanıtında gösterilmeli.");
        var code = WebUtility.HtmlDecode(match.Groups[1].Value);
        using var anonymous = NewClient(factory);
        const string replacement = "New-Integration-Password!2026";
        var reset = new Dictionary<string, string> { ["Email"] = outsider.Email!, ["Code"] = code,
            ["Password"] = replacement, ["ConfirmPassword"] = replacement };
        Require((await PostFormAsync(anonymous, "/Account/ResetPassword", "/Account/ResetPassword", reset)).StatusCode == HttpStatusCode.Redirect,
            "Geçerli kodla şifre değiştirilebilmeli.");
        Require((await outsiderClient.GetAsync("/")).StatusCode == HttpStatusCode.Redirect &&
            (await LoginAsync(anonymous, outsider.Email!, replacement)).StatusCode == HttpStatusCode.Redirect,
            "Şifre değişikliği eski oturumu kapatmalı ve yeni şifreyle giriş sağlanmalı.");
        using var retry = NewClient(factory);
        Require(WebUtility.HtmlDecode(await (await PostFormAsync(retry, "/Account/ResetPassword", "/Account/ResetPassword", reset)).Content.ReadAsStringAsync())
            .Contains("Kurtarma kodu geçersiz"), "Kullanılmış kurtarma kodu tekrar kabul edilmemeli.");

        var history = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administration/History"));
        Require(history.Contains("Roller değiştirildi") && history.Contains("Süreç yayımlandı") &&
            history.Contains("Yönetici adımı onayladı") && history.Contains("Yönetici talebi reddetti") &&
            history.Contains("Kurtarma kodu oluşturuldu") && !history.Contains(code),
            "Yönetim geçmişi önemli işlemleri kaydetmeli ve kurtarma kodunu göstermemeli.");
        Require((await requester.GetAsync("/Administration/History")).Headers.Location?.ToString().Contains("/Account/AccessDenied") == true,
            "Yönetim geçmişi yöneticiye özel olmalı.");
        Console.WriteLine("BAŞARILI: Adım gecikmesi, yönetici onayı ve gerekçeli ret, tek kullanımlık şifre kurtarma ve yönetim geçmişi.");
    }
}
