using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Services;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
public sealed class UsersController(UserAdministrationService service, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager, PasswordRecoveryService recovery) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1) => View(await service.ListAsync(search, page));

    [HttpGet]
    public async Task<IActionResult> RecoveryCode(string id)
    {
        var model = await recovery.GetAccountAsync(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    public async Task<IActionResult> IssueRecoveryCode([FromRoute] string id, CancellationToken ct)
    {
        var model = await recovery.IssueAsync(id, users.GetUserId(User)!, ct);
        return model is null ? NotFound() : View("RecoveryCode", model);
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateUserViewModel { Roles = [ApplicationRoles.Employee] });

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        if (ModelState.IsValid)
        {
            var result = await service.CreateAsync(model, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (result.Succeeded)
            {
                TempData["Success"] = "Kullanıcı oluşturuldu ve rolleri atandı.";
                return RedirectToAction(nameof(Index));
            }
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
        }
        model.Password = model.ConfirmPassword = string.Empty;
        ModelState.SetModelValue(nameof(model.Password), string.Empty, string.Empty);
        ModelState.SetModelValue(nameof(model.ConfirmPassword), string.Empty, string.Empty);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Roles(string id)
    {
        var model = await service.GetRolesAsync(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Roles([FromRoute] string id, EditUserRolesViewModel model)
    {
        if (id != model.Id) return BadRequest();
        var existing = await service.GetRolesAsync(id);
        if (existing is null) return NotFound();
        if (ModelState.IsValid)
        {
            var result = await service.UpdateRolesAsync(model, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (result.Succeeded)
            {
                if (id == users.GetUserId(User))
                {
                    var user = await users.FindByIdAsync(id);
                    await signInManager.RefreshSignInAsync(user!);
                    if (!await users.IsInRoleAsync(user!, ApplicationRoles.Administrator))
                        return RedirectToAction("Index", "Home");
                }
                TempData["Success"] = "Kullanıcı rolleri güncellendi.";
                return RedirectToAction(nameof(Index));
            }
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
        }
        model.FullName = existing.FullName;
        model.Email = existing.Email;
        return View(model);
    }
}
