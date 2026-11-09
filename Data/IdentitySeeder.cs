using Microsoft.AspNetCore.Identity;
using MiniWorkflowEngine.Domain;

namespace MiniWorkflowEngine.Data;

public sealed class IdentitySeeder(RoleManager<IdentityRole> roleManager)
{
    public async Task SeedRolesAsync()
    {
        foreach (var role in ApplicationRoles.All)
        {
            if (await roleManager.RoleExistsAsync(role)) continue;

            var result = await roleManager.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded)
                throw new InvalidOperationException($"Rol oluşturulamadı ({role}): {string.Join("; ", result.Errors.Select(x => x.Description))}");
        }
    }
}
