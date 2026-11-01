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

-- ============================================================================
-- 2026-10: реестр — «Победитель» удалён; «Срок поставки» (текст) заменён на
--          «Дата доставки» (дата); добавлены поля этапов 6-7:
--          ContractStatusId «Статус контракта», IsContractChecked «Проверен»,
--          IsEisFilled «Заполнить в ЕИС».
-- Скрипт идемпотентен: можно выполнять повторно.
-- ============================================================================

-- 1. Новая колонка «Дата доставки» (реестр + архив).
IF COL_LENGTH('dbo.RegistryItems', 'DeliveryDate') IS NULL
    ALTER TABLE dbo.RegistryItems ADD DeliveryDate DATETIME2(3) NULL;
IF COL_LENGTH('dbo.ArchiveRegistryItems', 'DeliveryDate') IS NULL
    ALTER TABLE dbo.ArchiveRegistryItems ADD DeliveryDate DATETIME2(3) NULL;
GO

-- 2. Перенос текстового «Срок поставки» в дату: всё, что распознаётся как дата.
--    Значения вида «30 дней» / «по заявкам» датой не являются и остаются пустыми.
IF COL_LENGTH('dbo.RegistryItems', 'DeliveryTime') IS NOT NULL
    UPDATE dbo.RegistryItems
    SET DeliveryDate = COALESCE(
            TRY_CONVERT(DATETIME2(3), NULLIF(LTRIM(RTRIM(DeliveryTime)), ''), 104),  -- dd.mm.yyyy
            TRY_CONVERT(DATETIME2(3), NULLIF(LTRIM(RTRIM(DeliveryTime)), ''), 126),  -- yyyy-mm-ddThh:mm:ss
            TRY_CONVERT(DATETIME2(3), NULLIF(LTRIM(RTRIM(DeliveryTime)), ''), 103),  -- dd/mm/yyyy
            TRY_CONVERT(DATETIME2(3), NULLIF(LTRIM(RTRIM(DeliveryTime)), '')))       -- прочее
    WHERE DeliveryDate IS NULL;

IF COL_LENGTH('dbo.ArchiveRegistryItems', 'DeliveryTime') IS NOT NULL
    UPDATE dbo.ArchiveRegistryItems
    SET DeliveryDate = COALESCE(
            TRY_CONVERT(DATETIME2(3), NULLIF(LTRIM(RTRIM(DeliveryTime)), ''), 104),
            TRY_CONVERT(DATETIME2(3), NULLIF(LTRIM(RTRIM(DeliveryTime)), ''), 126),
            TRY_CONVERT(DATETIME2(3), NULLIF(LTRIM(RTRIM(DeliveryTime)), ''), 103),
            TRY_CONVERT(DATETIME2(3), NULLIF(LTRIM(RTRIM(DeliveryTime)), '')))
    WHERE DeliveryDate IS NULL;
GO

-- 3. Удаляем старые колонки Winner и DeliveryTime (сначала их DEFAULT-ограничения, если есть).
DECLARE @tbl SYSNAME, @col SYSNAME, @df NVARCHAR(256), @sql NVARCHAR(MAX);
DECLARE drop_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT t.name, c.name, dc.name
    FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
    WHERE t.name IN ('RegistryItems', 'ArchiveRegistryItems')
      AND c.name IN ('Winner', 'DeliveryTime');
OPEN drop_cur;
FETCH NEXT FROM drop_cur INTO @tbl, @col, @df;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF @df IS NOT NULL
    BEGIN
        SET @sql = N'ALTER TABLE dbo.' + QUOTENAME(@tbl) + N' DROP CONSTRAINT ' + QUOTENAME(@df) + N';';
        EXEC sp_executesql @sql;
    END
    SET @sql = N'ALTER TABLE dbo.' + QUOTENAME(@tbl) + N' DROP COLUMN ' + QUOTENAME(@col) + N';';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM drop_cur INTO @tbl, @col, @df;
END
CLOSE drop_cur; DEALLOCATE drop_cur;
GO

-- 4. Поля этапов 6-7 (реестр).
IF COL_LENGTH('dbo.RegistryItems', 'ContractStatusId') IS NULL
    ALTER TABLE dbo.RegistryItems ADD ContractStatusId INT NULL;      -- 1 Снабдрайв / 2 заказчик / 3 подписан
