namespace MiniWorkflowEngine.ViewModels;

public sealed record HomeDraft(int Id, string Title);

public sealed record HomeViewModel(bool HasActiveWorkflow, int? WorkflowDraftId, HomeDraft? RequestDraft,
    int AssignedApprovals, int? AllPendingRequests, bool ShowApprovalCard);
