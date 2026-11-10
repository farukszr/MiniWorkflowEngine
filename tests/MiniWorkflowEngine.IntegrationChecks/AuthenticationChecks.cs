using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Services;

internal static partial class AuthenticationChecks
{
    private const string Password = "Integration-Password-2026!";

    public static async Task RunAsync()
    {
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database=MiniWorkflowEngine_AuthChecks_{Guid.NewGuid():N};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";
        await using var factory = new WebApplicationFactory<ApplicationDbContext>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(Path.GetFullPath("../../../../../", AppContext.BaseDirectory));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
            });
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await db.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedRolesAsync();
            var bootstrapper = scope.ServiceProvider.GetRequiredService<AdministratorBootstrapper>();
            Require(!(await bootstrapper.CreateAsync("admin@example.invalid", "Test Yönetici", "weak")).Succeeded,
                "Zayıf yönetici parolası reddedilmeli.");
            db.ChangeTracker.Clear();
            Require(!await db.Users.AnyAsync(), "Başarısız kurulum kullanıcı bırakmamalı.");
            Require((await bootstrapper.CreateAsync("admin@example.invalid", "Test Yönetici", Password)).Succeeded,
                "İlk yönetici kurulumu başarısız.");
            Require(!(await bootstrapper.CreateAsync("second@example.invalid", "İkinci Yönetici", Password)).Succeeded,
                "İlk yönetici kurulumu tekrar çalışmamalı.");
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var employee = new ApplicationUser { Email = "employee@example.invalid", UserName = "employee@example.invalid", FullName = "Test Çalışan" };
            Require((await users.CreateAsync(employee, Password)).Succeeded, "Çalışan oluşturulamadı.");
            Require((await users.AddToRoleAsync(employee, ApplicationRoles.Employee)).Succeeded, "Çalışan rolü atanamadı.");
            Console.WriteLine("BAŞARILI: Güçlü parola, ilk yönetici kurulumu ve tekrar kurulum engeli.");

            using var adminClient = NewClient(factory);
            var anonymous = await adminClient.GetAsync("/Administration");
            Require(anonymous.StatusCode == HttpStatusCode.Redirect && anonymous.Headers.Location!.ToString().Contains("/Account/Login"),
                "Anonim ziyaretçi girişe yönlenmeli.");
            Require((await adminClient.GetAsync("/css/site.css")).IsSuccessStatusCode, "Anonim CSS erişimi engellendi.");
            var missingToken = await adminClient.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["Email"] = "admin@example.invalid", ["Password"] = Password }));
            Require(missingToken.StatusCode == HttpStatusCode.BadRequest, "Tokensiz giriş reddedilmeli.");
            var invalid = await LoginAsync(adminClient, "admin@example.invalid", "Wrong-Password-2026!");
            Require(invalid.StatusCode == HttpStatusCode.OK && !(invalid.Headers.TryGetValues("Set-Cookie", out var badCookies) && badCookies.Any(x => x.StartsWith("__Host-MiniWorkflowEngine.Auth="))),
                "Hatalı parola oturum açmamalı.");
            var login = await LoginAsync(adminClient, "admin@example.invalid", Password, "https://example.invalid/unsafe");
            Require(login.StatusCode == HttpStatusCode.Redirect && login.Headers.Location!.ToString() == "/", "Harici dönüş adresi reddedilmeli.");
            var authCookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-MiniWorkflowEngine.Auth="));
            Require(authCookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && authCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase), "Oturum çerezi güvenli olmalı.");
            Require((await adminClient.GetAsync("/Administration")).StatusCode == HttpStatusCode.OK, "Yönetici erişimi başarısız.");
            Console.WriteLine("BAŞARILI: Anonim yönlendirme, CSRF, yanlış parola, güvenli çerez ve harici dönüş adresi kontrolü.");

            using var employeeClient = NewClient(factory);
            var employeeLogin = await LoginAsync(employeeClient, employee.Email!, Password, "/Home/Index");
            Require(employeeLogin.Headers.Location?.ToString() == "/Home/Index", "Yerel dönüş adresi korunmalı.");
            var denied = await employeeClient.GetAsync("/Administration");
            Require(denied.StatusCode == HttpStatusCode.Redirect && denied.Headers.Location!.ToString().Contains("/Account/AccessDenied"), "Çalışan yönetim sayfasına erişememeli.");
            Require((await employeeClient.GetAsync(denied.Headers.Location)).StatusCode == HttpStatusCode.Forbidden, "Erişim engeli sayfası 403 dönmeli.");
            Require((await employeeClient.GetAsync("/")).StatusCode == HttpStatusCode.OK, "Çalışan ana sayfaya erişebilmeli.");
            var adminStart = WebUtility.HtmlDecode(await adminClient.GetStringAsync("/"));
            var employeeStart = WebUtility.HtmlDecode(await employeeClient.GetStringAsync("/"));
            Require(adminStart.Contains("Yeni akış tasarla") && adminStart.Contains("/Workflows/Designer?ornek=masraf") &&
                employeeStart.Contains("Kurum yöneticiniz bir süreç yayımladığında") && employeeStart.Contains("/Requests/Create") &&
                !employeeStart.Contains("/Workflows/Designer") && !employeeStart.Contains("/Administration"),
                "İlk girişte yönetici kurulum adımını, çalışan süreç bekleme durumunu görmeli.");
            var management = await adminClient.GetStringAsync("/Administration");
            Require(management.Contains("href=\"/Users\"") && management.Contains("href=\"/Departments\"") &&
                management.Contains("href=\"/Administration/Reports\"") && management.Contains("href=\"/Administration/History\""),
                "Yönetim ekranı hesap, departman, rapor ve geçmiş bağlantılarını sunmalı.");
            Console.WriteLine("BAŞARILI: Yönetici/çalışan erişim ayrımı ve yerel dönüş adresi.");

            await CheckUserAdministrationAsync(factory, adminClient, employeeClient);
            await CheckWorkflowDefinitionsAsync(factory, adminClient, employeeClient);
            await CheckRequestScreensAsync(factory, adminClient, employeeClient);
            await CheckApprovalOperationsAsync(factory, adminClient, employeeClient);
            await CheckGraphPlatformAsync(factory, adminClient, employeeClient);
            await CheckDesignerBrowserAsync(adminClient);

            var getLogout = await adminClient.GetAsync("/Account/Logout");
            Require(getLogout.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound &&
                (await adminClient.GetAsync("/Administration")).StatusCode == HttpStatusCode.OK, "GET ile çıkış yapılmamalı.");
            Require((await adminClient.PostAsync("/Account/Logout", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "Tokensiz çıkış reddedilmeli.");
            var home = await adminClient.GetStringAsync("/");
            var logout = await adminClient.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(home) }));
            Require(logout.StatusCode == HttpStatusCode.Redirect && (await adminClient.GetAsync("/Administration")).StatusCode == HttpStatusCode.Redirect,
                "Çıkış sonrası oturum kapanmalı.");
            Console.WriteLine("BAŞARILI: Yalnızca CSRF korumalı POST ile çıkış ve oturumun kapanması.");

            using var lockedClient = NewClient(factory);
            for (var attempt = 0; attempt < 5; attempt++) await LoginAsync(lockedClient, employee.Email!, "Wrong-Password-2026!");
            db.ChangeTracker.Clear();
            var locked = await users.FindByEmailAsync(employee.Email!);
            Require(await users.IsLockedOutAsync(locked!), "Beş yanlış deneme hesabı kilitlemeli.");
            var blocked = await LoginAsync(lockedClient, employee.Email!, Password);
            Require(blocked.StatusCode == HttpStatusCode.OK && (await lockedClient.GetAsync("/")).StatusCode == HttpStatusCode.Redirect,
                "Kilitli hesap doğru parolayla da giriş yapamamalı.");
            Console.WriteLine("BAŞARILI: Beş başarısız denemede kilit ve kilitliyken giriş engeli.");
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private static HttpClient NewClient(WebApplicationFactory<ApplicationDbContext> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, string returnUrl = "/")
    {
        var page = await client.GetStringAsync("/Account/Login");
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = password, ["ReturnUrl"] = returnUrl,
            ["__RequestVerificationToken"] = Token(page)
        }));
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success) throw new Exception("Formda CSRF belirteci bulunamadı.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
