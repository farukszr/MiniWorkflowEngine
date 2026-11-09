using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;

internal static partial class AuthenticationChecks
{
    private static async Task CheckUserAdministrationAsync(WebApplicationFactory<ApplicationDbContext> factory,
        HttpClient adminClient, HttpClient employeeClient)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = await db.Users.AsNoTracking().SingleAsync(x => x.Email == "admin@example.invalid");
        var employee = await db.Users.AsNoTracking().SingleAsync(x => x.Email == "employee@example.invalid");

        using var anonymous = NewClient(factory);
        Require((await anonymous.GetAsync("/Users")).StatusCode == HttpStatusCode.Redirect, "Anonim kullanıcı listesine erişememeli.");
        foreach (var path in new[] { "/Users", "/Users/Create", $"/Users/Roles/{admin.Id}" })
        {
            var denied = await employeeClient.GetAsync(path);
            Require(denied.StatusCode == HttpStatusCode.Redirect && denied.Headers.Location!.ToString().Contains("AccessDenied"),
                "Çalışan yönetim ekranlarına erişememeli.");
        }
        var unauthorizedPost = await PostFormAsync(employeeClient, "/", "/Users/Create", CreateFields("unauthorized@example.invalid", [ApplicationRoles.Administrator]));
        Require(unauthorizedPost.StatusCode == HttpStatusCode.Redirect && unauthorizedPost.Headers.Location!.ToString().Contains("AccessDenied"),
            "Çalışan doğrudan POST ile yönetici oluşturamamalı.");
        var noToken = await adminClient.PostAsync("/Users/Create", new FormUrlEncodedContent(CreateFields("no-token@example.invalid", [ApplicationRoles.Employee])));
        Require(noToken.StatusCode == HttpStatusCode.BadRequest, "Kullanıcı oluşturma CSRF korumalı olmalı.");

        var invalidRole = await PostFormAsync(adminClient, "/Users/Create", "/Users/Create", CreateFields("invalid@example.invalid", ["UnlistedRole"]));
        Require(invalidRole.StatusCode == HttpStatusCode.OK && !await db.Users.AnyAsync(x => x.Email == "invalid@example.invalid"), "Geçersiz rol kısmi kullanıcı kaydı bırakmamalı.");
        var weakFields = CreateFields("weak@example.invalid", [ApplicationRoles.Employee]);
        weakFields["Password"] = weakFields["ConfirmPassword"] = "weak-password";
        Require((await PostFormAsync(adminClient, "/Users/Create", "/Users/Create", weakFields)).StatusCode == HttpStatusCode.OK &&
            !await db.Users.AnyAsync(x => x.Email == "weak@example.invalid"), "Zayıf parola reddedilmeli.");
        var noRoles = await PostFormAsync(adminClient, "/Users/Create", "/Users/Create", CreateFields("no-roles@example.invalid", []));
        Require(noRoles.StatusCode == HttpStatusCode.OK && !await db.Users.AnyAsync(x => x.Email == "no-roles@example.invalid"), "Rol seçimi zorunlu olmalı.");
        var emptyPassword = CreateFields("empty-password@example.invalid", [ApplicationRoles.Employee]);
        emptyPassword["Password"] = emptyPassword["ConfirmPassword"] = "";
        var emptyResponse = await PostFormAsync(adminClient, "/Users/Create", "/Users/Create", emptyPassword);
        Require(WebUtility.HtmlDecode(await emptyResponse.Content.ReadAsStringAsync()).Contains("Parola girin."), "Parola doğrulama hatası kullanıcıya gösterilmeli.");
        Console.WriteLine("BAŞARILI: Kullanıcı yönetiminde yetki, CSRF, geçersiz rol ve zayıf parola kontrolleri.");

