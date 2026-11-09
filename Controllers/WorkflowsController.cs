using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Services;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
public sealed class WorkflowsController(WorkflowDefinitionService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1) => View(await service.ListAsync(search, page));

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
        return model is null ? NotFound() : View(model);
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
