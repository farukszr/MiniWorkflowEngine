using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.ViewModels;

public sealed class WorkflowEditorViewModel
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Süreç kodu girin.")]
    [RegularExpression(@"^[A-Za-z][A-Za-z0-9_]{1,49}$", ErrorMessage = "Kod 2–50 karakter olmalı; harfle başlamalı ve yalnızca harf, rakam, alt çizgi içermelidir.")]
    [Display(Name = "Süreç kodu")]
    public string Code { get; set; } = string.Empty;
    [Required(ErrorMessage = "Süreç adı girin.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Süreç adı 2–150 karakter olmalıdır.")]
    [Display(Name = "Süreç adı")]
    public string Name { get; set; } = string.Empty;
    [StringLength(2000, ErrorMessage = "Açıklama en fazla 2000 karakter olabilir.")]
    [Display(Name = "Açıklama")]
    public string? Description { get; set; }
    public string? RowVersion { get; set; }
    [MinLength(1, ErrorMessage = "En az bir onay adımı ekleyin.")]
    [MaxLength(20, ErrorMessage = "En fazla 20 adım eklenebilir.")]
    public List<WorkflowStepViewModel> Steps { get; set; } = [];
    [BindNever, ValidateNever] public int Version { get; set; } = 1;
    [BindNever, ValidateNever] public bool CanEdit { get; set; }
    [BindNever, ValidateNever] public bool IsActive { get; set; }
    [BindNever, ValidateNever] public bool IsPublished { get; set; }
    [BindNever, ValidateNever] public bool IsGraph { get; set; }
    [BindNever, ValidateNever] public List<SelectListItem> AvailableRoles { get; set; } = [];
}

public sealed class WorkflowStepViewModel
{
    [Required(ErrorMessage = "Adım adı girin.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Adım adı 2–150 karakter olmalıdır.")]
    public string Name { get; set; } = string.Empty;
    [Required(ErrorMessage = "Onaylayıcı rolü seçin.")]
    public string ApproverRoleId { get; set; } = string.Empty;
    [MaxLength(10, ErrorMessage = "Bir adımda en fazla 10 koşul olabilir.")]
    public List<WorkflowConditionViewModel> Conditions { get; set; } = [];
}

public sealed class WorkflowConditionViewModel
{
    [EnumDataType(typeof(ConditionField), ErrorMessage = "Geçerli bir koşul alanı seçin.")]
    public ConditionField Field { get; set; }
    [EnumDataType(typeof(ConditionOperator), ErrorMessage = "Geçerli bir karşılaştırma seçin.")]
    public ConditionOperator Operator { get; set; }
    [Required(ErrorMessage = "Koşul değerini girin.")]
    [StringLength(250, ErrorMessage = "Koşul değeri en fazla 250 karakter olabilir.")]
    public string Value { get; set; } = string.Empty;
}

public sealed class WorkflowListViewModel
{
    public string? Search { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public List<WorkflowListItem> Items { get; set; } = [];
}

public sealed class WorkflowListItem
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public bool IsPublished { get; set; }
    public int StepCount { get; set; }
}

public sealed class WorkflowCommandViewModel
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Sürüm bilgisi eksik; sayfayı yenileyin.")]
    public string RowVersion { get; set; } = string.Empty;
}
