using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Services;

public sealed class PasswordRecoveryService(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public const string ProviderName = "PasswordRecovery";
    public const int ValidMinutes = 30;

    public async Task<RecoveryCodeViewModel?> GetAccountAsync(string id)
    {
        var user = await users.FindByIdAsync(id);
        return user?.Email is { } email ? new RecoveryCodeViewModel(user.Id, user.FullName, email) : null;
    }

    public async Task<RecoveryCodeViewModel?> IssueAsync(string id, string actorId, CancellationToken ct)
    {
        var actor = await users.FindByIdAsync(actorId);
        if (actor is null || !await users.IsInRoleAsync(actor, ApplicationRoles.Administrator)) return null;
        var user = await users.FindByIdAsync(id);
        if (user?.Email is not { } email) return null;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        db.RecordAdministrationEvent(actorId, "Kurtarma kodu oluşturuldu", email);
        await db.SaveChangesAsync(ct);
        return new RecoveryCodeViewModel(user.Id, user.FullName, email, code);
    }

    public async Task<IdentityResult> ResetAsync(ResetPasswordViewModel model)
    {
        var user = await users.FindByEmailAsync(model.Email.Trim());
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Code.Trim())); }
        catch (FormatException) { return InvalidCode(); }
        if (user is null) return InvalidCode();

        // Identity, kodu ve şifreyi doğruladıktan sonra şifreyi, kilidi ve güvenlik damgasını birlikte kaydeder.
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        return await users.ResetPasswordAsync(user, token, model.Password);
    }

    private static IdentityResult InvalidCode()
        => IdentityResult.Failed(new IdentityError { Description = "Kurtarma kodu geçersiz veya süresi dolmuş." });
}

public sealed class PasswordRecoveryTokenProvider(IDataProtectionProvider protection, ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(protection, Microsoft.Extensions.Options.Options.Create(new DataProtectionTokenProviderOptions
    {
        Name = PasswordRecoveryService.ProviderName,
        TokenLifespan = TimeSpan.FromMinutes(PasswordRecoveryService.ValidMinutes)
    }), logger);
