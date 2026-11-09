using MiniWorkflowEngine.Services.Workflow;

namespace MiniWorkflowEngine.Services.Interfaces;

public interface IWorkflowEngine
{
    Task<WorkflowResult> CreateDraftAsync(RequestDraftInput input, string actorId, CancellationToken cancellationToken = default);
    Task<WorkflowResult> UpdateDraftAsync(RequestCommand command, RequestDraftInput input, string actorId, CancellationToken cancellationToken = default);
    Task<WorkflowResult> SubmitAsync(RequestCommand command, string actorId, CancellationToken cancellationToken = default);
    Task<WorkflowResult> ApproveAsync(RequestCommand command, string actorId, CancellationToken cancellationToken = default);
    Task<WorkflowResult> RejectAsync(RequestCommand command, string actorId, CancellationToken cancellationToken = default);
    Task<WorkflowResult> CancelAsync(RequestCommand command, string actorId, CancellationToken cancellationToken = default);
    Task<WorkflowResult> GetAsync(int requestId, string actorId, CancellationToken cancellationToken = default);
}
