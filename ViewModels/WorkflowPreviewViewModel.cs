using System.ComponentModel.DataAnnotations;

namespace MiniWorkflowEngine.ViewModels;

public sealed class WorkflowPreviewInput
{
    [StringLength(30, ErrorMessage = "Geçerli bir tutar girin.")]
    [Display(Name = "Örnek tutar")]
    public string? Amount { get; set; }

    [StringLength(100, ErrorMessage = "Departman en fazla 100 karakter olabilir.")]
    [Display(Name = "Örnek departman")]
    public string? Department { get; set; }
}

public sealed record WorkflowPreviewViewModel(int Id, string Name, int Version, bool RequiresAmount,
    bool RequiresDepartment, IReadOnlyList<WorkflowPreviewStep>? Steps = null, string? Error = null);

public sealed record WorkflowPreviewStep(int Order, string Name, string Role, bool Applies);
