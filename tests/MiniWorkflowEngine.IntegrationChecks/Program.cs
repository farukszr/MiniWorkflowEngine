using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.Extensions;

// Yalnızca bu çalıştırma için oluşturulan izole LocalDB veritabanını kullanır.
var databaseName = $"MiniWorkflowEngine_Checks_{Guid.NewGuid():N}";
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:DefaultConnection"] = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
}).Build();
var services = new ServiceCollection();
services.AddLogging();
services.AddApplicationInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
try
{
    await db.Database.MigrateAsync();
    if (db.Database.HasPendingModelChanges())
        throw new Exception("Model ile migration eşleşmiyor.");
    var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
    await seeder.SeedRolesAsync();
    await seeder.SeedRolesAsync();
    if (await db.Roles.CountAsync() != ApplicationRoles.All.Count)
        throw new Exception("Rol kurulumu tekrar çalıştırılabilir değil.");
    Console.WriteLine("BAŞARILI: Migration ve tekrarlanabilir rol kurulumu.");

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var user = new ApplicationUser { UserName = "test@example.invalid", Email = "test@example.invalid", FullName = "Test Kullanıcısı" };
    EnsureIdentity(await userManager.CreateAsync(user, "Test-Password-2026!"));
    EnsureIdentity(await userManager.AddToRoleAsync(user, ApplicationRoles.Employee));
    if (!await userManager.CheckPasswordAsync(user, "Test-Password-2026!") ||
        !await userManager.IsInRoleAsync(user, ApplicationRoles.Employee))
        throw new Exception("Identity parola/rol doğrulaması başarısız.");
    Console.WriteLine("BAŞARILI: Identity kullanıcı, parola ve rol ilişkisi.");

    var role = await db.Roles.SingleAsync(x => x.Name == ApplicationRoles.Approver);
    var workflow = new WorkflowDefinition { Code = "EXPENSE", Name = "Masraf Onayı", IsActive = true };
    var step = new WorkflowStep { Name = "Yönetici Onayı", Order = 1, ApproverRoleId = role.Id };
    step.Conditions.Add(new StepCondition { Field = ConditionField.Amount, Operator = ConditionOperator.GreaterThan, Value = "1000" });
    workflow.Steps.Add(step);
    db.WorkflowDefinitions.Add(workflow);
    await db.SaveChangesAsync();
    var request = new WorkflowRequest { WorkflowDefinitionId = workflow.Id, CurrentStepId = step.Id,
        RequesterId = user.Id, Title = "Test talebi", Description = "Veri modeli doğrulaması", Amount = 1250.50m };
    db.WorkflowRequests.Add(request);
    await db.SaveChangesAsync();
    db.RequestActions.Add(new RequestAction { WorkflowRequestId = request.Id, WorkflowDefinitionId = workflow.Id,
        WorkflowStepId = step.Id, ActorId = user.Id, ActionType = RequestActionType.Submitted });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    var loaded = await db.WorkflowRequests.Include(x => x.Actions).SingleAsync();
    if (loaded.Amount != 1250.50m || loaded.Actions.Count != 1 || loaded.RowVersion.Length == 0)
        throw new Exception("Talep/geçmiş verisi beklenenden farklı.");
    Console.WriteLine("BAŞARILI: Süreç, koşul, talep ve işlem geçmişi kaydı.");

    var otherWorkflow = new WorkflowDefinition { Code = "LEAVE", Name = "İzin Onayı" };
    var otherStep = new WorkflowStep { Name = "Diğer Adım", Order = 1, ApproverRoleId = role.Id };
    otherWorkflow.Steps.Add(otherStep);
    db.Add(otherWorkflow);
    await db.SaveChangesAsync();
    loaded.CurrentStepId = otherStep.Id;
    await ExpectAsync<DbUpdateException>(() => db.SaveChangesAsync(), "Farklı süreç sürümünün adımı reddedildi.");
    db.ChangeTracker.Clear();

    db.WorkflowSteps.Add(new WorkflowStep { WorkflowDefinitionId = workflow.Id, Name = "Tekrarlanan sıra", Order = 1, ApproverRoleId = role.Id });
    await ExpectAsync<DbUpdateException>(() => db.SaveChangesAsync(), "Aynı süreçte tekrarlanan adım sırası reddedildi.");
    db.ChangeTracker.Clear();

    await using var secondScope = provider.CreateAsyncScope();
    var secondDb = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var firstCopy = await db.WorkflowRequests.SingleAsync();
    var secondCopy = await secondDb.WorkflowRequests.SingleAsync();
    firstCopy.Title = "Birinci değişiklik";
    await db.SaveChangesAsync();
    secondCopy.Title = "Eski veri ile değişiklik";
    await ExpectAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync(), "Eşzamanlı talep değişikliği algılandı.");
    await MigrationUpgradeChecks.RunAsync();
    await AuthenticationChecks.RunAsync();
    await WorkflowEngineChecks.RunAsync();
    Console.WriteLine("Tüm entegrasyon kontrolleri başarılı.");
}
finally
{
    await db.Database.EnsureDeletedAsync();
}

static void EnsureIdentity(IdentityResult result)
{
    if (!result.Succeeded) throw new Exception(string.Join("; ", result.Errors.Select(x => x.Description)));
}

static async Task ExpectAsync<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { Console.WriteLine($"BAŞARILI: {message}"); return; }
    throw new Exception($"Beklenen {typeof(T).Name} oluşmadı: {message}");
}

