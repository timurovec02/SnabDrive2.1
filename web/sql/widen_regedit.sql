USE [SnabDriveDB]
GO

-- ============================================================================
-- Расширение текстовых колонок реестра (Regedit) до 1000 символов.
-- В исходной схеме часть полей была nvarchar(200); здесь приводим к 1000.
-- Скрипт идемпотентен: повторяющийся запуск просто переустанавливает тип.
-- ============================================================================

ALTER TABLE [Regedit] ALTER COLUMN [NameLink] nvarchar(1000) NOT NULL;
ALTER TABLE [Regedit] ALTER COLUMN [PlaceOfDelivery] nvarchar(1000) NULL;
ALTER TABLE [Regedit] ALTER COLUMN [ReserveNumber] nvarchar(1000) NULL;
ALTER TABLE [Regedit] ALTER COLUMN [NationalMode] nvarchar(1000) NULL;
PRINT 'Regedit: NameLink/PlaceOfDelivery/ReserveNumber/NationalMode -> nvarchar(1000).';
GO

-- Зеркало в архиве (если колонки там тоже 200)
IF COL_LENGTH('ArchiveRegedit', 'PlaceOfDelivery') IS NOT NULL
BEGIN
    ALTER TABLE [ArchiveRegedit] ALTER COLUMN [NameLink] nvarchar(1000) NOT NULL;
    ALTER TABLE [ArchiveRegedit] ALTER COLUMN [PlaceOfDelivery] nvarchar(1000) NULL;
    ALTER TABLE [ArchiveRegedit] ALTER COLUMN [ReserveNumber] nvarchar(1000) NULL;
    ALTER TABLE [ArchiveRegedit] ALTER COLUMN [NationalMode] nvarchar(1000) NULL;
    PRINT 'ArchiveRegedit: текстовые колонки -> nvarchar(1000).';
END
GO
