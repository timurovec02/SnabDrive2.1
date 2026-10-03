/* =============================================================================
   SnabDrive Web — «Избранное Контур»
   Создаёт таблицу KonturFavorites и добавляет колонку KonturEnabled в AspNetUsers.
   Выполнить один раз в SSMS на базе SnabDriveDB.
   ============================================================================= */
USE [SnabDriveDB];
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

IF COL_LENGTH('dbo.AspNetUsers', 'KonturEnabled') IS NULL
BEGIN
    ALTER TABLE dbo.AspNetUsers ADD KonturEnabled bit NOT NULL CONSTRAINT DF_AspNetUsers_KonturEnabled DEFAULT(0);
    PRINT 'AspNetUsers.KonturEnabled добавлена.';
END
ELSE
    PRINT 'AspNetUsers.KonturEnabled уже существует.';
GO
