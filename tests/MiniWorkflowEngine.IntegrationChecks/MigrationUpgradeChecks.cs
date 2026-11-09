using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MiniWorkflowEngine.Data;

internal static class MigrationUpgradeChecks
{
    public static async Task RunAsync()
    {
        var name = $"MiniWorkflowEngine_UpgradeChecks_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(
            $"Server=(localdb)\\MSSQLLocalDB;Database={name};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True").Options;
        await using var db = new ApplicationDbContext(options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261108182632_InitialCreate");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO WorkflowDefinitions (Code, Name, Version, IsActive, CreatedAt)
                VALUES ('LEGACY_ACTIVE', 'Eski etkin', 1, 1, SYSDATETIMEOFFSET()),
                       ('LEGACY_USED', 'Eski kullanılan', 1, 0, SYSDATETIMEOFFSET()),
                       ('LEGACY_DRAFT', 'Eski taslak', 1, 0, SYSDATETIMEOFFSET());
                INSERT INTO AspNetUsers (Id, FullName, CreatedAt, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
                VALUES ('legacy-test', 'Test kullanıcısı', SYSDATETIMEOFFSET(), 0, 0, 0, 1, 0);
                INSERT INTO WorkflowRequests (WorkflowDefinitionId, RequesterId, Title, Description, Status, CreatedAt)
                SELECT Id, 'legacy-test', 'Eski talep', 'Migration doğrulaması', 'Draft', SYSDATETIMEOFFSET()
                FROM WorkflowDefinitions WHERE Code = 'LEGACY_USED';
                """);
            await db.GetService<IMigrator>().MigrateAsync("20261109135242_AddApprovalDelegationAndAdministrationHistory");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO AspNetUsers (Id, FullName, CreatedAt, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
                VALUES ('legacy-delegate', 'Eski vekil', SYSDATETIMEOFFSET(), 0, 0, 0, 1, 0);
                INSERT INTO AspNetRoles (Id, Name, NormalizedName) VALUES ('legacy-role', 'LegacyApproval', 'LEGACYAPPROVAL');
                INSERT INTO ApprovalDelegations (DelegatorId, DelegateId, RoleId, StartsOn, EndsOn)
                VALUES ('legacy-test', 'legacy-delegate', 'legacy-role', '2026-11-01', '2026-11-30');
                """);
            await db.Database.MigrateAsync();
            var items = await db.WorkflowDefinitions.AsNoTracking().ToListAsync();
            if (items.Single(x => x.Code == "LEGACY_ACTIVE").PublishedAt is null ||
                items.Single(x => x.Code == "LEGACY_USED").PublishedAt is null ||
                items.Single(x => x.Code == "LEGACY_DRAFT").PublishedAt is not null ||
                await db.WorkflowRequests.CountAsync() != 1 ||
                await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM ApprovalDelegations").SingleAsync() != 1)
                throw new Exception("Migration mevcut süreç durumlarını ve talepleri korumadı.");
            Console.WriteLine("BAŞARILI: Migration yükseltmesinde mevcut süreçler, talepler ve eski vekâlet kayıtları korundu.");
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
