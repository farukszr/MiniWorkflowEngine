using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Services;
using MiniWorkflowEngine.Services.Workflow;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
public sealed class WorkflowsController(WorkflowDefinitionService service, ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, WorkflowListState state = WorkflowListState.All)
        => View(await service.ListAsync(search, page, state));

    [HttpGet]
    public async Task<IActionResult> Create() => View("Edit", new WorkflowEditorViewModel
        { CanEdit = true, AvailableRoles = await service.RolesAsync(), Steps = [new WorkflowStepViewModel()] });

    [HttpPost]
    public async Task<IActionResult> Create(WorkflowEditorViewModel model)
    {
        if (model.Id != 0) return BadRequest();
        return await SaveAsync(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await service.GetAsync(id);
        if (model is null) return NotFound();
        if (model.IsGraph) return RedirectToAction(nameof(Designer), new { id });
        return model.CanEdit ? View(model) : RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Edit([FromRoute] int id, WorkflowEditorViewModel model)
    {
        if (id != model.Id) return BadRequest();
        var existing = await service.GetAsync(id);
        if (existing is null) return NotFound();
        model.Version = existing.Version;
        return await SaveAsync(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var model = await service.GetAsync(id);
        if (model is null) return NotFound();
        if (model.CanEdit) ViewData["UnstaffedSteps"] = await service.UnstaffedAsync(id);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Designer(int? id)
    {
        var model = id is null ? new GraphEditorViewModel { GraphJson = WorkflowGraph.Empty.Write() } : await service.GetGraphAsync(id.Value);
        return model is null ? NotFound() : await DesignerViewAsync(model);
    }

    [HttpPost]
    [RequestSizeLimit(256_000)]
    public async Task<IActionResult> Designer(int? id, GraphEditorViewModel model, string? next)
    {
        if ((id ?? 0) != model.Id) return BadRequest();
        if (ModelState.IsValid)
        {
            var result = await service.SaveGraphAsync(model, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (result.Succeeded)
            {
                TempData["Success"] = "Görsel taslak kaydedildi. Kullanıma açmak için yayımlayın.";
                return RedirectToAction(next == "preview" ? nameof(GraphPreview) : nameof(Details), new { id = result.Id });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        return await DesignerViewAsync(model);
    }

    private async Task<IActionResult> DesignerViewAsync(GraphEditorViewModel model)
    {
        ViewData["ReadOnly"] = model.Id != 0 && (await service.GetAsync(model.Id))?.CanEdit != true;
        ViewData["Roles"] = await service.RolesAsync();
        ViewData["Departments"] = await db.Departments.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync();
        ViewData["Users"] = await db.Users.AsNoTracking().OrderBy(x => x.FullName)
            .Select(x => new SelectListItem(x.FullName + " · " + x.Email, x.Id)).ToListAsync();
        return View("Designer", model);
    }

    [HttpGet]
    public Task<IActionResult> GraphPreview(int id) => GraphPreviewAsync(id, new(), evaluate: false);

    [HttpPost]
    public Task<IActionResult> GraphPreview(int id, GraphPreviewViewModel model) => GraphPreviewAsync(id, model, evaluate: true);

    private async Task<IActionResult> GraphPreviewAsync(int id, GraphPreviewViewModel model, bool evaluate)
    {
        var design = await service.GetGraphAsync(id);
        if (design is null) return NotFound();
        var graph = WorkflowGraph.Read(design.GraphJson)!;
        model.Id = design.Id;
        model.Name = design.Name;
        model.Fields = graph.Fields;
        if (evaluate)
        {
            if (!AmountInput.TryParse(model.Amount, out var amount)) ModelState.AddModelError(nameof(model.Amount), AmountInput.ErrorMessage);
            var error = GraphRules.Validate(graph, publishing: true);
            if (error is not null) ModelState.AddModelError(string.Empty, error);
            if (ModelState.IsValid)
            {
                error = GraphRuntime.ValidateValues(graph, model.FormValues, submitting: true, out var values);
                if (error is not null) ModelState.AddModelError(string.Empty, error);
                else
                {
                    var request = new WorkflowRequest { Amount = amount, Department = model.Department,
                        FormValuesJson = System.Text.Json.JsonSerializer.Serialize(values) };
                    var start = graph.Nodes.Single(n => n.Kind == NodeKind.Start);
                    var target = GraphRuntime.Follow(graph, request, start.Id, RouteKind.Next, model.Route.Add);
                    model.Result = target.Kind switch
                    {
                        NodeKind.Task => $"İlk görev: {target.Name}",
                        NodeKind.Approval => $"İlk onay: {target.Name}",
                        _ when target.Outcome == EndOutcome.Rejected => $"Talep reddedilir: {target.Name}",
                        _ => $"Süreç doğrudan tamamlanır: {target.Name}"
                    };
                    model.IsDraft = (await service.GetAsync(id))?.CanEdit == true;
                }
            }
        }
        return View("GraphPreview", model);
    }

    [HttpGet]
    public async Task<IActionResult> Preview(int id, [FromQuery] WorkflowPreviewInput input, bool evaluate, CancellationToken ct)
    {
        if (!AmountInput.TryParse(input.Amount, out var amount)) ModelState.AddModelError(nameof(input.Amount), AmountInput.ErrorMessage);
        var preview = await service.PreviewAsync(id, evaluate && ModelState.IsValid, amount, input.Department, ct);
        if (preview is null) return NotFound();
        if (preview.Error is not null) ModelState.AddModelError(string.Empty, preview.Error);
        ViewData["Preview"] = preview;
        return View(input);
    }

    [HttpPost]
    public Task<IActionResult> Publish([FromRoute] int id, WorkflowCommandViewModel model)
        => CommandAsync(id, model, service.PublishAsync, "Süreç yayımlandı. Önceki etkin sürüm arşivlendi.");

    [HttpPost]
    public Task<IActionResult> Deactivate([FromRoute] int id, WorkflowCommandViewModel model)
        => CommandAsync(id, model, service.DeactivateAsync, "Süreç devre dışı bırakıldı. Mevcut taleplerin sürüm bağlantısı korundu.");

    [HttpPost]
    public Task<IActionResult> NewVersion([FromRoute] int id, WorkflowCommandViewModel model)
        => CommandAsync(id, model, service.NewVersionAsync, "Yeni taslak sürüm oluşturuldu.", edit: true);

    private async Task<IActionResult> SaveAsync(WorkflowEditorViewModel model)
    {
        if (ModelState.IsValid)
        {
            var result = await service.SaveAsync(model, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (result.Succeeded)
            {
                TempData["Success"] = "Süreç taslağı kaydedildi. Kullanıma açmak için yayımlayın.";
                return RedirectToAction(nameof(Details), new { id = result.Id });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        model.AvailableRoles = await service.RolesAsync();
        return View("Edit", model);
    }

    private async Task<IActionResult> CommandAsync(int id, WorkflowCommandViewModel model,
        Func<WorkflowCommandViewModel, string, Task<WorkflowOperationResult>> action, string success, bool edit = false)
    {
        if (id != model.Id) return BadRequest();
        if (await service.GetAsync(id) is null) return NotFound();
        if (!ModelState.IsValid) TempData["Error"] = "Sürüm bilgisi geçersiz; sayfayı yenileyin.";
        else
        {
            var result = await action(model, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (result.Succeeded)
            {
                TempData["Success"] = success;
                return RedirectToAction(edit ? nameof(Edit) : nameof(Details), new { id = result.Id });
            }
            TempData["Error"] = result.Error;
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}
