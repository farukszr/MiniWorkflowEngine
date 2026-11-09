using System.Text.Json;
using System.Text.Json.Serialization;
using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.Domain;

public enum NodeKind { Start, Task, Approval, Decision, End }
public enum RouteKind { Next, Complete, Approve, Reject, Match, Otherwise }
public enum FieldKind { Text, LongText, Number, Amount, Date, Checkbox, Choice }
public enum EndOutcome { Completed, Rejected }

public sealed record GraphNode(Guid Id, NodeKind Kind, string Name, int X, int Y,
    string? RoleId = null, bool AllowReturn = false, EndOutcome Outcome = EndOutcome.Completed,
    string? UserId = null, int? DepartmentId = null);

public sealed record GraphRoute(Guid From, Guid To, RouteKind Kind, string? Variable = null,
    ConditionOperator Operator = ConditionOperator.Equals, string? Value = null);

public sealed record GraphField(string Key, string Label, FieldKind Kind, bool Required, string[]? Options = null);

public sealed record WorkflowGraph(GraphNode[] Nodes, GraphRoute[] Routes, GraphField[] Fields)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 12,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static WorkflowGraph Empty { get; } = new([], [], []);
    public static WorkflowGraph? Read(string? value) => value is null ? null : JsonSerializer.Deserialize<WorkflowGraph>(value, Json);
    public string Write() => JsonSerializer.Serialize(this, Json);
}
