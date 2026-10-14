/* ============================================================================
   snabdrive_db — ЕДИНЫЙ скрипт всей базы (ядро + Контур + воркфлоу + расширение).
   Идемпотентен. SSMS -> New Query -> вставить -> F5.
   ============================================================================ */

/* ============================================================================
   snabdrive_db — создание базы целиком одним скриптом.

   Что создаёт:
     1) саму базу snabdrive_db (если её нет);
     2) существующие таблицы реестра (RegistryItems, ArchiveRegistryItems, User, справочники,
        CellColors) — один-в-один со схемой WPF-клиента;
     3) веб-таблицы (ASP.NET Core Identity + AuditLogs + UserColumnPermissions),
        которые обычно создаёт EF-миграция.

   Как использовать:
     SSMS → New Query → вставьте скрипт → Execute (F5).
     Скрипт идемпотентен: все объекты создаются с проверкой IF NOT EXISTS,
     повторный запуск безопасен.

   Важно после запуска этого скрипта:
     в appsettings.Production.json поставьте
        "Database": { "Provider": "SqlServer", "AutoMigrateIdentitySchema": false, ... }
     — таблицы уже есть, миграцию можно не генерировать. Роли и администратор
     всё равно создадутся при первом старте приложения (SeedRolesAndAdmin = true).
   ========================================================================== */

IF DB_ID(N'snabdrive_db') IS NULL
BEGIN
    CREATE DATABASE [snabdrive_db];
END
GO

USE [snabdrive_db];
GO

