BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108201004_AddWorkflowPublication'
)
BEGIN
    ALTER TABLE [WorkflowDefinitions] ADD [PublishedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108201004_AddWorkflowPublication'
)
BEGIN
    UPDATE w SET PublishedAt = w.CreatedAt
    FROM WorkflowDefinitions AS w
    WHERE w.IsActive = 1 OR EXISTS (
        SELECT 1 FROM WorkflowRequests AS r WHERE r.WorkflowDefinitionId = w.Id
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108201004_AddWorkflowPublication'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108201004_AddWorkflowPublication', N'10.0.14');
END;

COMMIT;
GO

