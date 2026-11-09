using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.Domain;

public static class WorkflowLabels
{
    public static string Field(ConditionField field) => field switch { ConditionField.Amount => "Tutar", ConditionField.Department => "Departman", _ => "Bilinmeyen" };
    public static string Operator(ConditionOperator op) => op switch
    {
        ConditionOperator.Equals => "Eşittir", ConditionOperator.NotEquals => "Eşit değildir",
        ConditionOperator.GreaterThan => "Büyüktür", ConditionOperator.GreaterThanOrEqual => "Büyük veya eşittir",
        ConditionOperator.LessThan => "Küçüktür", ConditionOperator.LessThanOrEqual => "Küçük veya eşittir", _ => "Bilinmeyen"
    };
}
