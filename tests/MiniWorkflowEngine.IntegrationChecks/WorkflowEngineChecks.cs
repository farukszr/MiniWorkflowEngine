using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.Extensions;
using MiniWorkflowEngine.Services.Interfaces;
using MiniWorkflowEngine.Services.Workflow;

internal static class WorkflowEngineChecks
{
    public static async Task RunAsync()
    {
        CheckConditionOperators();
        var database = $"MiniWorkflowEngine_EngineChecks_{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var setupScope = provider.CreateAsyncScope();
        var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await db.Database.MigrateAsync();
            await setupScope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedRolesAsync();
            var users = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var requester = await CreateUserAsync(users, "owner", ApplicationRoles.Employee, ApplicationRoles.Approver);
            var approver = await CreateUserAsync(users, "approver", ApplicationRoles.Approver);
            var finance = await CreateUserAsync(users, "finance", ApplicationRoles.Administrator);
            var outsider = await CreateUserAsync(users, "outsider", ApplicationRoles.Employee);
            var approvalRole = await db.Roles.Where(x => x.Name == ApplicationRoles.Approver).Select(x => x.Id).SingleAsync();
            var financeRole = await db.Roles.Where(x => x.Name == ApplicationRoles.Administrator).Select(x => x.Id).SingleAsync();
            var definition = Definition("ENGINE", approvalRole, financeRole);
            db.WorkflowDefinitions.Add(definition);
            await db.SaveChangesAsync();
            var engine = setupScope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
            RequestDraftInput Input(decimal? amount = 500m, string? department = "BT")
                => new(definition.Id, "Masraf talebi", "Entegrasyon testi açıklaması", amount, department);

            Require(!(await engine.CreateDraftAsync(Input(), "unknown-user")).Succeeded, "Olmayan kullanıcı talep açamamalı.");
            Require((await engine.CreateDraftAsync(Input(-1), requester.Id)).Error == WorkflowError.Validation, "Negatif tutar reddedilmeli.");
            Require((await engine.CreateDraftAsync(Input(1.001m), requester.Id)).Error == WorkflowError.Validation, "Kuruş hassasiyeti aşılmamalı.");
            var draft = Good(await engine.CreateDraftAsync(Input(), requester.Id));
            Require(draft.Status == RequestStatus.Draft && draft.History.Single().ActionType == RequestActionType.DraftCreated, "Taslak ve geçmişi birlikte oluşmalı.");
            Require((await engine.GetAsync(draft.Id, outsider.Id)).Error == WorkflowError.NotFound, "İlgisiz kullanıcı talebi okuyamamalı.");
            Require((await engine.UpdateDraftAsync(Command(draft), Input(900m), outsider.Id)).Error == WorkflowError.NotFound, "İlgisiz kullanıcı taslağı değiştirememeli.");
            var changed = Good(await engine.UpdateDraftAsync(Command(draft), Input(700m), requester.Id));
            Require(changed.Amount == 700m && changed.RowVersion != draft.RowVersion && changed.History.Count == 2, "Taslak değişikliği sürümü ve geçmişi ilerletmeli.");
            Require((await engine.SubmitAsync(Command(draft), requester.Id)).Error == WorkflowError.Conflict, "Eski talep sürümü reddedilmeli.");
            var pending = Good(await engine.SubmitAsync(Command(changed), requester.Id));
            Require(pending.Status == RequestStatus.Pending && pending.CurrentStepName == "İlk onay", "İlk koşulsuz adım seçilmeli.");
            Require((await engine.ApproveAsync(Command(pending), requester.Id)).Error == WorkflowError.Forbidden, "Talep sahibi kendi talebini onaylayamamalı.");
            var adminOwned = Good(await engine.CreateDraftAsync(Input(), finance.Id));
            adminOwned = Good(await engine.SubmitAsync(Command(adminOwned), finance.Id));
            Require((await engine.ApproveAsync(Command(adminOwned), finance.Id)).Error == WorkflowError.Forbidden &&
                (await engine.RejectAsync(Command(adminOwned, "Ret nedeni."), finance.Id)).Error == WorkflowError.Forbidden,
                "Yönetici kendi talebine karar verememeli.");
            Require((await engine.UpdateDraftAsync(Command(pending), Input(), requester.Id)).Error == WorkflowError.InvalidState, "Gönderilmiş talep değiştirilememeli.");
            Require((await engine.CancelAsync(Command(pending), approver.Id)).Error == WorkflowError.Forbidden, "Onaylayıcı sahibinin yerine iptal edememeli.");
            var completed = Good(await engine.ApproveAsync(Command(pending), approver.Id));
            Require(completed.Status == RequestStatus.Approved && completed.CurrentStepId is null && completed.CompletedAt is not null, "Son adım talebi tamamlamalı.");
            Require(completed.History.Select(x => x.ActionType).SequenceEqual(new[] { RequestActionType.DraftCreated, RequestActionType.DraftUpdated,
                RequestActionType.Submitted, RequestActionType.Approved, RequestActionType.StepSkipped, RequestActionType.StepSkipped, RequestActionType.Completed }),
                "Koşullu atlama ve tamamlanma geçmişi sıralı olmalı.");
            Require((await engine.ApproveAsync(Command(completed), approver.Id)).Error == WorkflowError.InvalidState, "Tamamlanmış talep tekrar onaylanamamalı.");
            Require((await engine.CancelAsync(Command(completed), requester.Id)).Error == WorkflowError.InvalidState, "Tamamlanmış talep iptal edilememeli.");
            Require((await engine.GetAsync(completed.Id, approver.Id)).Succeeded, "Önceki işlemi yapan kullanıcı talebi okuyabilmeli.");
            Console.WriteLine("BAŞARILI: Taslak, sahiplik, rol ayrımı, kendi talebine karar engeli, koşul atlama ve sıralı geçmiş.");

            var multi = Good(await engine.CreateDraftAsync(Input(2500m, " finans "), requester.Id));
            multi = Good(await engine.SubmitAsync(Command(multi), requester.Id));
            multi = Good(await engine.ApproveAsync(Command(multi), finance.Id));
            Require(multi.CurrentStepName == "Finans onayı" && multi.History.Last().ActionType == RequestActionType.AdministratorApproved,
                "Yönetici müdahalesi mevcut adımı tamamlamalı ve sonraki adımı korumalı.");
            Require((await engine.ApproveAsync(Command(multi), approver.Id)).Error == WorkflowError.NotFound,
                "Yönetici kararı sonrasında bir önceki rolün kullanıcıları sıradaki adıma erişememeli.");
            multi = Good(await engine.ApproveAsync(Command(multi), finance.Id));
            Require(multi.CurrentStepName == "Departman kontrolü", "Türkçe departman karşılaştırması üçüncü adımı seçmeli.");
            multi = Good(await engine.ApproveAsync(Command(multi), approver.Id));
            Require(multi.Status == RequestStatus.Approved &&
                multi.History.Count(x => x.ActionType is RequestActionType.Approved or RequestActionType.AdministratorApproved) == 3,
                "Üç sıralı karar tek tek alınmalı.");

            var rejected = Good(await engine.CreateDraftAsync(Input(), requester.Id));
            rejected = Good(await engine.SubmitAsync(Command(rejected), requester.Id));
            Require((await engine.RejectAsync(Command(rejected), approver.Id)).Error == WorkflowError.Validation, "Ret gerekçesi zorunlu olmalı.");
            rejected = Good(await engine.RejectAsync(Command(rejected, "Belge eksik."), approver.Id));
            Require(rejected.Status == RequestStatus.Rejected && rejected.CompletedAt is not null && rejected.CurrentStepId is null,
                "Ret süreci durdurmalı.");
            Require((await engine.SubmitAsync(Command(rejected), requester.Id)).Error == WorkflowError.InvalidState, "Reddedilen talep yeniden gönderilememeli.");
            var cancelled = Good(await engine.CreateDraftAsync(Input(), requester.Id));
            cancelled = Good(await engine.CancelAsync(Command(cancelled, "Vazgeçildi."), requester.Id));
            Require(cancelled.Status == RequestStatus.Cancelled, "Taslak iptal edilebilmeli.");
            var cancelPending = Good(await engine.CreateDraftAsync(Input(), requester.Id));
            cancelPending = Good(await engine.SubmitAsync(Command(cancelPending), requester.Id));
            cancelPending = Good(await engine.CancelAsync(Command(cancelPending), requester.Id));
            Require(cancelPending.Status == RequestStatus.Cancelled && cancelPending.CurrentStepId is null, "Bekleyen talep sahibi tarafından iptal edilebilmeli.");
            Console.WriteLine("BAŞARILI: Tutar/departman ile üç aşamalı onay, gerekçeli ret ve taslak/bekleyen iptali.");

            var missing = Good(await engine.CreateDraftAsync(Input(null, null), requester.Id));
            Require((await engine.SubmitAsync(Command(missing), requester.Id)).Error == WorkflowError.Validation, "Koşul alanları eksikken talep gönderilememeli.");
            var unchanged = Good(await engine.GetAsync(missing.Id, requester.Id));
            Require(unchanged.RowVersion == missing.RowVersion && unchanged.History.Count == 1, "Hatalı gönderim durum/geçmiş bırakmamalı.");
            var staleVersionDraft = Good(await engine.CreateDraftAsync(Input(), requester.Id));
            var inFlight = Good(await engine.CreateDraftAsync(Input(), requester.Id));
            inFlight = Good(await engine.SubmitAsync(Command(inFlight), requester.Id));
            definition.IsActive = false;
            await db.SaveChangesAsync();
            Require((await engine.CreateDraftAsync(Input(), requester.Id)).Error == WorkflowError.InvalidState, "Arşiv sürümle yeni talep açılamamalı.");
            Require((await engine.SubmitAsync(Command(staleVersionDraft), requester.Id)).Error == WorkflowError.InvalidState, "Arşivlenen sürümdeki taslak gönderilememeli.");
            inFlight = Good(await engine.ApproveAsync(Command(inFlight), approver.Id));
            Require(inFlight.Status == RequestStatus.Approved && inFlight.WorkflowDefinitionId == definition.Id, "Arşivlenen sürümdeki gönderilmiş talep tamamlanabilmeli.");
            Console.WriteLine("BAŞARILI: Eksik koşul verisinde atomik ret ve arşivlenen sürümde bekleyen talebin devamı.");

            definition.IsActive = true;
            await db.SaveChangesAsync();
            await CheckRollbackAsync(db, engine, Input(), requester.Id, approver.Id);
            var revoked = Good(await engine.CreateDraftAsync(Input(), requester.Id));
            revoked = Good(await engine.SubmitAsync(Command(revoked), requester.Id));
            if (!(await users.RemoveFromRoleAsync(approver, ApplicationRoles.Approver)).Succeeded) throw new Exception("Rol geri alma testi hazırlanamadı.");
            Require(!(await engine.ApproveAsync(Command(revoked), approver.Id)).Succeeded, "Rolü alınan kullanıcı eski adımda karar verememeli.");
            if (!(await users.AddToRoleAsync(approver, ApplicationRoles.Approver)).Succeeded) throw new Exception("Test rolü geri yüklenemedi.");
            await CheckRacesAsync(engine, Input(), requester.Id, approver.Id);
            await CheckHistoryProtectionAsync(provider, completed.Id);
            await CheckBrokenDefinitionAsync(db, engine, approvalRole, requester.Id);
            Console.WriteLine("Workflow Engine kontrolleri başarılı.");
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private static WorkflowDefinition Definition(string code, string approvalRole, string financeRole)
        => new() { Code = code, Name = "Motor testi", IsActive = true, PublishedAt = DateTimeOffset.UtcNow,
            Steps = new List<WorkflowStep>
            {
                new() { Name = "İlk onay", Order = 1, ApproverRoleId = approvalRole },
                new() { Name = "Finans onayı", Order = 2, ApproverRoleId = financeRole,
                    Conditions = new List<StepCondition> { new() { Field = ConditionField.Amount, Operator = ConditionOperator.GreaterThan, Value = "1000.00" } } },
                new() { Name = "Departman kontrolü", Order = 3, ApproverRoleId = approvalRole,
                    Conditions = new List<StepCondition> { new() { Field = ConditionField.Department, Operator = ConditionOperator.Equals, Value = "FİNANS" } } }
            } };

    private static async Task CheckRacesAsync(IWorkflowEngine engine, RequestDraftInput input, string owner, string approver)
    {
        var draft = Good(await engine.CreateDraftAsync(input, owner));
        var submissions = await Task.WhenAll(engine.SubmitAsync(Command(draft), owner), engine.SubmitAsync(Command(draft), owner));
        Require(submissions.Count(x => x.Succeeded) == 1 && submissions.Count(x => x.Error == WorkflowError.Conflict) == 1, "İki eşzamanlı gönderimden biri başarılı olmalı.");
        var pending = Good(await engine.GetAsync(draft.Id, owner));
        var approvals = await Task.WhenAll(engine.ApproveAsync(Command(pending), approver), engine.ApproveAsync(Command(pending), approver));
        Require(approvals.Count(x => x.Succeeded) == 1 && approvals.Count(x => x.Error == WorkflowError.Conflict) == 1, "İki eşzamanlı onaydan biri başarılı olmalı.");
        var final = Good(await engine.GetAsync(draft.Id, owner));
        Require(final.History.Count(x => x.ActionType == RequestActionType.Approved) == 1 && final.History.Count(x => x.ActionType == RequestActionType.Submitted) == 1,
            "Çift istek mükerrer geçmiş oluşturmamalı.");
        var race = Good(await engine.CreateDraftAsync(input, owner));
        race = Good(await engine.SubmitAsync(Command(race), owner));
        var decisions = await Task.WhenAll(engine.ApproveAsync(Command(race), approver), engine.CancelAsync(Command(race), owner));
        Require(decisions.Count(x => x.Succeeded) == 1, "Onay/iptal yarışında yalnızca biri başarılı olmalı.");
        var ended = Good(await engine.GetAsync(race.Id, owner));
        Require(ended.Status is RequestStatus.Approved or RequestStatus.Cancelled &&
            ended.History.Count(x => x.ActionType is RequestActionType.Approved or RequestActionType.Cancelled) == 1,
            "Karar yarışında tek tutarlı terminal durum kalmalı.");
        Console.WriteLine("BAŞARILI: Gerçek eşzamanlı gönderim, çift onay ve onay/iptal yarışında tek karar/geçmiş.");
    }

    private static async Task CheckHistoryProtectionAsync(ServiceProvider provider, int requestId)
    {
        foreach (var delete in new[] { false, true })
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var action = await db.RequestActions.FirstAsync(x => x.WorkflowRequestId == requestId);
            if (delete) db.RequestActions.Remove(action); else action.Comment = "Geçmişi değiştir";
            try { await db.SaveChangesAsync(); throw new Exception("İşlem geçmişi koruması uygulanmadı."); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("geçmişi")) { }
        }
        Console.WriteLine("BAŞARILI: EF Core üzerinden geçmiş değiştirme ve silme engeli.");
    }

