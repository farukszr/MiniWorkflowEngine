using System.Globalization;
using System.Text.RegularExpressions;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.Services.Workflow;

public static class GraphRules
{
    public const int MaxDocumentLength = 96_000;
    private static readonly Regex VariableName = new("^[a-z][a-z0-9_]{0,49}$", RegexOptions.CultureInvariant);
    private static readonly StringComparer Turkish = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    public static string? Validate(WorkflowGraph graph, bool publishing)
    {
        if (graph.Nodes is null || graph.Routes is null || graph.Fields is null ||
            graph.Nodes.Length > 60 || graph.Routes.Length > 180 || graph.Fields.Length > 40)
            return "En fazla 60 düğüm, 180 bağlantı ve 40 form alanı tanımlanabilir.";
        if (graph.Nodes.Any(n => n is null || n.Id == Guid.Empty || string.IsNullOrWhiteSpace(n.Name) || n.Name.Trim().Length > 150 ||
                n.X is < 0 or > 6000 || n.Y is < 0 or > 6000) ||
            graph.Nodes.Select(n => n.Id).Distinct().Count() != graph.Nodes.Length)
            return "Düğüm adı, konumu veya kimliği geçersiz.";
        if (graph.Fields.Any(f => f is null || f.Key is null || !VariableName.IsMatch(f.Key) || f.Key is "amount" or "department" ||
                string.IsNullOrWhiteSpace(f.Label) || f.Label.Length > 120 ||
                f.Kind == FieldKind.Choice && (f.Options is null || f.Options.Length is < 1 or > 30 ||
                    f.Options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 100) ||
                    f.Options.Distinct(StringComparer.Ordinal).Count() != f.Options.Length)) ||
            graph.Fields.Select(f => f.Key).Distinct().Count() != graph.Fields.Length)
            return "Form alanı etiketi, değişken anahtarı veya seçenekleri geçersiz. Tutar ve departman anahtarları sistem alanlarıdır.";
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        if (graph.Routes.Any(r => r is null || r.From == r.To || !nodes.ContainsKey(r.From) || !nodes.ContainsKey(r.To) ||
                r.Variable?.Length > 50 || r.Value?.Length > 250) || graph.Routes.Distinct().Count() != graph.Routes.Length)
            return "Bağlantılar mevcut ve farklı düğümler arasında olmalı. Aynı bağlantıyı iki kez eklemeyin.";
        if (!publishing) return null;

        var start = graph.Nodes.Where(n => n.Kind == NodeKind.Start).ToArray();
        if (start.Length != 1 || !graph.Nodes.Any(n => n.Kind == NodeKind.End))
            return "Yayımlamak için bir başlangıç ve en az bir bitiş düğümü gerekir.";
        if (graph.Routes.Any(r => r.To == start[0].Id)) return "Başlangıca gelen bağlantı olamaz.";
        var outgoing = graph.Routes.ToLookup(r => r.From);
        foreach (var node in graph.Nodes)
        {
            var routes = outgoing[node.Id].ToArray();
            var kinds = routes.Select(r => r.Kind).Order().ToArray();
            var valid = node.Kind switch
            {
                NodeKind.Start => kinds.SequenceEqual([RouteKind.Next]),
                NodeKind.Task => HasAssignee(node) && kinds.SequenceEqual([RouteKind.Complete]),
                NodeKind.Approval => HasAssignee(node) && kinds.SequenceEqual([RouteKind.Approve, RouteKind.Reject]) &&
                    nodes[routes.Single(r => r.Kind == RouteKind.Reject).To] is { Kind: NodeKind.End, Outcome: EndOutcome.Rejected },
                NodeKind.Decision => routes.Count(r => r.Kind == RouteKind.Otherwise) == 1 &&
                    routes.Any(r => r.Kind == RouteKind.Match) && routes.All(r => r.Kind == RouteKind.Otherwise || ValidRule(graph, r)),
                NodeKind.End => routes.Length == 0,
                _ => false
            };
            if (!valid) return $"“{node.Name}” düğümünde atama, koşul veya çıkış bağlantısı eksik ya da geçersiz.";
        }
        var visited = new HashSet<Guid>();
        var visiting = new HashSet<Guid>();
        bool Visit(Guid id)
        {
            if (visiting.Contains(id)) return false;
            if (!visited.Add(id)) return true;
            visiting.Add(id);
            var safe = outgoing[id].All(route => Visit(route.To));
            visiting.Remove(id);
            return safe;
        }
        if (!Visit(start[0].Id)) return "Süreç bağlantıları döngü oluşturuyor. Düzeltme için iade işlemini kullanın.";
        return visited.Count == nodes.Count ? null : "Başlangıçtan ulaşılamayan düğümler var.";
    }

    private static bool HasAssignee(GraphNode node)
        => !string.IsNullOrWhiteSpace(node.RoleId);

    public static FieldKind? VariableType(WorkflowGraph graph, string? key) => key switch
    {
        "amount" => FieldKind.Amount,
        "department" => FieldKind.Text,
        _ => graph.Fields.FirstOrDefault(f => f.Key == key)?.Kind
    };

    private static bool ValidRule(WorkflowGraph graph, GraphRoute route)
        => route.Kind == RouteKind.Match && VariableType(graph, route.Variable) is { } type &&
            (route.Operator is ConditionOperator.Equals or ConditionOperator.NotEquals ||
                type is FieldKind.Number or FieldKind.Amount or FieldKind.Date) &&
            !string.IsNullOrWhiteSpace(route.Value) && TryValue(type, route.Value, out var expected) &&
            (type != FieldKind.Choice || graph.Fields.Single(f => f.Key == route.Variable).Options!.Contains(expected, StringComparer.Ordinal));

    public static bool TryValue(FieldKind type, string raw, out string value)
    {
        value = raw.Trim();
        if (value.Length > 2000) return false;
        if (type is FieldKind.Number or FieldKind.Amount)
        {
            if (!decimal.TryParse(value.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var number)) return false;
            if (type == FieldKind.Amount && (number < 0 || number > 9999999999999999.99m || decimal.Round(number, 2) != number)) return false;
            value = number.ToString(CultureInfo.InvariantCulture);
        }
        else if (type == FieldKind.Date)
        {
            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
        }
        else if (type == FieldKind.Checkbox)
        {
            if (!bool.TryParse(value, out var check)) return false;
            value = check ? "true" : "false";
        }
        return true;
    }

    public static bool Matches(FieldKind type, string actual, ConditionOperator op, string expected)
    {
        if (!TryValue(type, actual, out actual) || !TryValue(type, expected, out expected)) return false;
        var comparison = type switch
        {
            FieldKind.Number or FieldKind.Amount => decimal.Parse(actual, CultureInfo.InvariantCulture)
                .CompareTo(decimal.Parse(expected, CultureInfo.InvariantCulture)),
            FieldKind.Date or FieldKind.Checkbox or FieldKind.Choice => StringComparer.Ordinal.Compare(actual, expected),
            _ => Turkish.Compare(actual, expected)
        };
        return op switch
        {
            ConditionOperator.Equals => comparison == 0,
            ConditionOperator.NotEquals => comparison != 0,
            ConditionOperator.GreaterThan => comparison > 0,
            ConditionOperator.GreaterThanOrEqual => comparison >= 0,
            ConditionOperator.LessThan => comparison < 0,
            ConditionOperator.LessThanOrEqual => comparison <= 0,
            _ => false
        };
    }
}
