using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;

namespace MiniWorkflowEngine.Services;

public sealed class AdministratorBootstrapper(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public async Task<IdentityResult> CreateAsync(string email, string fullName, string password)
    {
        email = email.Trim();
        fullName = fullName.Trim();
        if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email) || fullName.Length is < 2 or > 150)
            return Failed("Geçerli bir e-posta ve 2–150 karakter uzunluğunda ad soyad girin.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if ((await users.GetUsersInRoleAsync(ApplicationRoles.Administrator)).Count > 0)
            return Failed("Yönetici hesabı zaten mevcut. İlk kurulum komutu tekrar yönetici oluşturamaz.");
        if (await users.FindByEmailAsync(email) is not null || await users.FindByNameAsync(email) is not null)
            return Failed("Bu adres kullanımda. Mevcut hesaplara ilk kurulum komutuyla yetki verilmez.");

        var user = new ApplicationUser { Email = email, UserName = email, FullName = fullName };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded) return created;
        var assigned = await users.AddToRoleAsync(user, ApplicationRoles.Administrator);
        if (!assigned.Succeeded) return assigned;
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }

    private static IdentityResult Failed(string description)
        => IdentityResult.Failed(new IdentityError { Description = description });
}
