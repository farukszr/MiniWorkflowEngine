using System.ComponentModel.DataAnnotations;

namespace MiniWorkflowEngine.ViewModels;

public sealed record RecoveryCodeViewModel(string UserId, string FullName, string Email, string? Code = null);

public sealed class ResetPasswordViewModel
{
    [Required(ErrorMessage = "E-posta adresinizi girin."), EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin."),
     StringLength(256), Display(Name = "E-posta")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "Kurtarma kodunu girin."), StringLength(2048), Display(Name = "Kurtarma kodu")]
    public string Code { get; set; } = "";
    [Required(ErrorMessage = "Yeni şifreyi girin."), StringLength(128, MinimumLength = 10, ErrorMessage = "Şifre 10–128 karakter olmalıdır."),
     DataType(DataType.Password), Display(Name = "Yeni şifre")]
    public string Password { get; set; } = "";
    [Required(ErrorMessage = "Yeni şifreyi tekrar girin."), Compare(nameof(Password), ErrorMessage = "Şifreler eşleşmiyor."),
     DataType(DataType.Password), Display(Name = "Yeni şifreyi doğrula")]
    public string ConfirmPassword { get; set; } = "";
}
