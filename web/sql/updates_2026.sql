-- =============================================================
-- SnabDrive 2.1 — инкрементальные обновления схемы (2026)
-- Выполнить на существующей БД [snabdrive_db] перед публикацией.
-- Все операции идемпотентны (IF NOT EXISTS).
-- =============================================================
USE [snabdrive_db];
GO

-- -------------------------------------------------------------
-- 1) Вложения закупок в БД (раньше хранились в wwwroot/uploads на IIS).
--    Содержимое — varbinary(max); отдаётся через GET /attachments/{id}/{name}.
-- -------------------------------------------------------------
IF OBJECT_ID(N'[Attachments]', N'U') IS NULL
BEGIN
    CREATE TABLE [Attachments] (
        [Id]            INT            IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Attachments] PRIMARY KEY,
        [RegeditId]     INT            NOT NULL,
        [FileName]      NVARCHAR(500)  NOT NULL,
        [ContentType]   NVARCHAR(200)  NULL,
        [SizeBytes]     BIGINT         NOT NULL CONSTRAINT [DF_Attachments_Size] DEFAULT(0),
        [Content]       VARBINARY(MAX) NOT NULL,
        [UploadedAtUtc] DATETIME2      NOT NULL CONSTRAINT [DF_Attachments_UploadedAt] DEFAULT(SYSUTCDATETIME()),
        [UploadedBy]    NVARCHAR(256)  NULL
    );
    CREATE INDEX [IX_Attachments_RegeditId] ON [Attachments]([RegeditId]);
    PRINT 'Таблица [Attachments] создана.';
END
ELSE
    PRINT 'Таблица [Attachments] уже существует — пропуск.';
GO

-- -------------------------------------------------------------
-- 2) Факты отправленных уведомлений о торгах (за 1 день и в день торгов),
--    чтобы не отправлять повторно. Уникальность по (RegeditId, Kind).
-- -------------------------------------------------------------
IF OBJECT_ID(N'[BiddingNotifications]', N'U') IS NULL
BEGIN
    CREATE TABLE [BiddingNotifications] (
        [Id]              INT           IDENTITY(1,1) NOT NULL CONSTRAINT [PK_BiddingNotifications] PRIMARY KEY,
        [RegeditId]       INT           NOT NULL,
        [Kind]            NVARCHAR(20)  NOT NULL, -- 'DayBefore' | 'DayOf'
        [NotifiedAtUtc]   DATETIME2     NOT NULL CONSTRAINT [DF_BiddingNotif_At] DEFAULT(SYSUTCDATETIME()),
        [RecipientsCount] INT           NOT NULL CONSTRAINT [DF_BiddingNotif_Recip] DEFAULT(0)
    );
    CREATE UNIQUE INDEX [UX_BiddingNotifications_Regedit_Kind] ON [BiddingNotifications]([RegeditId], [Kind]);
    PRINT 'Таблица [BiddingNotifications] создана.';
END
ELSE
    PRINT 'Таблица [BiddingNotifications] уже существует — пропуск.';
GO

-- -------------------------------------------------------------
-- 3) Ссылка на закупку в ЕИС — отдельное поле (раньше ссылка и наименование
--    были совмещены в NameLink). NVARCHAR(1000), NULL.
-- -------------------------------------------------------------
IF COL_LENGTH('RegistryItems', 'EisLink') IS NULL
BEGIN
    ALTER TABLE [RegistryItems] ADD [EisLink] NVARCHAR(1000) NULL;
    PRINT 'Колонка [RegistryItems].[EisLink] добавлена.';
END
ELSE
    PRINT 'Колонка [RegistryItems].[EisLink] уже существует — пропуск.';
GO

IF COL_LENGTH('ArchiveRegistryItems', 'EisLink') IS NULL
BEGIN
    ALTER TABLE [ArchiveRegistryItems] ADD [EisLink] NVARCHAR(1000) NULL;
    PRINT 'Колонка [ArchiveRegistryItems].[EisLink] добавлена.';
END
ELSE
    PRINT 'Колонка [ArchiveRegistryItems].[EisLink] уже существует — пропуск.';
GO

-- ============================================================================
-- 2026-10: поля «Согласован» (IsApproved) и «Звони» (IsCall).
-- ============================================================================
IF COL_LENGTH('dbo.RegistryItems', 'IsApproved') IS NULL
    ALTER TABLE dbo.RegistryItems ADD IsApproved BIT NOT NULL
        CONSTRAINT DF_RegistryItems_IsApproved DEFAULT(0);
IF COL_LENGTH('dbo.RegistryItems', 'IsCall') IS NULL
    ALTER TABLE dbo.RegistryItems ADD IsCall BIT NOT NULL
        CONSTRAINT DF_RegistryItems_IsCall DEFAULT(0);
GO

-- ============================================================================
-- 2026-10: «Номер закупки» (PurchaseNumber) в реестре и архиве.
-- ============================================================================
IF COL_LENGTH('dbo.RegistryItems', 'PurchaseNumber') IS NULL
    ALTER TABLE dbo.RegistryItems ADD PurchaseNumber NVARCHAR(200) NOT NULL
        CONSTRAINT DF_RegistryItems_PurchaseNumber DEFAULT('');
IF COL_LENGTH('dbo.ArchiveRegistryItems', 'PurchaseNumber') IS NULL
    ALTER TABLE dbo.ArchiveRegistryItems ADD PurchaseNumber NVARCHAR(200) NOT NULL
        CONSTRAINT DF_ArchiveRegistryItems_PurchaseNumber DEFAULT('');
GO

-- ============================================================================
-- 2026-10: новые поля воркфлоу в реестре.
--   IsByRequest «По заявкам» (этап 1), ExpectedDeliveryDate «Ожидаемая дата
--   доставки» (этапы 5/7), BranchId «Филиал» (этап 7), BranchFilePath «Файл
--   оповещения филиала» (этап 7), ReturnReason «Причина возврата» (этап 9).
-- ============================================================================
IF COL_LENGTH('dbo.RegistryItems', 'IsByRequest') IS NULL
    ALTER TABLE dbo.RegistryItems ADD IsByRequest BIT NOT NULL CONSTRAINT DF_RegistryItems_IsByRequest DEFAULT(0);
IF COL_LENGTH('dbo.RegistryItems', 'ExpectedDeliveryDate') IS NULL
    ALTER TABLE dbo.RegistryItems ADD ExpectedDeliveryDate DATETIME2(3) NULL;
IF COL_LENGTH('dbo.RegistryItems', 'BranchId') IS NULL
    ALTER TABLE dbo.RegistryItems ADD BranchId INT NULL;
IF COL_LENGTH('dbo.RegistryItems', 'BranchFilePath') IS NULL
    ALTER TABLE dbo.RegistryItems ADD BranchFilePath NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.RegistryItems', 'ReturnReason') IS NULL
    ALTER TABLE dbo.RegistryItems ADD ReturnReason NVARCHAR(2000) NULL;
GO

-- ============================================================================
-- 2026-10: справочник «Филиалы» (для оповещения на этапе 7).
-- ============================================================================
IF OBJECT_ID('dbo.Branches', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Branches
    (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY,
        Name        NVARCHAR(300)  NOT NULL,
        BitrixUserId NVARCHAR(100) NULL,
        IsActive    BIT NOT NULL CONSTRAINT DF_Branches_IsActive DEFAULT(1)
    );
    -- Примеры филиалов (замените на реальные):
    INSERT INTO dbo.Branches (Name) VALUES (N'Филиал 1'), (N'Филиал 2'), (N'Филиал 3');
END
GO
