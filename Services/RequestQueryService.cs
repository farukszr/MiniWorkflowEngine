using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Services.Workflow;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Services;

public sealed class RequestQueryService(ApplicationDbContext db)
{
    private IQueryable<WorkflowRequest> PendingFor(string actorId)
        => ApprovalAccess.PendingFor(db, actorId).AsNoTracking();

    private IQueryable<WorkflowRequest> InboxFor(string actorId)
        => PendingFor(actorId).Union(db.WorkflowRequests.AsNoTracking()
            .Where(x => x.RequesterId == actorId && x.Status == RequestStatus.Returned));

    public Task<int> PendingCountAsync(string actorId, CancellationToken ct) => InboxFor(actorId).CountAsync(ct);

    public async Task<RequestListViewModel> ListAsync(string actorId, RequestListScope scope, string? search, RequestStatus? status, int page, CancellationToken ct, bool overdue = false)
    {
        search = search?.Trim();
        if (search?.Length > 100) search = search[..100];
        if (status is not null && !Enum.IsDefined(status.Value)) status = null;
        var requests = db.WorkflowRequests.AsNoTracking();
        var query = scope switch
        {
            RequestListScope.Inbox => InboxFor(actorId),
            RequestListScope.All => requests,
            _ => requests.Where(x => x.RequesterId == actorId)
        };
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Title.Contains(search) || x.WorkflowDefinition.Name.Contains(search));
        if (scope != RequestListScope.Inbox && status is not null) query = query.Where(x => x.Status == status);
        var rows = query.Select(x => new
        {
            Request = x,
            WaitingSince = x.Status == RequestStatus.Returned
                ? x.Actions.Where(a => a.ActionType == RequestActionType.Returned).OrderByDescending(a => a.Id)
                    .Select(a => (DateTimeOffset?)a.CreatedAt).FirstOrDefault() ?? x.CreatedAt
                : x.Status == RequestStatus.Pending
                ? x.Actions.Where(a => a.ActionType == RequestActionType.Submitted || a.ActionType == RequestActionType.Approved ||
                        a.ActionType == RequestActionType.AdministratorApproved || a.ActionType == RequestActionType.TaskCompleted ||
                        a.ActionType == RequestActionType.Resubmitted)
                    .OrderByDescending(a => a.Id).Select(a => (DateTimeOffset?)a.CreatedAt).FirstOrDefault() ?? x.CreatedAt
                : (DateTimeOffset?)null
        });
        var cutoff = DateTimeOffset.UtcNow.AddHours(-ApprovalWaiting.OverdueHours);
        if (overdue) rows = rows.Where(x => x.WaitingSince <= cutoff);
        var count = await rows.CountAsync(ct);
        var pages = Math.Max(1, (int)Math.Ceiling(count / 20d));
        page = Math.Clamp(page, 1, pages);
        var ordered = scope == RequestListScope.Inbox || overdue
            ? rows.OrderBy(x => x.WaitingSince).ThenBy(x => x.Request.Id)
            : rows.OrderByDescending(x => x.Request.CreatedAt).ThenByDescending(x => x.Request.Id);
        var items = await ordered.Skip((page - 1) * 20).Take(20)
            .Select(x => new RequestListItem(x.Request.Id, x.Request.Title, x.Request.WorkflowDefinition.Name, x.Request.WorkflowDefinition.Version,
                x.Request.Requester.FullName, x.Request.Status, x.Request.CurrentStep == null ? null : x.Request.CurrentStep.Name,
                x.Request.CreatedAt, x.Request.Amount, x.WaitingSince)).ToListAsync(ct);
        return new RequestListViewModel { Scope = scope, Search = search, Status = scope == RequestListScope.Inbox ? null : status,
            Overdue = overdue, Page = page, TotalPages = pages, TotalCount = count, Items = items };
    }

    public async Task<List<RequestWorkflowChoice>> WorkflowsAsync(int? includeId, CancellationToken ct)
        => (await db.WorkflowDefinitions.AsNoTracking().Where(x => (x.IsActive && x.PublishedAt != null) || x.Id == includeId)
            .OrderBy(x => x.Name).ThenByDescending(x => x.Version)
            .Select(x => new { x.Id, x.Name, x.Version, x.IsActive, x.GraphJson,
                RequiresAmount = x.Steps.Any(s => s.Conditions.Any(c => c.Field == ConditionField.Amount)),
                RequiresDepartment = x.Steps.Any(s => s.Conditions.Any(c => c.Field == ConditionField.Department)) }).ToListAsync(ct))
            .Select(x => new RequestWorkflowChoice(x.Id, x.Name, x.Version, x.IsActive, x.RequiresAmount, x.RequiresDepartment,
                WorkflowGraph.Read(x.GraphJson)?.Fields ?? [])).ToList();

    public async Task<RequestDetailsViewModel> DetailsAsync(RequestSnapshot request, string actorId, CancellationToken ct)
    {
        var isOwner = request.RequesterId == actorId;
        var active = await db.WorkflowDefinitions.AnyAsync(x => x.Id == request.WorkflowDefinitionId && x.IsActive && x.PublishedAt != null, ct);
        var eligible = !isOwner && request.Status == RequestStatus.Pending && request.CurrentStepId is not null;
        var assigned = eligible &&
            await PendingFor(actorId).AnyAsync(x => x.Id == request.Id, ct);
        var administrative = eligible && !assigned && await ApprovalAccess.IsAdministratorAsync(db, actorId, ct);
        var requesterName = await db.Users.Where(x => x.Id == request.RequesterId).Select(x => x.FullName).SingleAsync(ct);
        return new RequestDetailsViewModel { Request = request, RequesterName = requesterName,
            CanEdit = isOwner && request.Status is RequestStatus.Draft or RequestStatus.Returned,
            CanSubmit = isOwner && (request.Status == RequestStatus.Returned || request.Status == RequestStatus.Draft && active), IsWorkflowActive = active,
            CanCancel = isOwner && request.Status is RequestStatus.Draft or RequestStatus.Pending or RequestStatus.Returned,
            CanDecide = assigned || administrative, IsAdministrativeDecision = administrative,
            WaitingSince = request.Status == RequestStatus.Returned
                ? request.History.LastOrDefault(x => x.ActionType == RequestActionType.Returned)?.CreatedAt ?? request.CreatedAt
                : request.Status == RequestStatus.Pending
                ? request.History.LastOrDefault(x => x.ActionType is RequestActionType.Submitted or RequestActionType.Approved
                    or RequestActionType.AdministratorApproved or RequestActionType.TaskCompleted or RequestActionType.Resubmitted)?.CreatedAt ?? request.CreatedAt
                : null };
    }
}
