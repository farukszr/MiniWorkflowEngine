using Microsoft.AspNetCore.Identity;

namespace MiniWorkflowEngine.Domain.Entities;

public sealed class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
