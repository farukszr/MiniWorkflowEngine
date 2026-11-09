using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Data;
using MiniWorkflowEngine.Domain;
using MiniWorkflowEngine.Domain.Entities;
using MiniWorkflowEngine.Domain.Enums;
using MiniWorkflowEngine.Services.Workflow;
using MiniWorkflowEngine.ViewModels;

namespace MiniWorkflowEngine.Services;

public sealed record WorkflowOperationResult(bool Succeeded, int Id, string? Error)
{
    public static WorkflowOperationResult Success(int id) => new(true, id, null);
    public static WorkflowOperationResult Failure(string error) => new(false, 0, error);
}

public sealed class WorkflowDefinitionService(ApplicationDbContext db, WorkflowConditionEvaluator evaluator)
{
    public async Task<WorkflowPreviewViewModel?> PreviewAsync(int id, bool evaluate, decimal? amount, string? department, CancellationToken ct)
    {
        var workflow = await db.WorkflowDefinitions.AsNoTracking().Include(x => x.Steps).ThenInclude(x => x.Conditions)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (workflow is null) return null;

        var orderedSteps = workflow.Steps.OrderBy(x => x.Order).ToArray();
        var required = WorkflowConditionEvaluator.RequiredInputs(orderedSteps);
        var preview = new WorkflowPreviewViewModel(id, workflow.Name, workflow.Version, required.Amount, required.Department);
        if (!evaluate) return preview;
        if (required.Amount && amount is null) return preview with { Error = "Bu süreçte tutar koşulu var. Örnek tutarı girin." };
        if (required.Department && string.IsNullOrWhiteSpace(department)) return preview with { Error = "Bu süreçte departman koşulu var. Örnek departmanı girin." };

        var roles = await db.Roles.Where(x => ApplicationRoles.All.Contains(x.Name!))
            .ToDictionaryAsync(x => x.Id, x => x.Name!, ct);
        try
        {
            var applicability = evaluator.EvaluateSteps(orderedSteps, roles.Keys, amount, department);
            return preview with { Steps = orderedSteps.Select(x => new WorkflowPreviewStep(x.Order, x.Name,
                ApplicationRoles.DisplayName(roles[x.ApproverRoleId]), applicability[x.Id])).ToArray() };
        }
        catch (InvalidWorkflowDefinitionException ex)
        {
            return preview with { Error = ex.Message };
        }
    }

