using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
public sealed class AdministrationController(ApplicationDbContext db) : Controller
{
    public IActionResult Index() => View();

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
