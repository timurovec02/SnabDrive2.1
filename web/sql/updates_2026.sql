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