    public async Task<WorkflowListViewModel> ListAsync(string? search, int page)
    {
        search = search?.Trim();
        if (search?.Length > 100) search = search[..100];
        var query = db.WorkflowDefinitions.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));
        var count = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(count / 20d));
        page = Math.Clamp(page, 1, pages);
        return new WorkflowListViewModel { Search = search, Page = page, TotalPages = pages, TotalCount = count,
            Items = await query.OrderBy(x => x.Code).ThenByDescending(x => x.Version).Skip((page - 1) * 20).Take(20)
                .Select(x => new WorkflowListItem { Id = x.Id, Code = x.Code, Name = x.Name, Version = x.Version,
                    IsActive = x.IsActive, IsPublished = x.PublishedAt != null || x.IsActive || db.WorkflowRequests.Any(r => r.WorkflowDefinitionId == x.Id), StepCount = x.Steps.Count }).ToListAsync() };
    }

    public async Task<List<SelectListItem>> RolesAsync()
        => (await db.Roles.AsNoTracking().Where(x => ApplicationRoles.All.Contains(x.Name!)).OrderBy(x => x.Name).ToListAsync())
            .Select(x => new SelectListItem(ApplicationRoles.DisplayName(x.Name!), x.Id)).ToList();

    public async Task<WorkflowEditorViewModel?> GetAsync(int id)
    {
        var entity = await db.WorkflowDefinitions.AsNoTracking().Include(x => x.Steps).ThenInclude(x => x.Conditions)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id);
        if (entity is null) return null;
        var published = entity.PublishedAt != null || entity.IsActive || await db.WorkflowRequests.AnyAsync(x => x.WorkflowDefinitionId == id);
        return new WorkflowEditorViewModel { Id = entity.Id, Code = entity.Code, Name = entity.Name, Description = entity.Description,
            Version = entity.Version, RowVersion = Convert.ToBase64String(entity.RowVersion), CanEdit = !published,
            IsActive = entity.IsActive, IsPublished = published, AvailableRoles = await RolesAsync(),
            Steps = entity.Steps.OrderBy(x => x.Order).Select(x => new WorkflowStepViewModel { Name = x.Name, ApproverRoleId = x.ApproverRoleId,
                Conditions = x.Conditions.OrderBy(c => c.Id).Select(c => new WorkflowConditionViewModel { Field = c.Field, Operator = c.Operator, Value = c.Value }).ToList() }).ToList() };
    }

    public Task<WorkflowOperationResult> SaveAsync(WorkflowEditorViewModel model, string actorId)
        => ExecuteAsync(actorId, async () =>
        {
            var error = await ValidateAsync(model);
            if (error is not null) return WorkflowOperationResult.Failure(error);
            WorkflowDefinition entity;
            if (model.Id == 0)
            {
                var code = model.Code.Trim().ToUpperInvariant();
                if (await db.WorkflowDefinitions.AnyAsync(x => x.Code == code)) return WorkflowOperationResult.Failure("Bu süreç kodu mevcut. Mevcut tanımdan yeni sürüm oluşturun.");
                entity = new WorkflowDefinition { Code = code };
                db.WorkflowDefinitions.Add(entity);
            }
            else
            {
                var existing = await db.WorkflowDefinitions.Include(x => x.Steps).ThenInclude(x => x.Conditions).SingleOrDefaultAsync(x => x.Id == model.Id);
                if (existing is null) return WorkflowOperationResult.Failure("Süreç bulunamadı.");
                entity = existing;
                if (!Matches(entity, model.RowVersion)) return Stale();
                if (await IsFrozenAsync(entity)) return WorkflowOperationResult.Failure("Yayımlanmış veya talepte kullanılmış süreç değiştirilemez. Yeni sürüm oluşturun.");
                if (!string.Equals(entity.Code, model.Code.Trim(), StringComparison.OrdinalIgnoreCase)) return WorkflowOperationResult.Failure("Süreç kodu değiştirilemez.");
                db.StepConditions.RemoveRange(entity.Steps.SelectMany(x => x.Conditions));
                await db.SaveChangesAsync();
                db.WorkflowSteps.RemoveRange(entity.Steps);
                await db.SaveChangesAsync();
                entity.Steps.Clear();
                // Yalnızca adım/koşul değişse de üst kaydın rowversion değeri yenilenir.
                db.Entry(entity).Property(x => x.Name).IsModified = true;
            }
            entity.Name = model.Name.Trim();
            entity.Description = model.Description?.Trim();
            foreach (var (step, index) in model.Steps.Select((step, index) => (step, index)))
                entity.Steps.Add(ToEntity(step, index + 1));
            await db.SaveChangesAsync();
            return WorkflowOperationResult.Success(entity.Id);
        });

    public Task<WorkflowOperationResult> PublishAsync(WorkflowCommandViewModel model, string actorId)
        => ExecuteAsync(actorId, async () =>
        {
            var entity = await db.WorkflowDefinitions.Include(x => x.Steps).ThenInclude(x => x.Conditions).SingleOrDefaultAsync(x => x.Id == model.Id);
            if (entity is null) return WorkflowOperationResult.Failure("Süreç bulunamadı.");
            if (!Matches(entity, model.RowVersion)) return Stale();
            if (await IsFrozenAsync(entity)) return WorkflowOperationResult.Failure("Yalnızca taslak sürüm yayımlanabilir.");
            var validation = new WorkflowEditorViewModel { Code = entity.Code, Name = entity.Name, Description = entity.Description,
                Steps = entity.Steps.OrderBy(x => x.Order).Select(x => new WorkflowStepViewModel { Name = x.Name, ApproverRoleId = x.ApproverRoleId,
                    Conditions = x.Conditions.Select(c => new WorkflowConditionViewModel { Field = c.Field, Operator = c.Operator, Value = c.Value }).ToList() }).ToList() };
            var error = await ValidateAsync(validation);
            if (error is not null) return WorkflowOperationResult.Failure(error);
            var previous = await db.WorkflowDefinitions.Where(x => x.Code == entity.Code && x.IsActive).ToListAsync();
            foreach (var old in previous) old.IsActive = false;
            await db.SaveChangesAsync();
            entity.PublishedAt = DateTimeOffset.UtcNow;
            entity.IsActive = true;
            db.RecordAdministrationEvent(actorId, "Süreç yayımlandı", $"{entity.Name} · {entity.Code} · v{entity.Version}");
            await db.SaveChangesAsync();
            return WorkflowOperationResult.Success(entity.Id);
        });

    public Task<WorkflowOperationResult> DeactivateAsync(WorkflowCommandViewModel model, string actorId)
        => ExecuteAsync(actorId, async () =>
        {
            var entity = await db.WorkflowDefinitions.SingleOrDefaultAsync(x => x.Id == model.Id);
            if (entity is null) return WorkflowOperationResult.Failure("Süreç bulunamadı.");
            if (!Matches(entity, model.RowVersion)) return Stale();
            if (!entity.IsActive) return WorkflowOperationResult.Failure("Bu sürüm zaten etkin değil.");
            entity.IsActive = false;
            entity.PublishedAt ??= DateTimeOffset.UtcNow;
            db.RecordAdministrationEvent(actorId, "Süreç devre dışı bırakıldı", $"{entity.Name} · {entity.Code} · v{entity.Version}");
            await db.SaveChangesAsync();
            return WorkflowOperationResult.Success(entity.Id);
        });

    public Task<WorkflowOperationResult> NewVersionAsync(WorkflowCommandViewModel model, string actorId)
        => ExecuteAsync(actorId, async () =>
        {
            var source = await db.WorkflowDefinitions.Include(x => x.Steps).ThenInclude(x => x.Conditions).SingleOrDefaultAsync(x => x.Id == model.Id);
            if (source is null) return WorkflowOperationResult.Failure("Süreç bulunamadı.");
            if (!Matches(source, model.RowVersion)) return Stale();
            if (!await IsFrozenAsync(source)) return WorkflowOperationResult.Failure("Taslağı doğrudan düzenleyebilirsiniz.");
            if (await db.WorkflowDefinitions.AnyAsync(x => x.Code == source.Code && x.PublishedAt == null && !x.IsActive))
                return WorkflowOperationResult.Failure("Bu sürecin bir taslağı zaten var. Süreç listesinden mevcut taslağı düzenleyin.");
            var maxVersion = await db.WorkflowDefinitions.Where(x => x.Code == source.Code).MaxAsync(x => x.Version);
            var copy = new WorkflowDefinition { Code = source.Code, Name = source.Name, Description = source.Description, Version = checked(maxVersion + 1) };
            foreach (var step in source.Steps.OrderBy(x => x.Order))
                copy.Steps.Add(new WorkflowStep { Name = step.Name, Order = step.Order, ApproverRoleId = step.ApproverRoleId,
                    Conditions = step.Conditions.Select(c => new StepCondition { Field = c.Field, Operator = c.Operator, Value = c.Value }).ToList() });
            db.WorkflowDefinitions.Add(copy);
            await db.SaveChangesAsync();
            return WorkflowOperationResult.Success(copy.Id);
        });

    private async Task<string?> ValidateAsync(WorkflowEditorViewModel model)
    {
        if (!Regex.IsMatch(model.Code ?? "", @"^[A-Za-z][A-Za-z0-9_]{1,49}$")) return "Geçerli bir süreç kodu girin.";
        if (string.IsNullOrWhiteSpace(model.Name) || model.Name.Trim().Length is < 2 or > 150 || model.Description?.Length > 2000) return "Süreç adı veya açıklaması geçerli değil.";
        if (model.Steps is null || model.Steps.Count is < 1 or > 20) return "1–20 onay adımı tanımlayın.";
        var allowedRoles = (await RolesAsync()).Select(x => x.Value).ToHashSet();
        foreach (var step in model.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name) || step.Name.Trim().Length is < 2 or > 150) return "Her adım için 2–150 karakterlik bir ad girin.";
            if (!allowedRoles.Contains(step.ApproverRoleId)) return "Her adım için geçerli bir onaylayıcı rolü seçin.";
            if (step.Conditions is null || step.Conditions.Count > 10) return "Bir adımda en fazla 10 koşul olabilir.";
            foreach (var condition in step.Conditions)
            {
                if (!Enum.IsDefined(condition.Field) || !Enum.IsDefined(condition.Operator) || string.IsNullOrWhiteSpace(condition.Value)) return "Koşul alanı, karşılaştırma ve değer geçerli olmalıdır.";
                var value = condition.Value.Trim();
                if (condition.Field == ConditionField.Department)
                {
                    if (condition.Operator is not (ConditionOperator.Equals or ConditionOperator.NotEquals)) return "Departman için yalnızca eşittir/eşit değildir kullanılabilir.";
                    if (value.Length > 100) return "Departman koşulu en fazla 100 karakter olabilir.";
                }
                else
                {
                    if (!Regex.IsMatch(value, @"^\d{1,16}([.,]\d{1,2})?$", RegexOptions.CultureInvariant)
                        || !decimal.TryParse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
                        return "Tutar için negatif olmayan, en fazla iki ondalık basamaklı sayı girin. Binlik ayırıcı kullanmayın (örnek: 1000,50).";
                    value = amount.ToString("0.00", CultureInfo.InvariantCulture);
                }
                condition.Value = value;
            }
        }
        return null;
    }

    private static WorkflowStep ToEntity(WorkflowStepViewModel step, int order)
        => new() { Name = step.Name.Trim(), Order = order, ApproverRoleId = step.ApproverRoleId,
            Conditions = step.Conditions.Select(c => new StepCondition { Field = c.Field, Operator = c.Operator, Value = c.Value.Trim() }).ToList() };

    private Task<bool> IsFrozenAsync(WorkflowDefinition entity)
        => entity.PublishedAt != null || entity.IsActive ? Task.FromResult(true) : db.WorkflowRequests.AnyAsync(x => x.WorkflowDefinitionId == entity.Id);

    private static bool Matches(WorkflowDefinition entity, string? stamp)
    {
        if (string.IsNullOrWhiteSpace(stamp)) return false;
        try { return entity.RowVersion.SequenceEqual(Convert.FromBase64String(stamp)); }
        catch (FormatException) { return false; }
    }

    private static WorkflowOperationResult Stale() => WorkflowOperationResult.Failure("Süreç başka bir işlemde güncellendi. Sayfayı yenileyip tekrar deneyin.");

    private async Task<WorkflowOperationResult> ExecuteAsync(string actorId, Func<Task<WorkflowOperationResult>> action)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var authorized = await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id
                                    where ur.UserId == actorId && role.Name == ApplicationRoles.Administrator select ur).AnyAsync();
            if (!authorized) return WorkflowOperationResult.Failure("Bu işlem için yönetici yetkisi gerekiyor.");
            var result = await action();
            if (result.Succeeded) await transaction.CommitAsync();
            return result;
        }
        catch (DbUpdateConcurrencyException) { return Stale(); }
        catch (Exception ex) when (ex is SqlException { Number: 1205 or 1222 } || ex is DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 or 2601 or 2627 } })
        { return WorkflowOperationResult.Failure("Eşzamanlı veya yinelenen işlem algılandı. Listeyi yenileyip tekrar deneyin."); }
    }
}