/* ----------------------------------------------------------------------------
   1. Справочники
---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.B2BStatuses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.B2BStatuses
    (
        ID      INT IDENTITY(1,1) NOT NULL,
        NameB2B NVARCHAR(MAX)     NOT NULL,
        CONSTRAINT PK_B2BStatus PRIMARY KEY (ID)
    );
END
GO

IF OBJECT_ID(N'dbo.PurchaseTypes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseTypes
    (
        ID             INT IDENTITY(1,1) NOT NULL,
        NameOfPurchase NVARCHAR(MAX)     NOT NULL,
        ColorCode      NVARCHAR(MAX)     NULL,
        CONSTRAINT PK_TypeOfPurchase PRIMARY KEY (ID)
    );
END
GO

IF OBJECT_ID(N'dbo.ExecutionStatuses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExecutionStatuses
    (
        ID            INT IDENTITY(1,1) NOT NULL,
        NameExecution NVARCHAR(MAX)     NOT NULL,
        CONSTRAINT PK_ExecutionStatus PRIMARY KEY (ID)
    );
END
GO

/* ----------------------------------------------------------------------------
   2. Реестр и архив
---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.RegistryItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RegistryItems
    (
        Id                            INT IDENTITY(1,1)  NOT NULL,
        NameLink                      NVARCHAR(1000)     NOT NULL,
        PlaceOfDelivery               NVARCHAR(1000)     NULL,
        ReserveNumber                 NVARCHAR(1000)     NULL,
        NationalMode                  NVARCHAR(1000)     NULL,
        DateOfTransferForPlacement    DATETIME2(7)       NULL,
        DateOfPlacement               DATETIME2(7)       NULL,
        BiddingDate                   DATETIME2(7)       NULL,
        DateResults                   DATETIME2(7)       NULL,
        DateOfConclusionOfTheContract DATETIME2(7)       NULL,
        NMCK                          DECIMAL(18,2)      NOT NULL CONSTRAINT DF_Regedit_NMCK DEFAULT (0),
        MinPrice                      DECIMAL(18,2)      NOT NULL CONSTRAINT DF_Regedit_MinPrice DEFAULT (0),
        ResultPrice                   DECIMAL(18,2)      NOT NULL CONSTRAINT DF_Regedit_ResultPrice DEFAULT (0),
        Winner                        NVARCHAR(MAX)      NULL,
        DeliveryTime                  NVARCHAR(MAX)      NULL,
        Description                   NVARCHAR(MAX)      NULL,
        Note                          NVARCHAR(MAX)      NULL,
        Customer                      NVARCHAR(MAX)      NULL,
        TypeOfPurchaseId              INT                NULL,
        B2BStatusId                   INT                NULL,
        ExecutionStatusId             INT                NULL,
        IsFinished                    BIT                NULL,
        CONSTRAINT PK_Regedit PRIMARY KEY (Id),
        CONSTRAINT FK_Regedit_TypeOfPurchase FOREIGN KEY (TypeOfPurchaseId)
            REFERENCES dbo.PurchaseTypes (ID),
        CONSTRAINT FK_Regedit_B2BStatus FOREIGN KEY (B2BStatusId)
            REFERENCES dbo.B2BStatuses (ID),
        CONSTRAINT FK_Regedit_ExecutionStatus FOREIGN KEY (ExecutionStatusId)
            REFERENCES dbo.ExecutionStatuses (ID)
    );

    CREATE INDEX IX_Regedit_Customer    ON dbo.RegistryItems (Customer);
    CREATE INDEX IX_Regedit_BiddingDate ON dbo.RegistryItems (BiddingDate);
END
GO

IF OBJECT_ID(N'dbo.ArchiveRegistryItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ArchiveRegistryItems
    (
        Id                            INT IDENTITY(1,1)  NOT NULL,
        IdOld                         INT                NOT NULL,
        NameLink                      NVARCHAR(1000)     NOT NULL,
        PlaceOfDelivery               NVARCHAR(1000)     NULL,
        ReserveNumber                 NVARCHAR(1000)     NULL,
        NationalMode                  NVARCHAR(1000)     NULL,
        DateOfTransferForPlacement    DATETIME2(7)       NULL,
        DateOfPlacement               DATETIME2(7)       NULL,
        BiddingDate                   DATETIME2(7)       NULL,
        DateResults                   DATETIME2(7)       NULL,
        DateOfConclusionOfTheContract DATETIME2(7)       NULL,
        NMCK                          DECIMAL(18,2)      NOT NULL CONSTRAINT DF_Archive_NMCK DEFAULT (0),
        MinPrice                      DECIMAL(18,2)      NOT NULL CONSTRAINT DF_Archive_MinPrice DEFAULT (0),
        ResultPrice                   DECIMAL(18,2)      NOT NULL CONSTRAINT DF_Archive_ResultPrice DEFAULT (0),
        Winner                        NVARCHAR(MAX)      NULL,
        DeliveryTime                  NVARCHAR(MAX)      NULL,
        Description                   NVARCHAR(MAX)      NULL,
        Note                          NVARCHAR(MAX)      NULL,
        Customer                      NVARCHAR(MAX)      NULL,
        TypeOfPurchaseId              INT                NULL,
        B2BStatusId                   INT                NULL,
        ExecutionStatusId             INT                NULL,
        ArchivateDate                 DATETIME2(7)       NULL,
        CONSTRAINT PK_ArchiveRegedit PRIMARY KEY (Id),
        CONSTRAINT FK_Archive_TypeOfPurchase FOREIGN KEY (TypeOfPurchaseId)
            REFERENCES dbo.PurchaseTypes (ID),
        CONSTRAINT FK_Archive_B2BStatus FOREIGN KEY (B2BStatusId)
            REFERENCES dbo.B2BStatuses (ID),
        CONSTRAINT FK_Archive_ExecutionStatus FOREIGN KEY (ExecutionStatusId)
            REFERENCES dbo.ExecutionStatuses (ID)
    );
END
GO

/* ----------------------------------------------------------------------------
   3. Подсветка ячеек и старые пользователи WPF
---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.CellColors', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CellColors
    (
        Id         INT IDENTITY(1,1) NOT NULL,
        RegeditId  INT               NOT NULL,
        ColumnName NVARCHAR(MAX)     NOT NULL,
        ColorCode  NVARCHAR(MAX)     NOT NULL,
        CONSTRAINT PK_CellColors PRIMARY KEY (Id),
        CONSTRAINT FK_CellColors_Regedit FOREIGN KEY (RegeditId)
            REFERENCES dbo.RegistryItems (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_CellColors_RegeditId ON dbo.CellColors (RegeditId);
END
GO

IF OBJECT_ID(N'dbo.[LegacyUsers]', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[LegacyUsers]
    (
        Id       INT IDENTITY(1,1) NOT NULL,
        Login    NVARCHAR(MAX)     NOT NULL,
        Password NVARCHAR(MAX)     NULL,
        Email    NVARCHAR(MAX)     NULL,
        IsAdmin  BIT               NOT NULL CONSTRAINT DF_User_IsAdmin DEFAULT (0),
        CONSTRAINT PK_User PRIMARY KEY (Id)
    );
END
GO

/* ----------------------------------------------------------------------------
   4. ASP.NET Core Identity
---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUsers
    (
        Id                   NVARCHAR(450)     NOT NULL,
        UserName             NVARCHAR(256)     NULL,
        NormalizedUserName   NVARCHAR(256)     NULL,
        Email                NVARCHAR(256)     NULL,
        NormalizedEmail      NVARCHAR(256)     NULL,
        EmailConfirmed       BIT               NOT NULL,
        PasswordHash         NVARCHAR(MAX)     NULL,
        SecurityStamp        NVARCHAR(MAX)     NULL,
        ConcurrencyStamp     NVARCHAR(MAX)     NULL,
        PhoneNumber          NVARCHAR(MAX)     NULL,
        PhoneNumberConfirmed BIT               NOT NULL,
        TwoFactorEnabled     BIT               NOT NULL,
        LockoutEnd           DATETIMEOFFSET(7) NULL,
        LockoutEnabled       BIT               NOT NULL,
        AccessFailedCount    INT               NOT NULL,
        -- собственные колонки веб-приложения
        DisplayName          NVARCHAR(200)     NULL,
        LegacyUserId         INT               NULL,
        LastLoginUtc         DATETIME2(7)      NULL,
        LastSeenUtc          DATETIME2(7)      NULL,
        IsBlocked            BIT               NOT NULL CONSTRAINT DF_Users_IsBlocked DEFAULT (0),
        ColumnAccessMode     INT               NOT NULL CONSTRAINT DF_Users_ColumnAccessMode DEFAULT (0),
        CONSTRAINT PK_AspNetUsers PRIMARY KEY (Id)
    );

    CREATE UNIQUE INDEX UserNameIndex ON dbo.AspNetUsers (NormalizedUserName)
        WHERE NormalizedUserName IS NOT NULL;
    CREATE INDEX EmailIndex ON dbo.AspNetUsers (NormalizedEmail);
END
GO

IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetRoles
    (
        Id               NVARCHAR(450) NOT NULL,
        Name             NVARCHAR(256) NULL,
        NormalizedName   NVARCHAR(256) NULL,
        ConcurrencyStamp NVARCHAR(MAX) NULL,
        CONSTRAINT PK_AspNetRoles PRIMARY KEY (Id)
    );

    CREATE UNIQUE INDEX RoleNameIndex ON dbo.AspNetRoles (NormalizedName)
        WHERE NormalizedName IS NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserRoles
    (
        UserId NVARCHAR(450) NOT NULL,
        RoleId NVARCHAR(450) NOT NULL,
        CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId)
            REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE,
        CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId)
            REFERENCES dbo.AspNetRoles (Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'dbo.AspNetUserClaims', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserClaims
    (
        Id         INT IDENTITY(1,1) NOT NULL,
        UserId     NVARCHAR(450)     NOT NULL,
        ClaimType  NVARCHAR(MAX)     NULL,
        ClaimValue NVARCHAR(MAX)     NULL,
        CONSTRAINT PK_AspNetUserClaims PRIMARY KEY (Id),
        CONSTRAINT FK_UserClaims_Users FOREIGN KEY (UserId)
            REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'dbo.AspNetUserLogins', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserLogins
    (
        LoginProvider       NVARCHAR(450) NOT NULL,
        ProviderKey         NVARCHAR(450) NOT NULL,
        ProviderDisplayName NVARCHAR(MAX) NULL,
        UserId              NVARCHAR(450) NOT NULL,
        CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
        CONSTRAINT FK_UserLogins_Users FOREIGN KEY (UserId)
            REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'dbo.AspNetUserTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserTokens
    (
        UserId        NVARCHAR(450) NOT NULL,
        LoginProvider NVARCHAR(450) NOT NULL,
        Name          NVARCHAR(450) NOT NULL,
        Value         NVARCHAR(MAX) NULL,
        CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, Name),
        CONSTRAINT FK_UserTokens_Users FOREIGN KEY (UserId)
            REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'dbo.AspNetRoleClaims', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetRoleClaims
    (
        Id         INT IDENTITY(1,1) NOT NULL,
        RoleId     NVARCHAR(450)     NOT NULL,
        ClaimType  NVARCHAR(MAX)     NULL,
        ClaimValue NVARCHAR(MAX)     NULL,
        CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY (Id),
        CONSTRAINT FK_RoleClaims_Roles FOREIGN KEY (RoleId)
            REFERENCES dbo.AspNetRoles (Id) ON DELETE CASCADE
    );
END
GO

/* ----------------------------------------------------------------------------
   5. Журнал аудита и права на колонки
---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs
    (
        Id          BIGINT IDENTITY(1,1) NOT NULL,
        CreatedAtUtc DATETIME2(7)        NOT NULL,
        Action      NVARCHAR(50)         NOT NULL,
        EntityName  NVARCHAR(100)        NOT NULL,
        EntityId    INT                  NOT NULL,
        UserId      NVARCHAR(450)        NULL,
        UserName    NVARCHAR(256)        NOT NULL,
        Summary     NVARCHAR(500)        NULL,
        ChangesJson NVARCHAR(MAX)        NULL,
        CONSTRAINT PK_AuditLog PRIMARY KEY (Id)
    );

    CREATE INDEX IX_AuditLog_Entity    ON dbo.AuditLogs (EntityName, EntityId);
    CREATE INDEX IX_AuditLog_CreatedAt ON dbo.AuditLogs (CreatedAtUtc);
END
GO

IF OBJECT_ID(N'dbo.UserColumnPermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserColumnPermissions
    (
        Id        INT IDENTITY(1,1) NOT NULL,
        UserId    NVARCHAR(450)     NOT NULL,
        ColumnKey NVARCHAR(100)     NOT NULL,
        CanView   BIT               NOT NULL,
        CanEdit   BIT               NOT NULL,
        CONSTRAINT PK_UserColumnPermission PRIMARY KEY (Id),
        CONSTRAINT FK_ColumnPermission_Users FOREIGN KEY (UserId)
            REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX IX_UserColumnPermission_Unique
        ON dbo.UserColumnPermissions (UserId, ColumnKey);
END
GO

/* ----------------------------------------------------------------------------
   6. История миграций EF (пустая — миграции в этом сценарии не нужны)
---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.__EFMigrationsHistory
    (
        MigrationId    NVARCHAR(150) NOT NULL,
        ProductVersion NVARCHAR(32)  NOT NULL,
        CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY (MigrationId)
    );
END
GO

/* ----------------------------------------------------------------------------
   7. Стартовые значения справочников (по желанию — удалите блок, если не нужны)
---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.B2BStatuses)
BEGIN
    INSERT INTO dbo.B2BStatuses (NameB2B) VALUES
        (N'Не подан'), (N'Подан'), (N'Отклонён'), (N'Победа');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PurchaseTypes)
BEGIN
    INSERT INTO dbo.PurchaseTypes (NameOfPurchase, ColorCode) VALUES
        (N'Электронный аукцион', N'#FF4CAF50'),
        (N'Запрос котировок',    N'#FF2196F3'),
        (N'Открытый конкурс',    N'#FFFFC107');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ExecutionStatuses)
BEGIN
    INSERT INTO dbo.ExecutionStatuses (NameExecution) VALUES
        (N'Не начато'), (N'В работе'), (N'Завершено');
END
GO

PRINT N'База snabdrive_db готова.';
GO

GO

/* =============================================================================
   SnabDrive Web — «Избранное Контур»
   Создаёт таблицу KonturFavorites и добавляет колонку KonturEnabled в AspNetUsers.
   Выполнить один раз в SSMS на базе snabdrive_db.
   ============================================================================= */
