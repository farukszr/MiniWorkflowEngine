using System.ComponentModel.DataAnnotations;

namespace MiniWorkflowEngine.Domain.Entities;

public sealed class AdministrationEvent
{
    public long Id { get; set; }
    public string ActorId { get; set; } = "";
    public ApplicationUser Actor { get; set; } = null!;
    [MaxLength(80)] public string Operation { get; set; } = "";
    [MaxLength(400)] public string Target { get; set; } = "";
    [MaxLength(600)] public string? Detail { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
