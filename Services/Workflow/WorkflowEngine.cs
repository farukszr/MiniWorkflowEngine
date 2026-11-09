using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.Services.Interfaces;

namespace MiniWorkflowEngine.Services.Workflow;

// Her çağrı bağımsız DbContext/transaction kullanır; başarısız işlem sonraki çağrıya taşınmaz.
public sealed class WorkflowEngine(IServiceScopeFactory scopeFactory, WorkflowConditionEvaluator evaluator) : IWorkflowEngine
{
    public Task<WorkflowResult> CreateDraftAsync(RequestDraftInput input, string actorId, CancellationToken cancellationToken = default)
        => ExecuteAsync(actorId, async (db, actor, ct) =>
        {
            var error = ValidateInput(input);
            if (error is not null) return Fail(WorkflowError.Validation, error);
            var definition = await db.WorkflowDefinitions.SingleOrDefaultAsync(x => x.Id == input.WorkflowDefinitionId, ct);
            if (definition is null || !definition.IsActive || definition.PublishedAt is null)
                return Fail(WorkflowError.InvalidState, "Yeni talep için etkin ve yayımlanmış bir süreç seçin.");
            var request = new WorkflowRequest { WorkflowDefinitionId = definition.Id, RequesterId = actor.Id };
            ApplyInput(request, input);
            db.WorkflowRequests.Add(request);
            AddAction(request, actor.Id, RequestActionType.DraftCreated);
            return await SaveAsync(db, request, ct);
        }, cancellationToken);

    public Task<WorkflowResult> UpdateDraftAsync(RequestCommand command, RequestDraftInput input, string actorId, CancellationToken cancellationToken = default)
        => MutateAsync(command, actorId, async (db, request, actor, ct) =>
        {
            if (request.RequesterId != actor.Id) return OwnerOnly();
            if (request.Status != RequestStatus.Draft) return Fail(WorkflowError.InvalidState, "Yalnızca taslak talep düzenlenebilir.");
            if (request.WorkflowDefinitionId != input.WorkflowDefinitionId) return Fail(WorkflowError.Validation, "Talebin süreç sürümü değiştirilemez.");
            var error = ValidateInput(input);
            if (error is not null) return Fail(WorkflowError.Validation, error);
            ApplyInput(request, input);
            // Aynı alanlar tekrar gönderilse de sürüm ve işlem geçmişi birlikte ilerler.
            db.Entry(request).Property(x => x.Title).IsModified = true;
            AddAction(request, actor.Id, RequestActionType.DraftUpdated);
            return await SaveAsync(db, request, ct);
        }, cancellationToken);

    public Task<WorkflowResult> SubmitAsync(RequestCommand command, string actorId, CancellationToken cancellationToken = default)
        => MutateAsync(command, actorId, async (db, request, actor, ct) =>
        {
            if (request.RequesterId != actor.Id) return OwnerOnly();
            if (request.Status != RequestStatus.Draft) return Fail(WorkflowError.InvalidState, "Yalnızca taslak talep gönderilebilir.");
            if (!request.WorkflowDefinition.IsActive || request.WorkflowDefinition.PublishedAt is null)
                return Fail(WorkflowError.InvalidState, "Taslağın süreç sürümü artık etkin değil. Etkin sürümle yeni talep oluşturun.");
            var error = ValidateInput(new RequestDraftInput(request.WorkflowDefinitionId, request.Title, request.Description, request.Amount, request.Department));
            if (error is not null) return Fail(WorkflowError.Validation, error);
            var applicability = await EvaluateStepsAsync(db, request, ct);
            var requiredInputs = WorkflowConditionEvaluator.RequiredInputs(request.WorkflowDefinition.Steps);
            if (requiredInputs.Amount && request.Amount is null)
                return Fail(WorkflowError.Validation, "Bu süreçte tutar koşulu bulunduğundan tutar zorunludur.");
            if (requiredInputs.Department && string.IsNullOrWhiteSpace(request.Department))
                return Fail(WorkflowError.Validation, "Bu süreçte departman koşulu bulunduğundan departman zorunludur.");
            request.Status = RequestStatus.Pending;
            AddAction(request, actor.Id, RequestActionType.Submitted, comment: command.Comment?.Trim());
            MoveNext(request, actor.Id, 0, applicability);
            return await SaveAsync(db, request, ct);
        }, cancellationToken);