USE [snabdrive_db];
GO

IF OBJECT_ID('dbo.KonturFavorites', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.KonturFavorites
    (
        Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_KonturFavorites PRIMARY KEY,
        PurchaseNumber  nvarchar(200)  NULL,
        NameLink        nvarchar(1000) NOT NULL,
        Customer        nvarchar(1000) NULL,
        NMCK            decimal(18,2)  NOT NULL CONSTRAINT DF_KonturFavorites_NMCK DEFAULT(0),
        PlaceOfDelivery nvarchar(1000) NULL,
        Winner          nvarchar(500)  NULL,
        ResultPrice     decimal(18,2)  NOT NULL CONSTRAINT DF_KonturFavorites_ResultPrice DEFAULT(0),
        Label           nvarchar(500)  NULL,
        EisLink         nvarchar(1000) NULL,
        BiddingDate     datetime2(7)   NULL,
        DateOfPlacement datetime2(7)   NULL,
        Status          int            NOT NULL CONSTRAINT DF_KonturFavorites_Status DEFAULT(0),
        RegeditId       int            NULL,
        RawJson         nvarchar(max)  NULL,
        AddedAt         datetime2(7)   NOT NULL CONSTRAINT DF_KonturFavorites_AddedAt DEFAULT(GETDATE()),
        AddedBy         nvarchar(256)  NULL
    );

    CREATE INDEX IX_KonturFavorites_Status ON dbo.KonturFavorites (Status);
    PRINT 'KonturFavorites создана.';
END
ELSE
    PRINT 'KonturFavorites уже существует.';
GO

IF OBJECT_ID('dbo.KonturFavorites', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.KonturFavorites', 'PlaceOfDelivery') IS NULL
        ALTER TABLE dbo.KonturFavorites ADD PlaceOfDelivery nvarchar(1000) NULL;
    IF COL_LENGTH('dbo.KonturFavorites', 'Winner') IS NULL
        ALTER TABLE dbo.KonturFavorites ADD Winner nvarchar(500) NULL;
    IF COL_LENGTH('dbo.KonturFavorites', 'ResultPrice') IS NULL
        ALTER TABLE dbo.KonturFavorites ADD ResultPrice decimal(18,2) NOT NULL CONSTRAINT DF_KonturFavorites_ResultPrice DEFAULT(0);

    -- Защитное расширение: если колонки созданы короче, приводим к нужной длине.
    IF COL_LENGTH('dbo.KonturFavorites', 'PlaceOfDelivery') < 1000
        ALTER TABLE dbo.KonturFavorites ALTER COLUMN PlaceOfDelivery nvarchar(1000) NULL;
    IF COL_LENGTH('dbo.KonturFavorites', 'Winner') < 500
        ALTER TABLE dbo.KonturFavorites ALTER COLUMN Winner nvarchar(500) NULL;
    IF COL_LENGTH('dbo.KonturFavorites', 'Customer') < 1000
        ALTER TABLE dbo.KonturFavorites ALTER COLUMN Customer nvarchar(1000) NULL;
    IF COL_LENGTH('dbo.KonturFavorites', 'NameLink') < 1000
        ALTER TABLE dbo.KonturFavorites ALTER COLUMN NameLink nvarchar(1000) NOT NULL;
    IF COL_LENGTH('dbo.KonturFavorites', 'Label') IS NULL
        ALTER TABLE dbo.KonturFavorites ADD Label nvarchar(500) NULL;
    IF COL_LENGTH('dbo.KonturFavorites', 'EisLink') IS NULL
        ALTER TABLE dbo.KonturFavorites ADD EisLink nvarchar(1000) NULL;
END
GO

IF COL_LENGTH('dbo.AspNetUsers', 'KonturEnabled') IS NULL
BEGIN
    ALTER TABLE dbo.AspNetUsers ADD KonturEnabled bit NOT NULL CONSTRAINT DF_AspNetUsers_KonturEnabled DEFAULT(0);
    PRINT 'AspNetUsers.KonturEnabled добавлена.';
END
ELSE
    PRINT 'AspNetUsers.KonturEnabled уже существует.';
GO

IF COL_LENGTH('dbo.AspNetUsers', 'SchedulerEnabled') IS NULL
BEGIN
    ALTER TABLE dbo.AspNetUsers ADD SchedulerEnabled bit NOT NULL CONSTRAINT DF_AspNetUsers_SchedulerEnabled DEFAULT(0);
    PRINT 'AspNetUsers.SchedulerEnabled добавлена.';
END
ELSE
    PRINT 'AspNetUsers.SchedulerEnabled уже существует.';
GO

-- ============================================================================
-- Контур ловушка (--templates): отдельная таблица для директора с аналитикой по датам
-- ============================================================================
IF OBJECT_ID('dbo.KonturTemplates', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.KonturTemplates
    (
        Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_KonturTemplates PRIMARY KEY,
        PurchaseNumber  nvarchar(200)  NULL,
        NameLink        nvarchar(1000) NOT NULL,
        Customer        nvarchar(1000) NULL,
        NMCK            decimal(18,2)  NOT NULL CONSTRAINT DF_KonturTemplates_NMCK DEFAULT(0),
        PlaceOfDelivery nvarchar(1000) NULL,
        Winner          nvarchar(500)  NULL,
        ResultPrice     decimal(18,2)  NOT NULL CONSTRAINT DF_KonturTemplates_ResultPrice DEFAULT(0),
        Label           nvarchar(500)  NULL,
        EisLink         nvarchar(1000) NULL,
        BiddingDate     datetime2(7)   NULL,
        DateOfPlacement datetime2(7)   NULL,
        RawJson         nvarchar(max)  NULL,
        AddedAt         datetime2(7)   NOT NULL CONSTRAINT DF_KonturTemplates_AddedAt DEFAULT(GETDATE()),
        UpdatedAt       datetime2(7)   NULL,
        AddedBy         nvarchar(256)  NULL
    );

    CREATE INDEX IX_KonturTemplates_PurchaseNumber ON dbo.KonturTemplates (PurchaseNumber);
    PRINT 'KonturTemplates создана.';
END
ELSE
    PRINT 'KonturTemplates уже существует.';
GO

GO

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

GO

USE [snabdrive_db]
GO

-- ============================================================================
-- Расширение текстовых колонок реестра (RegistryItems) до 1000 символов.
-- В исходной схеме часть полей была nvarchar(200); здесь приводим к 1000.
-- Скрипт идемпотентен: повторяющийся запуск просто переустанавливает тип.
-- ============================================================================

ALTER TABLE [RegistryItems] ALTER COLUMN [NameLink] nvarchar(1000) NOT NULL;
ALTER TABLE [RegistryItems] ALTER COLUMN [PlaceOfDelivery] nvarchar(1000) NULL;
ALTER TABLE [RegistryItems] ALTER COLUMN [ReserveNumber] nvarchar(1000) NULL;
ALTER TABLE [RegistryItems] ALTER COLUMN [NationalMode] nvarchar(1000) NULL;
PRINT 'RegistryItems: NameLink/PlaceOfDelivery/ReserveNumber/NationalMode -> nvarchar(1000).';
GO

-- Зеркало в архиве (если колонки там тоже 200)
IF COL_LENGTH('ArchiveRegistryItems', 'PlaceOfDelivery') IS NOT NULL
BEGIN
    ALTER TABLE [ArchiveRegistryItems] ALTER COLUMN [NameLink] nvarchar(1000) NOT NULL;
    ALTER TABLE [ArchiveRegistryItems] ALTER COLUMN [PlaceOfDelivery] nvarchar(1000) NULL;
    ALTER TABLE [ArchiveRegistryItems] ALTER COLUMN [ReserveNumber] nvarchar(1000) NULL;
    ALTER TABLE [ArchiveRegistryItems] ALTER COLUMN [NationalMode] nvarchar(1000) NULL;
    PRINT 'ArchiveRegistryItems: текстовые колонки -> nvarchar(1000).';
END
GO
