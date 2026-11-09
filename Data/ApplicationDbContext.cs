using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MiniWorkflowEngine.Domain.Entities;

namespace MiniWorkflowEngine.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<StepCondition> StepConditions => Set<StepCondition>();
    public DbSet<WorkflowRequest> WorkflowRequests => Set<WorkflowRequest>();
    public DbSet<RequestAction> RequestActions => Set<RequestAction>();
    public DbSet<AdministrationEvent> AdministrationEvents => Set<AdministrationEvent>();

    public void RecordAdministrationEvent(string actorId, string operation, string target, string? detail = null)
        => AdministrationEvents.Add(new AdministrationEvent { ActorId = actorId, Operation = operation, Target = target, Detail = detail });

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProtectHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ProtectHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ProtectHistory()
    {
        if (ChangeTracker.Entries<RequestAction>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Talep işlem geçmişi değiştirilemez veya silinemez; yalnızca yeni kayıt eklenebilir.");
        if (ChangeTracker.Entries<AdministrationEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Yönetim geçmişine yalnızca yeni kayıt eklenebilir.");
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        builder.Entity<AdministrationEvent>().HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