    public Task<WorkflowResult> ApproveAsync(RequestCommand command, string actorId, CancellationToken cancellationToken = default)
        => DecideAsync(command, actorId, approve: true, cancellationToken);

    public Task<WorkflowResult> RejectAsync(RequestCommand command, string actorId, CancellationToken cancellationToken = default)
        => DecideAsync(command, actorId, approve: false, cancellationToken);

    public Task<WorkflowResult> CancelAsync(RequestCommand command, string actorId, CancellationToken cancellationToken = default)
        => MutateAsync(command, actorId, async (db, request, actor, ct) =>
        {
            if (request.RequesterId != actor.Id) return OwnerOnly();
            if (request.Status is not (RequestStatus.Draft or RequestStatus.Pending)) return FinalState();
            AddAction(request, actor.Id, RequestActionType.Cancelled, request.CurrentStepId, command.Comment?.Trim());
            Finish(request, RequestStatus.Cancelled);
            return await SaveAsync(db, request, ct);
        }, cancellationToken);

    public Task<WorkflowResult> GetAsync(int requestId, string actorId, CancellationToken cancellationToken = default)
        => ExecuteAsync(actorId, async (db, actor, ct) =>
        {
            var request = await LoadAsync(db, requestId, ct);
            if (request is null) return NotFound();
            if (!await CanReadAsync(db, request, actor.Id, ct)) return NotFound();
            return WorkflowResult.Success(await SnapshotAsync(db, request, ct));
        }, cancellationToken);

    private Task<WorkflowResult> DecideAsync(RequestCommand command, string actorId, bool approve, CancellationToken cancellationToken)
        => MutateAsync(command, actorId, async (db, request, actor, ct) =>
        {
            if (request.Status != RequestStatus.Pending) return FinalState();
            var current = request.WorkflowDefinition.Steps.SingleOrDefault(x => x.Id == request.CurrentStepId);
            if (current is null) return Fail(WorkflowError.InvalidDefinition, "Talebin geçerli onay adımı bulunamadı.");
            if (request.RequesterId == actor.Id) return Fail(WorkflowError.Forbidden, "Kendi talebiniz için onay veya ret kararı veremezsiniz.");
            var assigned = await ApprovalAccess.PendingFor(db, actor.Id).AnyAsync(x => x.Id == request.Id, ct);
            if (!assigned && !await ApprovalAccess.IsAdministratorAsync(db, actor.Id, ct))
                return Fail(WorkflowError.Forbidden, "Bu adım için onay yetkiniz bulunmuyor.");
            if (!approve && string.IsNullOrWhiteSpace(command.Comment)) return Fail(WorkflowError.Validation, "Ret gerekçesi girin.");
            var applicability = await EvaluateStepsAsync(db, request, ct);
            var decision = (assigned, approve) switch
            {
                (true, true) => RequestActionType.Approved,
                (true, false) => RequestActionType.Rejected,
                (false, true) => RequestActionType.AdministratorApproved,
                _ => RequestActionType.AdministratorRejected
            };
            AddAction(request, actor.Id, decision, current.Id, command.Comment?.Trim());
            if (!assigned)
                db.RecordAdministrationEvent(actor.Id, RequestLabels.Action(decision), $"Talep #{request.Id} · {request.Title}", current.Name);
            if (approve) MoveNext(request, actor.Id, current.Order, applicability);
            else Finish(request, RequestStatus.Rejected);
            return await SaveAsync(db, request, ct);
        }, cancellationToken);

