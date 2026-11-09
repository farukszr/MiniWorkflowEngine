using Microsoft.AspNetCore.Identity;

namespace MiniWorkflowEngine.Services;

public sealed class TurkishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError ConcurrencyFailure() => Error("Kullanıcı başka bir işlemde güncellendi. Sayfayı yenileyin.");
    public override IdentityError PasswordTooShort(int length) => Error($"Parola en az {length} karakter olmalıdır.");
    public override IdentityError PasswordRequiresDigit() => Error("Parola en az bir rakam içermelidir.");
    public override IdentityError PasswordRequiresLower() => Error("Parola en az bir küçük harf içermelidir.");
    public override IdentityError PasswordRequiresUpper() => Error("Parola en az bir büyük harf içermelidir.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error("Parola en az bir özel karakter içermelidir.");
    public override IdentityError DuplicateEmail(string email) => Error("Bu e-posta adresi kullanımda.");
    public override IdentityError DuplicateUserName(string userName) => Error("Bu kullanıcı adı kullanımda.");
    public override IdentityError InvalidEmail(string? email) => Error("Geçerli bir e-posta adresi girin.");
    public override IdentityError InvalidUserName(string? userName) => Error("Geçerli bir kullanıcı adı girin.");
    public override IdentityError InvalidToken() => Error("Kurtarma kodu geçersiz veya süresi dolmuş.");
    private static IdentityError Error(string description) => new() { Description = description };
}
