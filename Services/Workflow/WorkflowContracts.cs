using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.Domain;

namespace MiniWorkflowEngine.Services.Workflow;

public sealed record RequestDraftInput(int WorkflowDefinitionId, string Title, string Description, decimal? Amount, string? Department,
    IReadOnlyDictionary<string, string?>? FormValues = null);
public sealed record RequestCommand(int RequestId, string RowVersion, string? Comment = null);
public enum WorkflowError { None, Validation, NotFound, Forbidden, InvalidState, Conflict, InvalidDefinition }
public sealed record WorkflowResult(bool Succeeded, WorkflowError Error, string Message, RequestSnapshot? Request = null)
{
    public static WorkflowResult Fail(WorkflowError error, string message) => new(false, error, message);
    public static WorkflowResult Success(RequestSnapshot request) => new(true, WorkflowError.None, "İşlem tamamlandı.", request);
}

public sealed record RequestHistoryItem(long Id, int? StepId, string? StepName, string ActorId, string ActorName,
    RequestActionType ActionType, string? Comment, DateTimeOffset CreatedAt);
public sealed record RequestSnapshot(int Id, int WorkflowDefinitionId, string WorkflowName, int WorkflowVersion,
    string RequesterId, string Title, string Description, decimal? Amount, string? Department,
    RequestStatus Status, int? CurrentStepId, string? CurrentStepName, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, string RowVersion, IReadOnlyList<RequestHistoryItem> History,
    WorkflowGraph? Graph = null, Guid? CurrentNodeId = null, IReadOnlyDictionary<string, string?>? FormValues = null);
