using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Services.Workflow;

namespace MiniWorkflowEngine.ViewModels;

public sealed class RequestEditorViewModel : IValidatableObject
{
    public int Id { get; set; }
    public string? RowVersion { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Bir süreç seçin.")]
    [Display(Name = "Süreç")]
    public int WorkflowDefinitionId { get; set; }
    [Required(ErrorMessage = "Talep başlığı girin.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Başlık 2–200 karakter olmalıdır.")]
    [Display(Name = "Başlık")]
    public string Title { get; set; } = string.Empty;
    [Required(ErrorMessage = "Talep açıklaması girin.")]
    [StringLength(4000, ErrorMessage = "Açıklama en fazla 4000 karakter olabilir.")]
    [Display(Name = "Açıklama")]
    public string Description { get; set; } = string.Empty;
    [Display(Name = "Tutar")]
    [StringLength(30, ErrorMessage = "Geçerli bir tutar girin.")]
    public string? Amount { get; set; }
    [StringLength(100, ErrorMessage = "Departman en fazla 100 karakter olabilir.")]
    [Display(Name = "Departman")]
    public string? Department { get; set; }
    public Dictionary<string, string?> FormValues { get; set; } = new(StringComparer.Ordinal);
    [BindNever, ValidateNever] public List<RequestWorkflowChoice> Workflows { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!AmountInput.TryParse(Amount, out _)) yield return new ValidationResult(AmountInput.ErrorMessage, [nameof(Amount)]);
    }

    public RequestDraftInput ToInput()
    {
        if (!AmountInput.TryParse(Amount, out var value)) throw new InvalidOperationException("Talep tutarı doğrulanmadan kullanılamaz.");
        return new(WorkflowDefinitionId, Title, Description, value, Department, FormValues);
    }

}

public sealed record RequestWorkflowChoice(int Id, string Name, int Version, bool IsActive, bool RequiresAmount, bool RequiresDepartment,
    GraphField[] Fields);

public sealed class RequestActionViewModel
{
    public int Id { get; set; }
    public RequestListScope? Scope { get; set; }
    [Required(ErrorMessage = "Talep sürümü eksik. Sayfayı yenileyin.")]
    public string RowVersion { get; set; } = string.Empty;
    [StringLength(2000, ErrorMessage = "İşlem notu en fazla 2000 karakter olabilir.")]
    public string? Comment { get; set; }
}

public sealed class RequestDetailsViewModel
{
    public RequestSnapshot Request { get; set; } = null!;
    public RequestListScope Scope { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public bool CanEdit { get; set; }
    public bool CanSubmit { get; set; }
    public bool CanCancel { get; set; }
    public bool CanDecide { get; set; }
    public bool IsAdministrativeDecision { get; set; }
    public bool IsWorkflowActive { get; set; }
    public DateTimeOffset? WaitingSince { get; set; }
    [Display(Name = "İşlem notu / ret gerekçesi")]
    public string? Comment { get; set; }
}

public enum RequestListScope { Mine, Inbox, All }

public sealed class RequestListViewModel
{
    public RequestListScope Scope { get; set; }
    public string? Search { get; set; }
    public RequestStatus? Status { get; set; }
    public bool Overdue { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public List<RequestListItem> Items { get; set; } = [];
}

public sealed record RequestListItem(int Id, string Title, string WorkflowName, int WorkflowVersion,
    string RequesterName, RequestStatus Status, string? CurrentStepName, DateTimeOffset CreatedAt, decimal? Amount,
    DateTimeOffset? WaitingSince);
