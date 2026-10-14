/* =============================================================================
   ОПТИМИЗАЦИЯ: индексы для ускорения загрузки и обмена данными
   -----------------------------------------------------------------------------
   Идемпотентно: каждый индекс создаётся только если его ещё нет — скрипт можно
   запускать повторно без ошибок. Выполнить ОДИН раз на БД [snabdrive_db]
   (через SSMS или sqlcmd). Индексы ускоряют фильтры/сортировки/поиск и JOIN'ы
   по справочникам; накладные расходы на запись минимальны.
   ============================================================================= */
USE [snabdrive_db];
GO
SET NOCOUNT ON;
GO

/* ---------- RegistryItems (основной реестр) ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RegistryItems_IsFinished'        AND object_id=OBJECT_ID('dbo.RegistryItems'))
    CREATE NONCLUSTERED INDEX IX_RegistryItems_IsFinished        ON dbo.RegistryItems(IsFinished);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RegistryItems_TypeOfPurchaseId'  AND object_id=OBJECT_ID('dbo.RegistryItems'))
    CREATE NONCLUSTERED INDEX IX_RegistryItems_TypeOfPurchaseId  ON dbo.RegistryItems(TypeOfPurchaseId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RegistryItems_B2BStatusId'       AND object_id=OBJECT_ID('dbo.RegistryItems'))
    CREATE NONCLUSTERED INDEX IX_RegistryItems_B2BStatusId       ON dbo.RegistryItems(B2BStatusId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RegistryItems_ExecutionStatusId' AND object_id=OBJECT_ID('dbo.RegistryItems'))
    CREATE NONCLUSTERED INDEX IX_RegistryItems_ExecutionStatusId ON dbo.RegistryItems(ExecutionStatusId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RegistryItems_BiddingDate'       AND object_id=OBJECT_ID('dbo.RegistryItems'))
    CREATE NONCLUSTERED INDEX IX_RegistryItems_BiddingDate       ON dbo.RegistryItems(BiddingDate);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RegistryItems_DateOfPlacement'   AND object_id=OBJECT_ID('dbo.RegistryItems'))
    CREATE NONCLUSTERED INDEX IX_RegistryItems_DateOfPlacement   ON dbo.RegistryItems(DateOfPlacement);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RegistryItems_NameLink'          AND object_id=OBJECT_ID('dbo.RegistryItems'))
    CREATE NONCLUSTERED INDEX IX_RegistryItems_NameLink          ON dbo.RegistryItems(NameLink);

/* ---------- ArchiveRegistryItems (архив) ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ArchiveRegistryItems_TypeOfPurchaseId'  AND object_id=OBJECT_ID('dbo.ArchiveRegistryItems'))
    CREATE NONCLUSTERED INDEX IX_ArchiveRegistryItems_TypeOfPurchaseId  ON dbo.ArchiveRegistryItems(TypeOfPurchaseId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ArchiveRegistryItems_B2BStatusId'       AND object_id=OBJECT_ID('dbo.ArchiveRegistryItems'))
    CREATE NONCLUSTERED INDEX IX_ArchiveRegistryItems_B2BStatusId       ON dbo.ArchiveRegistryItems(B2BStatusId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ArchiveRegistryItems_ExecutionStatusId' AND object_id=OBJECT_ID('dbo.ArchiveRegistryItems'))
    CREATE NONCLUSTERED INDEX IX_ArchiveRegistryItems_ExecutionStatusId ON dbo.ArchiveRegistryItems(ExecutionStatusId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ArchiveRegistryItems_NameLink'          AND object_id=OBJECT_ID('dbo.ArchiveRegistryItems'))
    CREATE NONCLUSTERED INDEX IX_ArchiveRegistryItems_NameLink          ON dbo.ArchiveRegistryItems(NameLink);

/* ---------- KonturFavorites (Избранное Контур: фильтр по статусу + сортировка по дате) ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_KonturFavorites_Status_AddedAt' AND object_id=OBJECT_ID('dbo.KonturFavorites'))
    CREATE NONCLUSTERED INDEX IX_KonturFavorites_Status_AddedAt ON dbo.KonturFavorites(Status, AddedAt DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_KonturFavorites_RegeditId'      AND object_id=OBJECT_ID('dbo.KonturFavorites'))
    CREATE NONCLUSTERED INDEX IX_KonturFavorites_RegeditId      ON dbo.KonturFavorites(RegeditId);

/* ---------- KonturTemplates ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_KonturTemplates_AddedAt' AND object_id=OBJECT_ID('dbo.KonturTemplates'))
    CREATE NONCLUSTERED INDEX IX_KonturTemplates_AddedAt ON dbo.KonturTemplates(AddedAt DESC);

/* ---------- WorkflowLogs (Задачи: выборка по заявке) ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_WorkflowLogs_RegeditId' AND object_id=OBJECT_ID('dbo.WorkflowLogs'))
    CREATE NONCLUSTERED INDEX IX_WorkflowLogs_RegeditId ON dbo.WorkflowLogs(RegeditId);

/* ---------- AuditLogs (аудит: поиск по сущности и по времени) ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AuditLogs_Entity'    AND object_id=OBJECT_ID('dbo.AuditLogs'))
    CREATE NONCLUSTERED INDEX IX_AuditLogs_Entity    ON dbo.AuditLogs(EntityName, EntityId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AuditLogs_CreatedAt' AND object_id=OBJECT_ID('dbo.AuditLogs'))
    CREATE NONCLUSTERED INDEX IX_AuditLogs_CreatedAt ON dbo.AuditLogs(CreatedAtUtc DESC);

/* ---------- UserColumnPermissions (права на колонки: выборка по пользователю) ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_UserColumnPermissions_UserId' AND object_id=OBJECT_ID('dbo.UserColumnPermissions'))
    CREATE NONCLUSTERED INDEX IX_UserColumnPermissions_UserId ON dbo.UserColumnPermissions(UserId);

GO
PRINT 'Индексы созданы/проверены.';
GO

/* =============================================================================
   Дополнительно (по желанию) — статистика и обслуживание БД.
   Обновить статистику (ускоряет построение планов запросов):
       EXEC sp_updatestats;
   Рекомендуется настроить регулярное обслуживание (например, планом SQL Agent):
       ALTER INDEX ALL ON dbo.RegistryItems        REORGANIZE;
       ALTER INDEX ALL ON dbo.ArchiveRegistryItems REORGANIZE;
       ALTER INDEX ALL ON dbo.KonturFavorites      REORGANIZE;
   ============================================================================= */
