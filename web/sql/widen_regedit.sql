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
