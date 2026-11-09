using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.Models;
using MiniWorkflowEngine.Services;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Controllers;

public class HomeController(ApplicationDbContext db, RequestQueryService requests) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdmin = User.IsInRole(ApplicationRoles.Administrator);
        var active = await db.WorkflowDefinitions.AnyAsync(x => x.IsActive && x.PublishedAt != null, ct);
        var assigned = await requests.PendingCountAsync(actorId, ct);
        var draft = active ? await db.WorkflowRequests.AsNoTracking()
            .Where(x => x.RequesterId == actorId && x.Status == RequestStatus.Draft &&
                x.WorkflowDefinition.IsActive && x.WorkflowDefinition.PublishedAt != null)
            .OrderByDescending(x => x.Id).Select(x => new HomeDraft(x.Id, x.Title)).FirstOrDefaultAsync(ct) : null;
        int? workflowDraft = isAdmin && !active ? await db.WorkflowDefinitions.AsNoTracking()
            .Where(x => !x.IsActive && x.PublishedAt == null && !db.WorkflowRequests.Any(r => r.WorkflowDefinitionId == x.Id))
            .OrderByDescending(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct) : null;
        int? allPending = isAdmin ? await db.WorkflowRequests.CountAsync(x => x.Status == RequestStatus.Pending, ct) : null;
        return View(new HomeViewModel(active, workflowDraft, draft, assigned, allPending,
            assigned > 0 || User.IsInRole(ApplicationRoles.Approver)));
    }

    [AllowAnonymous]
    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
