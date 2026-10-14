USE [snabdrive_db]
GO

-- ============================================================================
-- Воркфлоу закупок по стадиям (веб-приложение)
-- Стадии: 1 = поиск/подготовка, 2 = участие в закупке, 3 = оформление документов,
--         4 = завершено; IsLost = 1 — закупка проиграна (задача прервана).
-- ============================================================================

IF COL_LENGTH('RegistryItems', 'Stage') IS NULL
BEGIN
    ALTER TABLE [RegistryItems] ADD [Stage] int NOT NULL CONSTRAINT [DF_Regedit_Stage] DEFAULT(1);
    PRINT 'Добавлен RegistryItems.Stage.';
END
ELSE
    PRINT 'RegistryItems.Stage уже существует.';
GO

IF COL_LENGTH('RegistryItems', 'IsLost') IS NULL
BEGIN
    ALTER TABLE [RegistryItems] ADD [IsLost] bit NOT NULL CONSTRAINT [DF_Regedit_IsLost] DEFAULT(0);
    PRINT 'Добавлен RegistryItems.IsLost.';
END
ELSE
    PRINT 'RegistryItems.IsLost уже существует.';
GO

IF COL_LENGTH('RegistryItems', 'AssignedTo') IS NULL
BEGIN
    ALTER TABLE [RegistryItems] ADD [AssignedTo] nvarchar(256) NULL;
    PRINT 'Добавлен RegistryItems.AssignedTo.';
END
ELSE
    PRINT 'RegistryItems.AssignedTo уже существует.';
GO

-- ----------------------------------------------------------------------------
-- Журнал передач задачи между стадиями
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.WorkflowLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkflowLogs
    (
        Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RegistryWorkflowLog PRIMARY KEY,
        RegeditId int            NOT NULL,
        FromStage int            NOT NULL,
        ToStage   int            NOT NULL,
        [Action]  nvarchar(50)   NOT NULL,
        [Comment] nvarchar(max)  NULL,
        AtUtc     datetime2(7)   NOT NULL CONSTRAINT DF_RegistryWorkflowLog_AtUtc DEFAULT(SYSUTCDATETIME()),
        ByUser    nvarchar(256)  NULL
    );

    CREATE INDEX IX_RegistryWorkflowLog_RegeditId ON dbo.WorkflowLogs (RegeditId);
    PRINT 'WorkflowLogs создана.';
END
ELSE
    PRINT 'WorkflowLogs уже существует.';
GO

IF COL_LENGTH('RegistryItems', 'Place') IS NULL
BEGIN
    ALTER TABLE [RegistryItems] ADD [Place] nvarchar(1000) NULL;
    PRINT 'Добавлен RegistryItems.Place.';
END
ELSE
    PRINT 'RegistryItems.Place уже существует.';
GO

IF COL_LENGTH('RegistryItems', 'Attachments') IS NULL
BEGIN
    ALTER TABLE [RegistryItems] ADD [Attachments] nvarchar(max) NULL;
    PRINT 'Добавлен RegistryItems.Attachments.';
END
ELSE
    PRINT 'RegistryItems.Attachments уже существует.';
GO
