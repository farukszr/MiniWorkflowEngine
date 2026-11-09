using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MiniWorkflowEngine.Domain;

namespace MiniWorkflowEngine.ViewModels;

public sealed class GraphPreviewViewModel
{
    [StringLength(30, ErrorMessage = "Geçerli bir tutar girin."), Display(Name = "Örnek tutar")] public string? Amount { get; set; }
    [StringLength(100, ErrorMessage = "Departman en fazla 100 karakter olabilir."), Display(Name = "Örnek departman")] public string? Department { get; set; }
    public Dictionary<string, string?> FormValues { get; set; } = new(StringComparer.Ordinal);
    [BindNever] public int Id { get; set; }
    [BindNever] public string Name { get; set; } = "";
    [BindNever] public GraphField[] Fields { get; set; } = [];
    [BindNever] public string? Result { get; set; }
    [BindNever] public List<string> Route { get; set; } = [];
}
