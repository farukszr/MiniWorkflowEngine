namespace MiniWorkflowEngine.ViewModels;

public sealed record AdministrationHistoryItem(string Actor, string Operation, string Target, string? Detail, DateTimeOffset CreatedAt);

public sealed record AdministrationHistoryViewModel(int Page, bool HasNext, IReadOnlyList<AdministrationHistoryItem> Items);
