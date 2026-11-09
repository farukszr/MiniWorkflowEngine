IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [FullName] nvarchar(150) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [WorkflowDefinitions] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [Description] nvarchar(2000) NULL,
    [Version] int NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_WorkflowDefinitions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_WorkflowDefinition_Version] CHECK ([Version] > 0)
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [WorkflowSteps] (
    [Id] int NOT NULL IDENTITY,
    [WorkflowDefinitionId] int NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [Order] int NOT NULL,
    [ApproverRoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_WorkflowSteps] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_WorkflowSteps_Id_WorkflowDefinitionId] UNIQUE ([Id], [WorkflowDefinitionId]),
    CONSTRAINT [CK_WorkflowStep_Order] CHECK ([Order] > 0),
    CONSTRAINT [FK_WorkflowSteps_AspNetRoles_ApproverRoleId] FOREIGN KEY ([ApproverRoleId]) REFERENCES [AspNetRoles] ([Id]),
    CONSTRAINT [FK_WorkflowSteps_WorkflowDefinitions_WorkflowDefinitionId] FOREIGN KEY ([WorkflowDefinitionId]) REFERENCES [WorkflowDefinitions] ([Id])
);

CREATE TABLE [StepConditions] (
    [Id] int NOT NULL IDENTITY,
    [WorkflowStepId] int NOT NULL,
    [Field] nvarchar(30) NOT NULL,
    [Operator] nvarchar(30) NOT NULL,
    [Value] nvarchar(250) NOT NULL,
    CONSTRAINT [PK_StepConditions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_StepConditions_WorkflowSteps_WorkflowStepId] FOREIGN KEY ([WorkflowStepId]) REFERENCES [WorkflowSteps] ([Id])
);

CREATE TABLE [WorkflowRequests] (
    [Id] int NOT NULL IDENTITY,
    [WorkflowDefinitionId] int NOT NULL,
    [RequesterId] nvarchar(450) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [Description] nvarchar(4000) NOT NULL,
    [Amount] decimal(18,2) NULL,
    [Department] nvarchar(100) NULL,
    [Status] nvarchar(30) NOT NULL,
    [CurrentStepId] int NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CompletedAt] datetimeoffset NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_WorkflowRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_WorkflowRequests_Id_WorkflowDefinitionId] UNIQUE ([Id], [WorkflowDefinitionId]),
    CONSTRAINT [CK_WorkflowRequest_Amount] CHECK ([Amount] IS NULL OR [Amount] >= 0),
    CONSTRAINT [CK_WorkflowRequest_CompletedAt] CHECK ([CompletedAt] IS NULL OR [CompletedAt] >= [CreatedAt]),
    CONSTRAINT [FK_WorkflowRequests_AspNetUsers_RequesterId] FOREIGN KEY ([RequesterId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_WorkflowRequests_WorkflowDefinitions_WorkflowDefinitionId] FOREIGN KEY ([WorkflowDefinitionId]) REFERENCES [WorkflowDefinitions] ([Id]),
    CONSTRAINT [FK_WorkflowRequests_WorkflowSteps_CurrentStepId_WorkflowDefinitionId] FOREIGN KEY ([CurrentStepId], [WorkflowDefinitionId]) REFERENCES [WorkflowSteps] ([Id], [WorkflowDefinitionId])
);

CREATE TABLE [RequestActions] (
    [Id] bigint NOT NULL IDENTITY,
    [WorkflowRequestId] int NOT NULL,
    [WorkflowDefinitionId] int NOT NULL,
    [WorkflowStepId] int NULL,
    [ActorId] nvarchar(450) NOT NULL,
    [ActionType] nvarchar(30) NOT NULL,
    [Comment] nvarchar(2000) NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_RequestActions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RequestActions_AspNetUsers_ActorId] FOREIGN KEY ([ActorId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_RequestActions_WorkflowRequests_WorkflowRequestId_WorkflowDefinitionId] FOREIGN KEY ([WorkflowRequestId], [WorkflowDefinitionId]) REFERENCES [WorkflowRequests] ([Id], [WorkflowDefinitionId]),
    CONSTRAINT [FK_RequestActions_WorkflowSteps_WorkflowStepId_WorkflowDefinitionId] FOREIGN KEY ([WorkflowStepId], [WorkflowDefinitionId]) REFERENCES [WorkflowSteps] ([Id], [WorkflowDefinitionId])
);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

CREATE INDEX [IX_RequestActions_ActorId] ON [RequestActions] ([ActorId]);

CREATE INDEX [IX_RequestActions_WorkflowRequestId_CreatedAt] ON [RequestActions] ([WorkflowRequestId], [CreatedAt]);

CREATE INDEX [IX_RequestActions_WorkflowRequestId_WorkflowDefinitionId] ON [RequestActions] ([WorkflowRequestId], [WorkflowDefinitionId]);

CREATE INDEX [IX_RequestActions_WorkflowStepId_WorkflowDefinitionId] ON [RequestActions] ([WorkflowStepId], [WorkflowDefinitionId]);

CREATE INDEX [IX_StepConditions_WorkflowStepId] ON [StepConditions] ([WorkflowStepId]);

CREATE UNIQUE INDEX [IX_WorkflowDefinitions_Code] ON [WorkflowDefinitions] ([Code]) WHERE [IsActive] = 1;

CREATE UNIQUE INDEX [IX_WorkflowDefinitions_Code_Version] ON [WorkflowDefinitions] ([Code], [Version]);

CREATE INDEX [IX_WorkflowRequests_CurrentStepId_WorkflowDefinitionId] ON [WorkflowRequests] ([CurrentStepId], [WorkflowDefinitionId]);

CREATE INDEX [IX_WorkflowRequests_RequesterId_CreatedAt] ON [WorkflowRequests] ([RequesterId], [CreatedAt]);

CREATE INDEX [IX_WorkflowRequests_Status_CurrentStepId] ON [WorkflowRequests] ([Status], [CurrentStepId]);

CREATE INDEX [IX_WorkflowRequests_WorkflowDefinitionId] ON [WorkflowRequests] ([WorkflowDefinitionId]);

CREATE INDEX [IX_WorkflowSteps_ApproverRoleId] ON [WorkflowSteps] ([ApproverRoleId]);

CREATE UNIQUE INDEX [IX_WorkflowSteps_WorkflowDefinitionId_Order] ON [WorkflowSteps] ([WorkflowDefinitionId], [Order]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108182632_InitialCreate', N'10.0.14');

COMMIT;
GO

