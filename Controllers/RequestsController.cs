using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.Services;
using MiniWorkflowEngine.Services.Interfaces;
using MiniWorkflowEngine.Services.Workflow;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Controllers;

[Authorize]
public sealed class RequestsController(IWorkflowEngine engine, RequestQueryService queries) : Controller
{
    private string ActorId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, RequestStatus? status, int page = 1, CancellationToken ct = default)
        => View("List", await queries.ListAsync(ActorId, RequestListScope.Mine, search, status, page, ct));

    [HttpGet]
    public async Task<IActionResult> Inbox(string? search, int page = 1, bool overdue = false, CancellationToken ct = default)
        => View("List", await queries.ListAsync(ActorId, RequestListScope.Inbox, search, null, page, ct, overdue));

    [HttpGet]
    public async Task<IActionResult> PendingCount(CancellationToken ct)
        => Json(new { count = await queries.PendingCountAsync(ActorId, ct) });

    [HttpGet]
    [Authorize(Roles = ApplicationRoles.Administrator)]
    public async Task<IActionResult> All(string? search, RequestStatus? status, int page = 1, bool overdue = false, CancellationToken ct = default)
        => View("List", await queries.ListAsync(ActorId, RequestListScope.All, search, status, page, ct, overdue));

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var workflows = await queries.WorkflowsAsync(null, ct);
        return View("Edit", new RequestEditorViewModel
        {
            Workflows = workflows,
            WorkflowDefinitionId = workflows.Count == 1 ? workflows[0].Id : 0
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(RequestEditorViewModel model, CancellationToken ct)
    {
        if (model.Id != 0) return BadRequest();
        if (ModelState.IsValid)
        {
            var result = await engine.CreateDraftAsync(model.ToInput(), ActorId, ct);
            if (result.Succeeded) return Saved(result, "Talep taslağı oluşturuldu. Hazır olduğunda onaya gönderebilirsiniz.");
            ModelState.AddModelError(string.Empty, result.Message);
        }
        model.Workflows = await queries.WorkflowsAsync(null, ct);
        return View("Edit", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await engine.GetAsync(id, ActorId, ct);
        if (!result.Succeeded) return Denied(result);
        var request = result.Request!;
        if (request.RequesterId != ActorId) return ForbiddenView();
        if (request.Status != RequestStatus.Draft) return RedirectToAction(nameof(Details), new { id });
        return View(new RequestEditorViewModel { Id = request.Id, RowVersion = request.RowVersion,
            WorkflowDefinitionId = request.WorkflowDefinitionId, Title = request.Title, Description = request.Description,
            Amount = request.Amount?.ToString("0.00", CultureInfo.GetCultureInfo("tr-TR")), Department = request.Department,
            Workflows = await queries.WorkflowsAsync(request.WorkflowDefinitionId, ct) });
    }

    [HttpPost]
    public async Task<IActionResult> Edit([FromRoute] int id, RequestEditorViewModel model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        var accessible = await engine.GetAsync(id, ActorId, ct);
        if (!accessible.Succeeded) return Denied(accessible);
        if (accessible.Request!.RequesterId != ActorId) return ForbiddenView();
        if (ModelState.IsValid)
        {
            var result = await engine.UpdateDraftAsync(new RequestCommand(id, model.RowVersion ?? string.Empty), model.ToInput(), ActorId, ct);
            if (result.Succeeded) return Saved(result, "Talep taslağı güncellendi.");
            if (result.Error == WorkflowError.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError(string.Empty, result.Message);
        }
        model.Workflows = await queries.WorkflowsAsync(accessible.Request.WorkflowDefinitionId, ct);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, [FromQuery] RequestListScope? scope, CancellationToken ct)
    {
        var result = await engine.GetAsync(id, ActorId, ct);
        if (!result.Succeeded) return Denied(result);
        return View(await DetailPageAsync(result.Request!, scope, ct));
    }

    [HttpPost]
    public Task<IActionResult> Submit([FromRoute] int id, RequestActionViewModel model, CancellationToken ct)
        => ExecuteAsync(id, model, engine.SubmitAsync, "Talep onaya gönderildi.", ct);
    [HttpPost]
    public Task<IActionResult> Approve([FromRoute] int id, RequestActionViewModel model, CancellationToken ct)
        => ExecuteAsync(id, model, engine.ApproveAsync, "Onay kaydedildi.", ct);
    [HttpPost]
    public Task<IActionResult> Reject([FromRoute] int id, RequestActionViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Comment)) ModelState.AddModelError(nameof(model.Comment), "Ret gerekçesi girin.");
        return ExecuteAsync(id, model, engine.RejectAsync, "Talep reddedildi.", ct);
    }
    [HttpPost]
    public Task<IActionResult> Cancel([FromRoute] int id, RequestActionViewModel model, CancellationToken ct)
        => ExecuteAsync(id, model, engine.CancelAsync, "Talep iptal edildi.", ct);

    private async Task<IActionResult> ExecuteAsync(int id, RequestActionViewModel model,
        Func<RequestCommand, string, CancellationToken, Task<WorkflowResult>> operation, string success, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        if (ModelState.IsValid)
        {
            var result = await operation(new RequestCommand(id, model.RowVersion, model.Comment), ActorId, ct);
            if (result.Succeeded) return Saved(result, success, model.Scope);
            if (result.Error is WorkflowError.Forbidden or WorkflowError.NotFound) return Denied(result);
            if (result.Error == WorkflowError.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError(string.Empty, result.Message);
        }
        var current = await engine.GetAsync(id, ActorId, ct);
        if (!current.Succeeded) return Denied(current);
        var details = await DetailPageAsync(current.Request!, model.Scope, ct);
        details.Comment = model.Comment;
        return View("Details", details);
    }

    private async Task<RequestDetailsViewModel> DetailPageAsync(RequestSnapshot request, RequestListScope? scope, CancellationToken ct)
    {
        var details = await queries.DetailsAsync(request, ActorId, ct);
        var isAdmin = User.IsInRole(ApplicationRoles.Administrator);
        details.Scope = scope switch
        {
            RequestListScope.Inbox => RequestListScope.Inbox,
            RequestListScope.All when isAdmin => RequestListScope.All,
            RequestListScope.Mine when request.RequesterId == ActorId => RequestListScope.Mine,
            _ when details.CanDecide && !details.IsAdministrativeDecision => RequestListScope.Inbox,
            _ when request.RequesterId == ActorId => RequestListScope.Mine,
            _ when isAdmin => RequestListScope.All,
            _ => RequestListScope.Inbox
        };
        return details;
    }

    private IActionResult Saved(WorkflowResult result, string message, RequestListScope? scope = null)
    {
        TempData["Success"] = result.Request!.Status == RequestStatus.Approved ? "Talep onaylanarak tamamlandı." : message;
        return RedirectToAction(nameof(Details), new { id = result.Request.Id, scope });
    }
    private IActionResult Denied(WorkflowResult result)
        => result.Error == WorkflowError.Forbidden ? ForbiddenView() : NotFound();

    private IActionResult ForbiddenView()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("~/Views/Account/AccessDenied.cshtml");
    }
}
