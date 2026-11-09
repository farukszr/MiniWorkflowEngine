using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniWorkflowEngine.Domain.Entities;

namespace MiniWorkflowEngine.Data.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
        => b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
}

public sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> b)
    {
        b.ToTable("WorkflowDefinitions", t => t.HasCheckConstraint("CK_WorkflowDefinition_Version", "[Version] > 0"));
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.Code, x.Version }).IsUnique();
        b.HasIndex(x => x.Code).IsUnique().HasFilter("[IsActive] = 1");
    }
}

public sealed class WorkflowStepConfiguration : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> b)
    {
        b.ToTable("WorkflowSteps", t => t.HasCheckConstraint("CK_WorkflowStep_Order", "[Order] > 0"));
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.HasIndex(x => new { x.WorkflowDefinitionId, x.Order }).IsUnique();
        b.HasAlternateKey(x => new { x.Id, x.WorkflowDefinitionId });
        b.HasOne(x => x.WorkflowDefinition).WithMany(x => x.Steps)
            .HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ApproverRole).WithMany().HasForeignKey(x => x.ApproverRoleId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class StepConditionConfiguration : IEntityTypeConfiguration<StepCondition>
{
    public void Configure(EntityTypeBuilder<StepCondition> b)
    {
        b.Property(x => x.Field).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Operator).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Value).HasMaxLength(250).IsRequired();
        b.HasOne(x => x.WorkflowStep).WithMany(x => x.Conditions)
            .HasForeignKey(x => x.WorkflowStepId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class WorkflowRequestConfiguration : IEntityTypeConfiguration<WorkflowRequest>
{
    public void Configure(EntityTypeBuilder<WorkflowRequest> b)
    {
        b.ToTable("WorkflowRequests", t =>
        {
            t.HasCheckConstraint("CK_WorkflowRequest_Amount", "[Amount] IS NULL OR [Amount] >= 0");
            t.HasCheckConstraint("CK_WorkflowRequest_CompletedAt", "[CompletedAt] IS NULL OR [CompletedAt] >= [CreatedAt]");
        });
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        b.Property(x => x.Department).HasMaxLength(100);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasAlternateKey(x => new { x.Id, x.WorkflowDefinitionId });
        b.HasIndex(x => new { x.RequesterId, x.CreatedAt });
        b.HasIndex(x => new { x.Status, x.CurrentStepId });
        b.HasOne(x => x.WorkflowDefinition).WithMany().HasForeignKey(x => x.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId)
            .OnDelete(DeleteBehavior.NoAction);
        // Geçerli adımın talebin bağlı olduğu süreç sürümüne ait olması zorunludur.
        b.HasOne(x => x.CurrentStep).WithMany()
            .HasForeignKey(x => new { x.CurrentStepId, x.WorkflowDefinitionId })
            .HasPrincipalKey(x => new { x.Id, x.WorkflowDefinitionId }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RequestActionConfiguration : IEntityTypeConfiguration<RequestAction>
{
    public void Configure(EntityTypeBuilder<RequestAction> b)
    {
        b.Property(x => x.ActionType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.HasIndex(x => new { x.WorkflowRequestId, x.CreatedAt });
        b.HasOne(x => x.WorkflowRequest).WithMany(x => x.Actions)
            .HasForeignKey(x => new { x.WorkflowRequestId, x.WorkflowDefinitionId })
            .HasPrincipalKey(x => new { x.Id, x.WorkflowDefinitionId }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.WorkflowStep).WithMany()
            .HasForeignKey(x => new { x.WorkflowStepId, x.WorkflowDefinitionId })
            .HasPrincipalKey(x => new { x.Id, x.WorkflowDefinitionId }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
    }
}
