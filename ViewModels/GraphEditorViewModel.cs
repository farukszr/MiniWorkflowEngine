using System.ComponentModel.DataAnnotations;

namespace MiniWorkflowEngine.ViewModels;

public sealed class GraphEditorViewModel
{
    public int Id { get; set; }
    [RegularExpression("^[A-Za-z][A-Za-z0-9_]{1,49}$", ErrorMessage = "Süreç kodu bir harfle başlamalı ve yalnızca İngilizce harf, rakam veya alt çizgi içermelidir.")]
    [Display(Name = "Süreç kodu")]
    public string? Code { get; set; }
    [Required(ErrorMessage = "Süreç adı girin."), StringLength(150, MinimumLength = 2, ErrorMessage = "Süreç adı 2–150 karakter olmalıdır.")]
    [Display(Name = "Süreç adı")]
    public string Name { get; set; } = "";
    [StringLength(2000, ErrorMessage = "Açıklama en fazla 2000 karakter olabilir."), Display(Name = "Açıklama")]
    public string? Description { get; set; }
    public string? RowVersion { get; set; }
    [Required(ErrorMessage = "Tasarım belgesi eksik."), StringLength(96_000, ErrorMessage = "Tasarım belgesi boyut sınırını aşıyor.")]
    public string GraphJson { get; set; } = "";
}