    private static async Task CheckRollbackAsync(ApplicationDbContext db, IWorkflowEngine engine, RequestDraftInput input, string owner, string approver)
    {
        var draft = Good(await engine.CreateDraftAsync(input, owner));
        var pending = Good(await engine.SubmitAsync(Command(draft), owner));
        // Yalnızca izole test veritabanında, geçmiş INSERT'ini kasıtlı olarak başarısız yap.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE RequestActions WITH NOCHECK ADD CONSTRAINT CK_Test_ApprovalFailure CHECK ([ActionType] <> N'Approved');");
        try
        {
            try { await engine.ApproveAsync(Command(pending), approver); throw new Exception("Kasıtlı veritabanı hatası oluşmadı."); }
            catch (DbUpdateException) { }
            var after = Good(await engine.GetAsync(pending.Id, owner));
            Require(after.Status == RequestStatus.Pending && after.CurrentStepId == pending.CurrentStepId && after.RowVersion == pending.RowVersion && after.History.Count == pending.History.Count,
                "Geçmiş INSERT'i başarısız olunca durum, sürüm ve geçmiş birlikte geri alınmalı.");
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE RequestActions DROP CONSTRAINT CK_Test_ApprovalFailure;"); }
        Console.WriteLine("BAŞARILI: Geçmiş kaydı sırasında gerçek SQL hatasında talep durumunun atomik geri alınması.");
    }

    private static async Task CheckBrokenDefinitionAsync(ApplicationDbContext db, IWorkflowEngine engine, string role, string owner)
    {
        var allConditional = new WorkflowDefinition { Code = "ALL_CONDITIONAL", Name = "Tümü koşullu", IsActive = true, PublishedAt = DateTimeOffset.UtcNow,
            Steps = new List<WorkflowStep> { new() { Name = "Koşullu adım", Order = 1, ApproverRoleId = role,
                Conditions = new List<StepCondition> { new() { Field = ConditionField.Amount, Operator = ConditionOperator.GreaterThan, Value = "9999" } } } } };
        db.Add(allConditional);
        await db.SaveChangesAsync();
        var input = new RequestDraftInput(allConditional.Id, "Koşullu talep", "Tümü atlanan adımlar", 5, null);
        var draft = Good(await engine.CreateDraftAsync(input, owner));
        var done = Good(await engine.SubmitAsync(Command(draft), owner));
        Require(done.Status == RequestStatus.Approved && !done.History.Any(x => x.ActionType == RequestActionType.Approved)
            && done.History.Any(x => x.ActionType == RequestActionType.StepSkipped) && done.History.Any(x => x.ActionType == RequestActionType.Completed),
            "Tüm adımlar atlandığında sistem tamamlanması ayrı kaydedilmeli.");

        var broken = Good(await engine.CreateDraftAsync(input, owner));
        allConditional.Steps.Single().Conditions.Single().Value = "geçersiz";
        await db.SaveChangesAsync();
        var invalid = await engine.SubmitAsync(Command(broken), owner);
        Require(invalid.Error == WorkflowError.InvalidDefinition, "Bozuk tanım güvenli biçimde reddedilmeli.");
        var original = Good(await engine.GetAsync(broken.Id, owner));
        Require(original.Status == RequestStatus.Draft && original.History.Count == 1 && original.RowVersion == broken.RowVersion,
            "Bozuk tanımda işlem yarım kalmamalı.");
        Console.WriteLine("BAŞARILI: Tüm koşullar sağlanmadığında otomatik tamamlanma ve bozuk tanımda atomik geri alma.");
    }

    private static void CheckConditionOperators()
    {
        var evaluator = new WorkflowConditionEvaluator();
        foreach (var op in Enum.GetValues<ConditionOperator>())
        foreach (var actual in new[] { 9m, 10m, 11m })
        {
            var step = new WorkflowStep { Conditions = new List<StepCondition> { new() { Field = ConditionField.Amount, Operator = op, Value = "10.00" } } };
            var expected = op switch { ConditionOperator.Equals => actual == 10, ConditionOperator.NotEquals => actual != 10,
                ConditionOperator.GreaterThan => actual > 10, ConditionOperator.GreaterThanOrEqual => actual >= 10,
                ConditionOperator.LessThan => actual < 10, ConditionOperator.LessThanOrEqual => actual <= 10, _ => false };
            Require(evaluator.IsApplicable(step, actual, null) == expected, "Tutar karşılaştırma sınırı hatalı.");
            Require(!evaluator.IsApplicable(step, null, null), "Eksik değer koşulu sağlamamalı.");
        }
        var department = new WorkflowStep { Conditions = new List<StepCondition> { new() { Field = ConditionField.Department, Operator = ConditionOperator.Equals, Value = "FİNANS" } } };
        Require(evaluator.IsApplicable(department, null, " finans "), "Türkçe harf ve boşluk karşılaştırması hatalı.");
        department.Conditions.Add(new StepCondition { Field = ConditionField.Amount, Operator = ConditionOperator.GreaterThan, Value = "10" });
        Require(!evaluator.IsApplicable(department, 5, "finans"), "Koşullar VE ile birleşmeli.");
        Console.WriteLine("BAŞARILI: Altı karşılaştırmanın sınırları, eksik veri, Türkçe departman ve VE koşulları.");
    }

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users, string name, params string[] roles)
    {
        var user = new ApplicationUser { UserName = $"{name}@example.invalid", Email = $"{name}@example.invalid", FullName = name };
        if (!(await users.CreateAsync(user)).Succeeded || !(await users.AddToRolesAsync(user, roles)).Succeeded) throw new Exception("Motor test kullanıcısı oluşturulamadı.");
        return user;
    }
    private static RequestCommand Command(RequestSnapshot request, string? comment = null) => new(request.Id, request.RowVersion, comment);
    private static RequestSnapshot Good(WorkflowResult result)
    {
        if (!result.Succeeded || result.Request is null) throw new Exception($"Motor işlemi başarısız: {result.Error} / {result.Message}");
        return result.Request;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
