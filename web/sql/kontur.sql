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
        PlaceOfDelivery nvarchar(1000) NULL,
        Winner          nvarchar(500)  NULL,
        ResultPrice     decimal(18,2)  NOT NULL CONSTRAINT DF_KonturFavorites_ResultPrice DEFAULT(0),
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
