/* =============================================================================
   Таблица прав ролей [RolePermissions] (веб-приложение).
   Одна строка на роль: доступ к вкладкам (FeaturesJson) и права на колонки (ColumnsJson).
   Идемпотентно. Выполнить на БД [snabdrive_db] (или на веб-БД, если Identity вынесена).
   Значения по умолчанию для ролей приложение дописывает само при старте
   (Database:SeedRolesAndAdmin = true), поэтому INSERT здесь не обязателен.
   ============================================================================= */
USE [snabdrive_db];
GO
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RolePermissions
    (
        RoleName     nvarchar(64)  NOT NULL,
        FeaturesJson nvarchar(max) NOT NULL CONSTRAINT DF_RolePermissions_Features DEFAULT (N'{}'),
        ColumnsJson  nvarchar(max) NULL,
        StagesJson   nvarchar(max) NULL,
        UpdatedAtUtc datetime2(7)  NOT NULL CONSTRAINT DF_RolePermissions_Updated DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_RolePermissions PRIMARY KEY (RoleName)
    );
    PRINT 'Таблица [RolePermissions] создана.';
END
ELSE
    PRINT 'Таблица [RolePermissions] уже существует.';
GO

/* Колонка стадий для уже существующей таблицы (обновление со старой версии). */
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name='StagesJson' AND Object_ID=OBJECT_ID(N'dbo.RolePermissions'))
BEGIN
    ALTER TABLE dbo.RolePermissions ADD StagesJson nvarchar(max) NULL;
    PRINT 'Колонка [StagesJson] добавлена в [RolePermissions].';
END
GO