        var created = await PostFormAsync(adminClient, "/Users/Create", "/Users/Create", CreateFields("created@example.invalid", [ApplicationRoles.Employee, ApplicationRoles.Approver]));
        Require(created.StatusCode == HttpStatusCode.Redirect, "Kullanıcı oluşturma başarısız.");
        var createdUser = await db.Users.AsNoTracking().SingleAsync(x => x.Email == "created@example.invalid");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Require(await users.CheckPasswordAsync(createdUser, Password) && (await users.GetRolesAsync(createdUser)).Count == 2,
            "Yeni kullanıcı parolası ve çoklu rolleri kaydedilmeli.");
        var duplicate = await PostFormAsync(adminClient, "/Users/Create", "/Users/Create", CreateFields("CREATED@example.invalid", [ApplicationRoles.Employee]));
        Require(duplicate.StatusCode == HttpStatusCode.OK && await db.Users.CountAsync(x => x.NormalizedEmail == "CREATED@EXAMPLE.INVALID") == 1,
            "Mükerrer e-posta reddedilmeli.");
        var listHtml = await adminClient.GetStringAsync("/Users?search=created%40example.invalid&page=999");
        Require(listHtml.Contains("created@example.invalid") && !listHtml.Contains("employee@example.invalid"), "Arama ve sayfa sınırı çalışmalı.");
        Require((await adminClient.GetAsync("/Users/Roles/nonexistent-id")).StatusCode == HttpStatusCode.NotFound, "Bulunamayan kullanıcı 404 dönmeli.");
        Console.WriteLine("BAŞARILI: Çoklu rolle kullanıcı oluşturma, mükerrer e-posta engeli, arama ve sayfalama.");

        var adminRolesPage = await adminClient.GetStringAsync($"/Users/Roles/{admin.Id}");
        var lastAdmin = await PostRolesAsync(adminClient, admin.Id, adminRolesPage, [ApplicationRoles.Employee]);
        Require(lastAdmin.StatusCode == HttpStatusCode.OK && await users.IsInRoleAsync(admin, ApplicationRoles.Administrator),
            "Son yönetici kendisini yetkisiz bırakamamalı.");
        var emptyRoles = await PostRolesAsync(adminClient, admin.Id, adminRolesPage, []);
        Require(emptyRoles.StatusCode == HttpStatusCode.OK && await users.IsInRoleAsync(admin, ApplicationRoles.Administrator), "Boş rol seçimi mevcut rolleri silmemeli.");
        var mismatch = await PostFormAsync(adminClient, $"/Users/Roles/{employee.Id}", $"/Users/Roles/{employee.Id}",
            new Dictionary<string, string> { ["Id"] = admin.Id, ["ConcurrencyStamp"] = admin.ConcurrencyStamp!, ["Roles"] = ApplicationRoles.Employee });
        Require(mismatch.StatusCode == HttpStatusCode.BadRequest, "URL ve form kullanıcı kimliği eşleşmeli.");
        var oldPage = await adminClient.GetStringAsync($"/Users/Roles/{createdUser.Id}");
        var update = await PostRolesAsync(adminClient, createdUser.Id, oldPage, [ApplicationRoles.Approver]);
        Require(update.StatusCode == HttpStatusCode.Redirect, "Rol güncelleme başarısız.");
        var stale = await PostRolesAsync(adminClient, createdUser.Id, oldPage, [ApplicationRoles.Administrator]);
        Require(stale.StatusCode == HttpStatusCode.OK && !await users.IsInRoleAsync(createdUser, ApplicationRoles.Administrator),
            "Eski form yeni rolleri ezmemeli.");
        Console.WriteLine("BAŞARILI: Son yönetici koruması, kimlik eşleşmesi ve eski form güncellemesinin reddi.");

