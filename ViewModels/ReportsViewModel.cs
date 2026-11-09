using Microsoft.AspNetCore.Mvc.Rendering;
using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.ViewModels;

public sealed record ProcessReportRow(string Code, string Name, int Version, int Total, int Pending, int Overdue,
    int Approved, int Rejected, int Returned, double? AverageMinutes);

public sealed record ReportsViewModel(DateOnly? From, DateOnly? To, int? DepartmentId, string? Code, RequestStatus? Status,
    IReadOnlyList<SelectListItem> Departments, IReadOnlyList<SelectListItem> Processes, IReadOnlyList<ProcessReportRow> Rows);