IF COL_LENGTH('dbo.RegistryItems', 'IsContractChecked') IS NULL
    ALTER TABLE dbo.RegistryItems ADD IsContractChecked BIT NOT NULL CONSTRAINT DF_RegistryItems_IsContractChecked DEFAULT(0);
IF COL_LENGTH('dbo.RegistryItems', 'IsEisFilled') IS NULL
    ALTER TABLE dbo.RegistryItems ADD IsEisFilled BIT NOT NULL CONSTRAINT DF_RegistryItems_IsEisFilled DEFAULT(0);
GO

-- 5. Чистим права на удалённые колонки, чтобы не мешали сохранению настроек.
DELETE FROM dbo.UserColumnPermissions WHERE ColumnKey IN ('Winner', 'DeliveryTime');
GO

-- ============================================================================
-- 2026-11: право «Менять стадию закупок» — по образцу доступа к Контур.
--   Флаг в карточке пользователя (/users) + клейм снабdrive.stage.
--   Администратор и директор имеют право всегда, без флага.
--   Право даёт: ручной перенос закупки на любую стадию во вкладке «Задачи»
--   и редактирование колонки «Стадия» прямо в реестре.
-- ============================================================================
IF COL_LENGTH('dbo.AspNetUsers', 'StageChangeEnabled') IS NULL
BEGIN
    ALTER TABLE dbo.AspNetUsers ADD StageChangeEnabled BIT NOT NULL
        CONSTRAINT DF_AspNetUsers_StageChangeEnabled DEFAULT(0);
    PRINT 'AspNetUsers.StageChangeEnabled добавлена.';
END
ELSE
    PRINT 'AspNetUsers.StageChangeEnabled уже существует.';
GO

-- ============================================================================
-- 2026-11: учёт работы по этапам — кто и когда начал/завершил этап.
--   Заполняется автоматически при любом переводе закупки между стадиями
--   (кнопки этапов, отметки «Согласован»/«Оплачен», ручной перенос, оповещение
--   филиала, «Проблемы с приемкой»). Нужны для контроля работы каждого сотрудника.
-- ============================================================================
IF COL_LENGTH('dbo.RegistryItems', 'StageStartedAtUtc') IS NULL
    ALTER TABLE dbo.RegistryItems ADD StageStartedAtUtc DATETIME2(3) NULL;
IF COL_LENGTH('dbo.RegistryItems', 'StageStartedBy') IS NULL
    ALTER TABLE dbo.RegistryItems ADD StageStartedBy NVARCHAR(256) NULL;
IF COL_LENGTH('dbo.RegistryItems', 'StageCompletedAtUtc') IS NULL
    ALTER TABLE dbo.RegistryItems ADD StageCompletedAtUtc DATETIME2(3) NULL;
IF COL_LENGTH('dbo.RegistryItems', 'StageCompletedBy') IS NULL
    ALTER TABLE dbo.RegistryItems ADD StageCompletedBy NVARCHAR(256) NULL;
GO

-- =============================================================================
-- Окончание приема заявок (дедлайн подачи заявки). Колонка приходит из выгрузки
-- Контур отдельным столбцом «Окончание приема заявок», показывается в обоих
-- Контур-таблицах, в реестре и на этапах 1 / 1.2 / 2 / 3 вкладки «Задачи».
-- =============================================================================
IF COL_LENGTH('dbo.RegistryItems', 'ApplicationEndDate') IS NULL
    ALTER TABLE dbo.RegistryItems ADD ApplicationEndDate DATETIME2(3) NULL;
IF COL_LENGTH('dbo.ArchiveRegistryItems', 'ApplicationEndDate') IS NULL
    ALTER TABLE dbo.ArchiveRegistryItems ADD ApplicationEndDate DATETIME2(3) NULL;
IF COL_LENGTH('dbo.KonturFavorites', 'ApplicationEndDate') IS NULL
    ALTER TABLE dbo.KonturFavorites ADD ApplicationEndDate DATETIME2(3) NULL;
IF COL_LENGTH('dbo.KonturTemplates', 'ApplicationEndDate') IS NULL
    ALTER TABLE dbo.KonturTemplates ADD ApplicationEndDate DATETIME2(3) NULL;
GO

-- Сортировка реестра и отбор по дедлайну идут по этой колонке.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RegistryItems_ApplicationEndDate' AND object_id = OBJECT_ID('dbo.RegistryItems'))
    CREATE NONCLUSTERED INDEX IX_RegistryItems_ApplicationEndDate ON dbo.RegistryItems(ApplicationEndDate);
GO
