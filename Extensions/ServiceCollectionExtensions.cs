using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Services;
using MiniWorkflowEngine.Services.Interfaces;
using MiniWorkflowEngine.Services.Workflow;

namespace MiniWorkflowEngine.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("SQL Server bağlantısı tanımlı değil. ConnectionStrings:DefaultConnection ayarını yapılandırın.");

        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Tokens.PasswordResetTokenProvider = PasswordRecoveryService.ProviderName;
            options.Password.RequiredLength = 10;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<ApplicationDbContext>()
            .AddErrorDescriber<TurkishIdentityErrorDescriber>().AddDefaultTokenProviders()
            .AddTokenProvider<PasswordRecoveryTokenProvider>(PasswordRecoveryService.ProviderName);

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.Cookie.Name = "__Host-MiniWorkflowEngine.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });
        services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<AdministratorBootstrapper>();
        services.AddScoped<UserAdministrationService>();
        services.AddScoped<PasswordRecoveryService>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("PasswordRecovery", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(5) }));
        });
        services.AddScoped<WorkflowDefinitionService>();
        services.AddScoped<RequestQueryService>();
        services.AddSingleton<WorkflowConditionEvaluator>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        return services;
    }
}
