using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.Services.Workflow;

internal static class ApprovalAccess
{
    public static IQueryable<WorkflowRequest> PendingFor(ApplicationDbContext db, string actorId)
        => db.WorkflowRequests.Where(x => x.Status == RequestStatus.Pending && x.RequesterId != actorId && x.CurrentStep != null &&
            db.UserRoles.Any(ur => ur.UserId == actorId && ur.RoleId == x.CurrentStep.ApproverRoleId));

    public static Task<bool> IsAdministratorAsync(ApplicationDbContext db, string actorId, CancellationToken ct)
        => (from membership in db.UserRoles join role in db.Roles on membership.RoleId equals role.Id
            where membership.UserId == actorId && role.Name == ApplicationRoles.Administrator select membership).AnyAsync(ct);
}
