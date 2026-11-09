using System.Globalization;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.Services.Workflow;

public sealed class InvalidWorkflowDefinitionException(string message) : Exception(message);

public sealed class WorkflowConditionEvaluator
{
    private static readonly StringComparer DepartmentComparer = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    public Dictionary<int, bool> EvaluateSteps(IReadOnlyList<WorkflowStep> orderedSteps,
        IReadOnlyCollection<string> allowedRoles, decimal? amount, string? department)
    {
        if (orderedSteps.Count is < 1 or > 20 || !orderedSteps.Select(x => x.Order).SequenceEqual(Enumerable.Range(1, orderedSteps.Count)))
            throw new InvalidWorkflowDefinitionException("Sürecin onay sırası geçersiz. Yöneticiye başvurun.");
        if (orderedSteps.Any(x => !allowedRoles.Contains(x.ApproverRoleId) || x.Conditions.Count > 10))
            throw new InvalidWorkflowDefinitionException("Süreçte geçersiz rol veya koşul sayısı var.");
        return orderedSteps.ToDictionary(x => x.Id, x => IsApplicable(x, amount, department));
    }

    public static (bool Amount, bool Department) RequiredInputs(IEnumerable<WorkflowStep> steps)
    {
        var fields = steps.SelectMany(x => x.Conditions).Select(x => x.Field).ToHashSet();
        return (fields.Contains(ConditionField.Amount), fields.Contains(ConditionField.Department));
    }

    public bool IsApplicable(WorkflowStep step, decimal? amount, string? department)
    {
        // Tüm koşulları doğrula; önceki yanlış koşul bozuk sonraki koşulu gizlemesin.
        var results = step.Conditions.Select(condition => Evaluate(condition, amount, department)).ToArray();
        return results.All(x => x);
    }

    private static bool Evaluate(StepCondition condition, decimal? amount, string? department)
    {
        if (!Enum.IsDefined(condition.Field) || !Enum.IsDefined(condition.Operator) || string.IsNullOrWhiteSpace(condition.Value))
            throw new InvalidWorkflowDefinitionException("Süreçte geçersiz bir koşul var. Yöneticiye başvurun.");
        if (condition.Field == ConditionField.Department)
        {
            if (condition.Operator is not (ConditionOperator.Equals or ConditionOperator.NotEquals) || condition.Value.Trim().Length > 100)
                throw new InvalidWorkflowDefinitionException("Departman koşulu geçersiz. Yöneticiye başvurun.");
            if (string.IsNullOrWhiteSpace(department)) return false;
            var equal = DepartmentComparer.Equals(department.Trim(), condition.Value.Trim());
            return condition.Operator == ConditionOperator.Equals ? equal : !equal;
        }
        if (!decimal.TryParse(condition.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var expected)
            || expected < 0 || expected > 9999999999999999.99m || decimal.Round(expected, 2) != expected)
            throw new InvalidWorkflowDefinitionException("Tutar koşulu geçersiz. Yöneticiye başvurun.");
        if (amount is null) return false;
        return condition.Operator switch
        {
            ConditionOperator.Equals => amount.Value == expected,
            ConditionOperator.NotEquals => amount.Value != expected,
            ConditionOperator.GreaterThan => amount.Value > expected,
            ConditionOperator.GreaterThanOrEqual => amount.Value >= expected,
            ConditionOperator.LessThan => amount.Value < expected,
            ConditionOperator.LessThanOrEqual => amount.Value <= expected,
            _ => throw new InvalidWorkflowDefinitionException("Karşılaştırma geçersiz.")
        };
    }
}