    private Task<WorkflowResult> MutateAsync(RequestCommand command, string actorId,
        Func<ApplicationDbContext, WorkflowRequest, ApplicationUser, CancellationToken, Task<WorkflowResult>> action, CancellationToken cancellationToken)
        => ExecuteAsync(actorId, async (db, actor, ct) =>
        {
            if (command.Comment?.Length > 2000) return Fail(WorkflowError.Validation, "Açıklama en fazla 2000 karakter olabilir.");
            var request = await LoadAsync(db, command.RequestId, ct);
            if (request is null || !await CanReadAsync(db, request, actor.Id, ct)) return NotFound();
            if (!Matches(request.RowVersion, command.RowVersion)) return Fail(WorkflowError.Conflict, "Talep başka bir işlemde güncellendi. Güncel talebi yükleyip tekrar deneyin.");
            return await action(db, request, actor, ct);
        }, cancellationToken);

    private async Task<Dictionary<int, bool>> EvaluateStepsAsync(ApplicationDbContext db, WorkflowRequest request, CancellationToken ct)
    {
        var steps = request.WorkflowDefinition.Steps.OrderBy(x => x.Order).ToArray();
        var roles = await db.Roles.Where(x => ApplicationRoles.All.Contains(x.Name!)).Select(x => x.Id).ToListAsync(ct);
        return evaluator.EvaluateSteps(steps, roles, request.Amount, request.Department);
    }

    private static void MoveNext(WorkflowRequest request, string actorId, int afterOrder, IReadOnlyDictionary<int, bool> applicability)
    {
        foreach (var step in request.WorkflowDefinition.Steps.Where(x => x.Order > afterOrder).OrderBy(x => x.Order))
        {
            if (applicability[step.Id]) { request.CurrentStepId = step.Id; return; }
            AddAction(request, actorId, RequestActionType.StepSkipped, step.Id, "Adımın koşulları sağlanmadığı için otomatik atlandı.");
        }
        Finish(request, RequestStatus.Approved);
        AddAction(request, actorId, RequestActionType.Completed, comment: "Uygulanabilir tüm onay adımları tamamlandı.");
    }

    private static void Finish(WorkflowRequest request, RequestStatus status)
    {
        request.Status = status;
        request.CurrentStepId = null;
        request.CurrentStep = null;
        request.CompletedAt = DateTimeOffset.UtcNow;
    }

    private static void AddAction(WorkflowRequest request, string actorId, RequestActionType action, int? stepId = null, string? comment = null)
        => request.Actions.Add(new RequestAction { WorkflowDefinitionId = request.WorkflowDefinitionId,
            ActorId = actorId, ActionType = action, WorkflowStepId = stepId, Comment = comment });

    private static void ApplyInput(WorkflowRequest request, RequestDraftInput input)
    {
        request.Title = input.Title.Trim();
        request.Description = input.Description.Trim();
        request.Amount = input.Amount;
        request.Department = string.IsNullOrWhiteSpace(input.Department) ? null : input.Department.Trim();
    }

