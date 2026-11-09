namespace MiniWorkflowEngine.Domain.Enums;

public enum RequestStatus { Draft, Pending, Approved, Rejected, Cancelled, Returned }
public enum RequestActionType
{
    Submitted, Approved, Rejected, Cancelled, StepSkipped, DraftCreated, DraftUpdated, Completed,
    AdministratorApproved, AdministratorRejected, TaskCompleted, Returned, Resubmitted, RouteSelected
}
public enum ConditionField { Amount, Department }
public enum ConditionOperator { Equals, NotEquals, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual }
