using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
public sealed class DepartmentsController(ApplicationDbContext db) : Controller
{
    private string ActorId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var departments = await db.Departments.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new DepartmentListItem(x.Id, x.Name, db.Users.Count(u => u.DepartmentId == x.Id))).ToListAsync(ct);
        var members = await db.Users.AsNoTracking().OrderBy(x => x.FullName)
            .Select(x => new DepartmentMember(x.Id, x.FullName, x.Email, x.DepartmentId)).ToListAsync(ct);
        return View(new DepartmentsViewModel(departments, members,
            departments.Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToArray()));
    }

    [HttpPost]
    public async Task<IActionResult> Save(int? id, string? name, CancellationToken ct)
    {
        name = name?.Trim();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(name) || name.Length is < 2 or > 100)
            return Message("2–100 karakterlik bir departman adı girin.", error: true);
        var department = id is null ? new Department() : await db.Departments.FindAsync([id.Value], ct);
        if (department is null) return NotFound();
        if (department.Name == name) return RedirectToAction(nameof(Index));
        if (await db.Departments.AnyAsync(x => x.Name == name && x.Id != id, ct))
            return Message("Bu departman adı zaten kullanımda.", error: true);
        var oldName = department.Name;
        department.Name = name;
        if (id is null) db.Departments.Add(department);
        db.RecordAdministrationEvent(ActorId, id is null ? "Departman oluşturuldu" : "Departman adı değiştirildi", name,
            id is null ? null : oldName);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { return Message("Bu departman adı zaten kullanımda.", error: true); }
        return Message("Departman kaydedildi.");
    }

    [HttpPost]
    public async Task<IActionResult> Assign(string userId, int? departmentId, CancellationToken ct)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(userId)) return Message("Geçerli bir kullanıcı ve departman seçin.", error: true);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return NotFound();
        if (user.DepartmentId == departmentId) return RedirectToAction(nameof(Index));
        var department = departmentId is null ? null : await db.Departments.FindAsync([departmentId.Value], ct);
        if (departmentId is not null && department is null) return Message("Departman bulunamadı.", error: true);
        user.DepartmentId = departmentId;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        db.RecordAdministrationEvent(ActorId, "Departman üyeliği güncellendi", user.Email ?? user.FullName, department?.Name ?? "Üyelik kaldırıldı");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Message("Kullanıcı güncellendi. İşlemi tekrar deneyin.", error: true); }
        return Message("Departman üyeliği güncellendi.");
    }

    private IActionResult Message(string message, bool error = false)
    {
        TempData[error ? "Error" : "Success"] = message;
        return RedirectToAction(nameof(Index));
    }
}
