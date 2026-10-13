USE [SnabDriveDB]
GO

-- ============================================================================
-- Воркфлоу закупок по стадиям (веб-приложение)
-- Стадии: 1 = поиск/подготовка, 2 = участие в закупке, 3 = оформление документов,
--         4 = завершено; IsLost = 1 — закупка проиграна (задача прервана).
-- ============================================================================

IF COL_LENGTH('Regedit', 'Stage') IS NULL
BEGIN
    ALTER TABLE [Regedit] ADD [Stage] int NOT NULL CONSTRAINT [DF_Regedit_Stage] DEFAULT(1);
    PRINT 'Добавлен Regedit.Stage.';
END
ELSE
    PRINT 'Regedit.Stage уже существует.';
GO

IF COL_LENGTH('Regedit', 'IsLost') IS NULL
BEGIN
    ALTER TABLE [Regedit] ADD [IsLost] bit NOT NULL CONSTRAINT [DF_Regedit_IsLost] DEFAULT(0);
    PRINT 'Добавлен Regedit.IsLost.';
END
ELSE
    PRINT 'Regedit.IsLost уже существует.';
GO

IF COL_LENGTH('Regedit', 'AssignedTo') IS NULL
BEGIN
    ALTER TABLE [Regedit] ADD [AssignedTo] nvarchar(256) NULL;
    PRINT 'Добавлен Regedit.AssignedTo.';
END
ELSE
    PRINT 'Regedit.AssignedTo уже существует.';
GO

-- ----------------------------------------------------------------------------
-- Журнал передач задачи между стадиями
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.RegistryWorkflowLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RegistryWorkflowLog
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

    CREATE INDEX IX_RegistryWorkflowLog_RegeditId ON dbo.RegistryWorkflowLog (RegeditId);
    PRINT 'RegistryWorkflowLog создана.';
END
ELSE
    PRINT 'RegistryWorkflowLog уже существует.';
GO

IF COL_LENGTH('Regedit', 'Place') IS NULL
BEGIN
    ALTER TABLE [Regedit] ADD [Place] nvarchar(1000) NULL;
    PRINT 'Добавлен Regedit.Place.';
END
ELSE
    PRINT 'Regedit.Place уже существует.';
GO

IF COL_LENGTH('Regedit', 'Attachments') IS NULL
BEGIN
    ALTER TABLE [Regedit] ADD [Attachments] nvarchar(max) NULL;
    PRINT 'Добавлен Regedit.Attachments.';
END
ELSE
    PRINT 'Regedit.Attachments уже существует.';
GO
