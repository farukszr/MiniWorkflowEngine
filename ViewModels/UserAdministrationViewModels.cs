using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MiniWorkflowEngine.ViewModels;

public sealed class UserListViewModel
{
    public string? Search { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<UserListItem> Users { get; set; } = [];
}

public sealed class UserListItem
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
}

public sealed class CreateUserViewModel
{
    [Required(ErrorMessage = "Ad soyad girin.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Ad soyad 2–150 karakter olmalıdır.")]
    [Display(Name = "Ad soyad")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-posta adresi girin.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(256, ErrorMessage = "E-posta en fazla 256 karakter olabilir.")]
    [Display(Name = "E-posta adresi")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parola girin.")]
    [DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Parola 10–128 karakter olmalıdır.")]
    [Display(Name = "İlk giriş parolası")]
    public string Password { get; set; } = string.Empty;

    [Compare(nameof(Password), ErrorMessage = "Parolalar eşleşmiyor.")]
    [DataType(DataType.Password)]
    [Display(Name = "Parola tekrarı")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [MinLength(1, ErrorMessage = "En az bir rol seçin.")]
    public string[] Roles { get; set; } = [];
}

public sealed class EditUserRolesViewModel
{
    [Required] public string Id { get; set; } = string.Empty;
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
    [BindNever, ValidateNever] public string FullName { get; set; } = string.Empty;
    [BindNever, ValidateNever] public string Email { get; set; } = string.Empty;
    [MinLength(1, ErrorMessage = "En az bir rol seçin.")]
    public string[] Roles { get; set; } = [];
}