        // İkinci yöneticinin oturum açtıktan sonra rolünün alınması oturumunu kapatmalı.
        var promotePage = await adminClient.GetStringAsync($"/Users/Roles/{createdUser.Id}");
        Require((await PostRolesAsync(adminClient, createdUser.Id, promotePage, [ApplicationRoles.Administrator])).StatusCode == HttpStatusCode.Redirect,
            "İkinci yönetici atanamadı.");
        using var secondAdminClient = NewClient(factory);
        await LoginAsync(secondAdminClient, createdUser.Email!, Password);
        Require((await secondAdminClient.GetAsync("/Users")).IsSuccessStatusCode, "Yeni yönetici kullanıcı listesine erişebilmeli.");
        var revokePage = await adminClient.GetStringAsync($"/Users/Roles/{createdUser.Id}");
        Require((await PostRolesAsync(adminClient, createdUser.Id, revokePage, [ApplicationRoles.Employee])).StatusCode == HttpStatusCode.Redirect,
            "İkinci yöneticinin rolü alınamadı.");
        var revoked = await secondAdminClient.GetAsync("/Users");
        Require(revoked.StatusCode == HttpStatusCode.Redirect && revoked.Headers.Location!.ToString().Contains("/Account/Login"),
            "Rolü alınan yöneticinin eski oturumu geçersiz olmalı.");
        Console.WriteLine("BAŞARILI: Rol değişikliği sonrası eski yetkili oturumun iptali.");

        var promoteAgainPage = await adminClient.GetStringAsync($"/Users/Roles/{createdUser.Id}");
        await PostRolesAsync(adminClient, createdUser.Id, promoteAgainPage, [ApplicationRoles.Administrator]);
        await LoginAsync(secondAdminClient, createdUser.Email!, Password);
        var selfPage = await secondAdminClient.GetStringAsync($"/Users/Roles/{createdUser.Id}");
        var selfChange = await PostRolesAsync(secondAdminClient, createdUser.Id, selfPage, [ApplicationRoles.Employee]);
        Require(selfChange.StatusCode == HttpStatusCode.Redirect && selfChange.Headers.Location!.ToString() == "/",
            "Başka yönetici varken kendi rolünü değiştiren kullanıcı ana sayfaya yönlenmeli.");
        Require((await secondAdminClient.GetAsync("/")).IsSuccessStatusCode, "Kendi rolünü değiştiren kullanıcının geçerli oturumu yenilenmeli.");
        var selfDenied = await secondAdminClient.GetAsync("/Users");
        Require(selfDenied.StatusCode == HttpStatusCode.Redirect && selfDenied.Headers.Location!.ToString().Contains("AccessDenied"),
            "Kendi rolünü değiştiren kullanıcı eski yönetici yetkisini korumamalı.");
        Console.WriteLine("BAŞARILI: Kendi rolünü değiştiren yöneticinin oturum ve yetkilerinin yenilenmesi.");
    }

    private static Dictionary<string, string> CreateFields(string email, string[] roles)
    {
        var fields = new Dictionary<string, string> { ["FullName"] = "Yeni Kullanıcı", ["Email"] = email,
            ["Password"] = Password, ["ConfirmPassword"] = Password };
        for (var i = 0; i < roles.Length; i++) fields[$"Roles[{i}]"] = roles[i];
        return fields;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string pagePath, string postPath, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await client.GetStringAsync(pagePath));
        return await client.PostAsync(postPath, new FormUrlEncodedContent(fields));
    }

    private static Task<HttpResponseMessage> PostRolesAsync(HttpClient client, string id, string page, string[] roles)
    {
        var fields = new Dictionary<string, string> { ["Id"] = id, ["ConcurrencyStamp"] = HiddenValue(page, "ConcurrencyStamp"),
            ["__RequestVerificationToken"] = Token(page) };
        for (var i = 0; i < roles.Length; i++) fields[$"Roles[{i}]"] = roles[i];
        return client.PostAsync($"/Users/Roles/{id}", new FormUrlEncodedContent(fields));
    }

    private static string HiddenValue(string html, string name)
    {
        var match = Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"");
        if (!match.Success) throw new Exception($"Form alanı bulunamadı: {name}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
