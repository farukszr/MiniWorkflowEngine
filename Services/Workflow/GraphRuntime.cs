using System.Globalization;
using System.Text.Json;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;

namespace MiniWorkflowEngine.Services.Workflow;

internal static class GraphRuntime
{
    public static Dictionary<string, string?> ReadValues(string? json)
        => json is null ? new(StringComparer.Ordinal) : JsonSerializer.Deserialize<Dictionary<string, string?>>(json)!;

    public static string? ValidateValues(WorkflowGraph graph, IReadOnlyDictionary<string, string?> values,
        bool submitting, out Dictionary<string, string?> normalized)
    {
        normalized = new(StringComparer.Ordinal);
        if (values.Count > graph.Fields.Length || values.Keys.Any(key => !graph.Fields.Any(field => field.Key == key)))
            return "Form sürümünde bulunmayan alan gönderildi. Sayfayı yenileyin.";
        foreach (var field in graph.Fields)
        {
            var raw = values.GetValueOrDefault(field.Key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (submitting && field.Required) return $"“{field.Label}” alanını doldurun.";
                continue;
            }
            if (!GraphRules.TryValue(field.Kind, raw, out var value) ||
                field.Kind == FieldKind.Choice && !field.Options!.Contains(value, StringComparer.Ordinal))
                return $"“{field.Label}” alanındaki değer geçersiz.";
            normalized.Add(field.Key, value);
        }
        return null;
    }

    public static GraphNode Follow(WorkflowGraph graph, WorkflowRequest request, Guid from, RouteKind action,
        Action<string> onDecision)
    {
        var values = ReadValues(request.FormValuesJson);
        values["amount"] = request.Amount?.ToString(CultureInfo.InvariantCulture);
        values["department"] = request.Department;
        var next = Target(from, action);
        for (var i = 0; i < graph.Nodes.Length; i++)
        {
            if (next.Kind != NodeKind.Decision) return next;
            var selected = graph.Routes.FirstOrDefault(route => route.From == next.Id && route.Kind == RouteKind.Match &&
                route.Variable is { } key && GraphRules.VariableType(graph, key) is { } type &&
                values.GetValueOrDefault(key) is { } actual && GraphRules.Matches(type, actual, route.Operator, route.Value!));
            var target = selected is null ? Target(next.Id, RouteKind.Otherwise) : Node(selected.To);
            onDecision($"{next.Name} → {target.Name}");
            next = target;
        }
        throw new InvalidWorkflowDefinitionException("Süreç yürütme sınırını aştı.");

        GraphNode Node(Guid id) => graph.Nodes.Single(n => n.Id == id);
        GraphNode Target(Guid id, RouteKind kind) => Node(graph.Routes.Single(r => r.From == id && r.Kind == kind).To);
    }
}
