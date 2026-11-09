using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
public sealed class AdministrationController(ApplicationDbContext db) : Controller
{
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Reports(DateOnly? from, DateOnly? to, int? departmentId, string? code,
        RequestStatus? status, CancellationToken ct)
    {
        var query = db.WorkflowRequests.AsNoTracking();
        if (from is { } first)
        {
            var start = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(x => x.CreatedAt >= start);
        }
        if (to is { } last)
        {
            var end = new DateTimeOffset(last.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            query = query.Where(x => x.CreatedAt <= end);
        }
        if (from > to) ModelState.AddModelError(string.Empty, "Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        if (departmentId is not null) query = query.Where(x => x.RequesterDepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(code)) query = query.Where(x => x.WorkflowDefinition.Code == code);
        if (status is not null) query = query.Where(x => x.Status == status);
        var cutoff = DateTimeOffset.UtcNow.AddHours(-ApprovalWaiting.OverdueHours);
        var rows = new List<ProcessReportRow>();
        if (ModelState.IsValid)
        {
            var overdue = await query.Where(x => (x.Status == RequestStatus.Pending || x.Status == RequestStatus.Returned) &&
                    (x.Actions.Where(a => x.Status == RequestStatus.Returned ? a.ActionType == RequestActionType.Returned :
                            a.ActionType == RequestActionType.Submitted || a.ActionType == RequestActionType.Resubmitted ||
                            a.ActionType == RequestActionType.Approved || a.ActionType == RequestActionType.AdministratorApproved ||
                            a.ActionType == RequestActionType.TaskCompleted)
                        .OrderByDescending(a => a.Id).Select(a => (DateTimeOffset?)a.CreatedAt).FirstOrDefault() ?? x.CreatedAt) <= cutoff)
                .GroupBy(x => x.WorkflowDefinitionId).Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
            var totals = await query.GroupBy(x => new
                {
                    x.WorkflowDefinitionId, x.WorkflowDefinition.Code, x.WorkflowDefinition.Name, x.WorkflowDefinition.Version
                })
                .Select(g => new
                {
                    g.Key, Total = g.Count(), Pending = g.Count(x => x.Status == RequestStatus.Pending),
                    Approved = g.Count(x => x.Status == RequestStatus.Approved), Rejected = g.Count(x => x.Status == RequestStatus.Rejected),
                    Returned = g.Count(x => x.Status == RequestStatus.Returned),
                    AverageMinutes = g.Average(x => (double?)EF.Functions.DateDiffMinute(x.CreatedAt, x.CompletedAt))
                }).ToListAsync(ct);
            rows = totals.OrderBy(x => x.Key.Code).ThenByDescending(x => x.Key.Version)
                .Select(x => new ProcessReportRow(x.Key.Code, x.Key.Name, x.Key.Version, x.Total, x.Pending,
                    overdue.GetValueOrDefault(x.Key.WorkflowDefinitionId), x.Approved, x.Rejected, x.Returned, x.AverageMinutes)).ToList();
        }
        var departments = await db.Departments.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync(ct);
        var processes = await db.WorkflowDefinitions.AsNoTracking().Select(x => x.Code).Distinct().OrderBy(x => x)
            .Select(x => new SelectListItem(x, x)).ToListAsync(ct);
        return View(new ReportsViewModel(from, to, departmentId, code, status, departments, processes, rows));
    }

    [HttpGet]
    public async Task<IActionResult> History(int page = 1, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);
        var entries = await db.AdministrationEvents.AsNoTracking().OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(x => new AdministrationHistoryItem(x.Actor.FullName, x.Operation, x.Target, x.Detail, x.CreatedAt))
            .ToListAsync(ct);
        return View(new AdministrationHistoryViewModel(page, entries.Count > pageSize, entries.Take(pageSize).ToArray()));
    }
}
