/* ============================================================================
   SnabDriveDB — создание базы целиком одним скриптом.

   Что создаёт:
     1) саму базу SnabDriveDB (если её нет);
     2) существующие таблицы реестра (Regedit, ArchiveRegedit, User, справочники,
        CellColors) — один-в-один со схемой WPF-клиента;
     3) веб-таблицы (ASP.NET Core Identity + AuditLog + UserColumnPermission),
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

IF DB_ID(N'SnabDriveDB') IS NULL
BEGIN
    CREATE DATABASE [SnabDriveDB];
END
GO

USE [SnabDriveDB];
GO

/* ----------------------------------------------------------------------------
   1. Справочники
---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.B2BStatus', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.B2BStatus
    (
        ID      INT IDENTITY(1,1) NOT NULL,
        NameB2B NVARCHAR(MAX)     NOT NULL,
        CONSTRAINT PK_B2BStatus PRIMARY KEY (ID)
    );
END
GO

IF OBJECT_ID(N'dbo.TypeOfPurchase', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TypeOfPurchase
    (
        ID             INT IDENTITY(1,1) NOT NULL,
        NameOfPurchase NVARCHAR(MAX)     NOT NULL,
        ColorCode      NVARCHAR(MAX)     NULL,
        CONSTRAINT PK_TypeOfPurchase PRIMARY KEY (ID)
    );
END
GO

IF OBJECT_ID(N'dbo.ExecutionStatus', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExecutionStatus
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
IF OBJECT_ID(N'dbo.Regedit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Regedit
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
            REFERENCES dbo.TypeOfPurchase (ID),
        CONSTRAINT FK_Regedit_B2BStatus FOREIGN KEY (B2BStatusId)
            REFERENCES dbo.B2BStatus (ID),
        CONSTRAINT FK_Regedit_ExecutionStatus FOREIGN KEY (ExecutionStatusId)
            REFERENCES dbo.ExecutionStatus (ID)
    );

    CREATE INDEX IX_Regedit_Customer    ON dbo.Regedit (Customer);
    CREATE INDEX IX_Regedit_BiddingDate ON dbo.Regedit (BiddingDate);
END
GO

IF OBJECT_ID(N'dbo.ArchiveRegedit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ArchiveRegedit
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
            REFERENCES dbo.TypeOfPurchase (ID),
        CONSTRAINT FK_Archive_B2BStatus FOREIGN KEY (B2BStatusId)
            REFERENCES dbo.B2BStatus (ID),
        CONSTRAINT FK_Archive_ExecutionStatus FOREIGN KEY (ExecutionStatusId)
            REFERENCES dbo.ExecutionStatus (ID)
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
            REFERENCES dbo.Regedit (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_CellColors_RegeditId ON dbo.CellColors (RegeditId);
END
GO

IF OBJECT_ID(N'dbo.[User]', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[User]
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
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLog
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

    CREATE INDEX IX_AuditLog_Entity    ON dbo.AuditLog (EntityName, EntityId);
    CREATE INDEX IX_AuditLog_CreatedAt ON dbo.AuditLog (CreatedAtUtc);
END
GO

IF OBJECT_ID(N'dbo.UserColumnPermission', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserColumnPermission
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
        ON dbo.UserColumnPermission (UserId, ColumnKey);
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
IF NOT EXISTS (SELECT 1 FROM dbo.B2BStatus)
BEGIN
    INSERT INTO dbo.B2BStatus (NameB2B) VALUES
        (N'Не подан'), (N'Подан'), (N'Отклонён'), (N'Победа');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TypeOfPurchase)
BEGIN
    INSERT INTO dbo.TypeOfPurchase (NameOfPurchase, ColorCode) VALUES
        (N'Электронный аукцион', N'#FF4CAF50'),
        (N'Запрос котировок',    N'#FF2196F3'),
        (N'Открытый конкурс',    N'#FFFFC107');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ExecutionStatus)
BEGIN
    INSERT INTO dbo.ExecutionStatus (NameExecution) VALUES
        (N'Не начато'), (N'В работе'), (N'Завершено');
END
GO

PRINT N'База SnabDriveDB готова.';
GO