    private static string? ValidateInput(RequestDraftInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length is < 2 or > 200) return "Talep başlığı 2–200 karakter olmalıdır.";
        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length > 4000) return "Talep açıklaması zorunludur ve en fazla 4000 karakter olabilir.";
        if (input.Department?.Trim().Length > 100) return "Departman en fazla 100 karakter olabilir.";
        if (input.Amount is { } amount && (amount < 0 || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount))
            return "Tutar negatif olamaz; en fazla 16 tam sayı ve iki ondalık basamak içerebilir.";
        return null;
    }

    private static Task<WorkflowRequest?> LoadAsync(ApplicationDbContext db, int id, CancellationToken ct)
        => db.WorkflowRequests.Include(x => x.WorkflowDefinition).ThenInclude(x => x.Steps).ThenInclude(x => x.Conditions)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);

    private static async Task<bool> CanReadAsync(ApplicationDbContext db, WorkflowRequest request, string actorId, CancellationToken ct)
    {
        if (request.RequesterId == actorId) return true;
        if (await ApprovalAccess.IsAdministratorAsync(db, actorId, ct)) return true;
        if (await db.RequestActions.AnyAsync(x => x.WorkflowRequestId == request.Id && x.ActorId == actorId, ct)) return true;
        return request.Status == RequestStatus.Pending &&
            await ApprovalAccess.PendingFor(db, actorId).AnyAsync(x => x.Id == request.Id, ct);
    }

    private static async Task<WorkflowResult> SaveAsync(ApplicationDbContext db, WorkflowRequest request, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        return WorkflowResult.Success(await SnapshotAsync(db, request, ct));
    }

    private static async Task<RequestSnapshot> SnapshotAsync(ApplicationDbContext db, WorkflowRequest request, CancellationToken ct)
    {
        var definition = await db.WorkflowDefinitions.AsNoTracking().SingleAsync(x => x.Id == request.WorkflowDefinitionId, ct);
        var currentName = request.CurrentStepId is null ? null : await db.WorkflowSteps.Where(x => x.Id == request.CurrentStepId).Select(x => x.Name).SingleAsync(ct);
        var history = await db.RequestActions.AsNoTracking().Where(x => x.WorkflowRequestId == request.Id).OrderBy(x => x.Id)
            .Select(x => new RequestHistoryItem(x.Id, x.WorkflowStepId, x.WorkflowStep == null ? null : x.WorkflowStep.Name,
                x.ActorId, x.Actor.FullName, x.ActionType, x.Comment, x.CreatedAt)).ToListAsync(ct);
        return new RequestSnapshot(request.Id, definition.Id, definition.Name, definition.Version, request.RequesterId,
            request.Title, request.Description, request.Amount, request.Department, request.Status, request.CurrentStepId,
            currentName, request.CreatedAt, request.CompletedAt, Convert.ToBase64String(request.RowVersion), history.AsReadOnly());
    }

    private async Task<WorkflowResult> ExecuteAsync(string actorId,
        Func<ApplicationDbContext, ApplicationUser, CancellationToken, Task<WorkflowResult>> operation, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var actor = string.IsNullOrWhiteSpace(actorId) ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actorId, ct);
            if (actor is null || actor.LockoutEnd > DateTimeOffset.UtcNow) return Fail(WorkflowError.Forbidden, "Etkin bir kullanıcı hesabıyla giriş yapın.");
            var result = await operation(db, actor, ct);
            if (result.Succeeded) await transaction.CommitAsync(ct);
            return result;
        }
        catch (InvalidWorkflowDefinitionException ex) { return Fail(WorkflowError.InvalidDefinition, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Fail(WorkflowError.Conflict, "Talep eşzamanlı güncellendi. Yenileyip tekrar deneyin."); }
        catch (Exception ex) when (IsLockConflict(ex))
        { return Fail(WorkflowError.Conflict, "Başka bir işlem talebi güncelliyor. Yenileyip tekrar deneyin."); }
    }

    private static bool IsLockConflict(Exception exception)
    {
        // SQL Server sağlayıcısı kilitlenmeyi ek bir InvalidOperationException ile sarabilir.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 or 1222 }) return true;
        return false;
    }

    private static bool Matches(byte[] current, string stamp)
    {
        if (string.IsNullOrWhiteSpace(stamp)) return false;
        try { return current.SequenceEqual(Convert.FromBase64String(stamp)); }
        catch (FormatException) { return false; }
    }

    private static WorkflowResult Fail(WorkflowError error, string message) => WorkflowResult.Fail(error, message);
    private static WorkflowResult OwnerOnly() => Fail(WorkflowError.Forbidden, "Bu işlemi yalnızca talep sahibi yapabilir.");
    private static WorkflowResult FinalState() => Fail(WorkflowError.InvalidState, "Talep bu işleme uygun durumda değil veya zaten tamamlandı.");
    private static WorkflowResult NotFound() => Fail(WorkflowError.NotFound, "Talep bulunamadı veya erişim yetkiniz yok.");
}
