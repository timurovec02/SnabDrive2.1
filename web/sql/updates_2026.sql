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
