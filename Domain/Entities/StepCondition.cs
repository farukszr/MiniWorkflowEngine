using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.Domain.Entities;

// Bir adımdaki koşullar ileride AND mantığıyla değerlendirilecek.
public sealed class StepCondition
{
    public int Id { get; set; }
    public int WorkflowStepId { get; set; }
    public WorkflowStep WorkflowStep { get; set; } = null!;
    public ConditionField Field { get; set; }
    public ConditionOperator Operator { get; set; }
    public string Value { get; set; } = string.Empty;
}
