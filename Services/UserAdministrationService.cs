using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Services;

public sealed class UserAdministrationService(ApplicationDbContext db, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles)
{
    private const int PageSize = 20;

    public async Task<UserListViewModel> ListAsync(string? search, int page)
    {
        search = search?.Trim();
        if (search?.Length > 100) search = search[..100];
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.FullName.Contains(search) || (x.Email != null && x.Email.Contains(search)));
        var count = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);
        var items = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(x => new UserListItem { Id = x.Id, FullName = x.FullName, Email = x.Email ?? "", CreatedAt = x.CreatedAt }).ToListAsync();
        var ids = items.Select(x => x.Id).ToArray();
        var roles = await (from ur in db.UserRoles.AsNoTracking()
                           join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                           where ids.Contains(ur.UserId)
                           select new { ur.UserId, r.Name }).ToListAsync();
        foreach (var item in items)
            item.Roles = roles.Where(x => x.UserId == item.Id).Select(x => x.Name!).Order().ToArray();
        return new UserListViewModel { Users = items, Search = search, Page = page, TotalPages = totalPages, TotalCount = count };
    }

    public async Task<EditUserRolesViewModel?> GetRolesAsync(string id)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (user is null) return null;
        return new EditUserRolesViewModel { Id = user.Id, FullName = user.FullName, Email = user.Email ?? "",
            ConcurrencyStamp = user.ConcurrencyStamp!, Roles = (await users.GetRolesAsync(user)).ToArray() };
    }

    public Task<IdentityResult> CreateAsync(CreateUserViewModel model, string actorId)
        => InTransactionAsync(async () =>
        {
            if (!await IsAdministratorAsync(actorId)) return Failed("Bu işlem için yönetici yetkisi gerekiyor.");
            if (!await ValidRolesAsync(model.Roles)) return Failed("Listeden en az bir geçerli rol seçin.");
            if (model.FullName.Trim().Length is < 2 or > 150) return Failed("Ad soyad 2–150 karakter olmalıdır.");
            var user = new ApplicationUser { FullName = model.FullName.Trim(), Email = model.Email.Trim(), UserName = model.Email.Trim() };
            var created = await users.CreateAsync(user, model.Password);
            if (!created.Succeeded) return created;
            var assigned = await users.AddToRolesAsync(user, model.Roles.Distinct(StringComparer.Ordinal));
            if (!assigned.Succeeded) return assigned;
            db.RecordAdministrationEvent(actorId, "Kullanıcı oluşturuldu", user.Email!, RoleSummary(model.Roles.Distinct()));
            await db.SaveChangesAsync();
            return assigned;
        });

    public Task<IdentityResult> UpdateRolesAsync(EditUserRolesViewModel model, string actorId)
        => InTransactionAsync(async () =>
        {
            if (!await IsAdministratorAsync(actorId)) return Failed("Bu işlem için yönetici yetkisi gerekiyor.");
            if (!await ValidRolesAsync(model.Roles)) return Failed("Listeden en az bir geçerli rol seçin.");
            var user = await users.FindByIdAsync(model.Id);
            if (user is null) return Failed("Kullanıcı bulunamadı.");
            if (user.ConcurrencyStamp != model.ConcurrencyStamp)
                return Failed("Bu kullanıcı başka bir işlemde güncellendi. Sayfayı yenileyip tekrar deneyin.");
            var current = await users.GetRolesAsync(user);
            var selected = model.Roles.Distinct(StringComparer.Ordinal).ToArray();
            if (current.Contains(ApplicationRoles.Administrator) && !selected.Contains(ApplicationRoles.Administrator)
                && (await users.GetUsersInRoleAsync(ApplicationRoles.Administrator)).Count <= 1)
                return Failed("Son yöneticinin yönetici rolü kaldırılamaz. Önce başka bir kullanıcıya yönetici rolü atayın.");
            var remove = current.Except(selected).ToArray();
            var add = selected.Except(current).ToArray();
            if (remove.Length == 0 && add.Length == 0) return IdentityResult.Success;
            var removed = await users.RemoveFromRolesAsync(user, remove);
            if (!removed.Succeeded) return removed;
            var added = await users.AddToRolesAsync(user, add);
            if (!added.Succeeded) return added;
            // Diğer oturumlar bir sonraki istekte iptal edilir.
            var updated = await users.UpdateSecurityStampAsync(user);
            if (!updated.Succeeded) return updated;
            db.RecordAdministrationEvent(actorId, "Roller değiştirildi", user.Email ?? user.Id,
                $"{RoleSummary(current)} → {RoleSummary(selected)}");
            await db.SaveChangesAsync();
            return updated;
        });

    private static string RoleSummary(IEnumerable<string> roles)
        => string.Join(", ", roles.Order().Select(ApplicationRoles.DisplayName));

    private async Task<bool> IsAdministratorAsync(string id)
    {
        var actor = await users.FindByIdAsync(id);
        return actor is not null && await users.IsInRoleAsync(actor, ApplicationRoles.Administrator);
    }

    public Task<string[]> AvailableRoleNamesAsync()
        => db.Roles.AsNoTracking().Where(x => x.Name != null).OrderBy(x => x.Name).Select(x => x.Name!).ToArrayAsync();

    public Task<IdentityResult> CreateRoleAsync(string name, string actorId)
        => InTransactionAsync(async () =>
        {
            if (!await IsAdministratorAsync(actorId)) return Failed("Bu işlem için yönetici yetkisi gerekiyor.");
            name = name.Trim();
            var normalized = roles.NormalizeKey(name);
            if (name.Length is < 2 or > 60 || name.Any(char.IsControl) ||
                ApplicationRoles.All.Any(role => roles.NormalizeKey(ApplicationRoles.DisplayName(role)) == normalized))
                return Failed("2–60 karakterlik farklı bir görev rolü adı girin.");
            if (await roles.RoleExistsAsync(name)) return Failed("Bu rol adı zaten kullanımda.");
            var result = await roles.CreateAsync(new IdentityRole(name));
            if (!result.Succeeded) return result;
            db.RecordAdministrationEvent(actorId, "Görev rolü oluşturuldu", name);
            await db.SaveChangesAsync();
            return result;
        });

    private async Task<bool> ValidRolesAsync(string[]? selected)
    {
        if (selected is not { Length: > 0 }) return false;
        var available = await AvailableRoleNamesAsync();
        return selected.All(role => available.Contains(role, StringComparer.Ordinal));
    }

    private async Task<IdentityResult> InTransactionAsync(Func<Task<IdentityResult>> action)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await action();
            if (result.Succeeded) await transaction.CommitAsync();
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failed("Eşzamanlı değişiklik algılandı. Sayfayı yenileyip tekrar deneyin.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Failed("Bu e-posta adresi kullanımda. Listeyi yenileyip kontrol edin.");
        }
        catch (Exception ex) when (ex is SqlException { Number: 1205 or 1222 } ||
                                  ex is DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 } })
        {
            return Failed("Başka bir yönetim işlemi sürüyor. Sayfayı yenileyip tekrar deneyin.");
        }
    }

    private static IdentityResult Failed(string message) => IdentityResult.Failed(new IdentityError { Description = message });
}
