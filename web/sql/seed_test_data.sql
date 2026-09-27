/* ============================================================================
   SnabDriveDB — тестовые данные.

   Заполняет:
     - справочники (если пусты);
     - dbo.Regedit        ~16 реалистичных закупок (разные даты, суммы, статусы);
     - dbo.ArchiveRegedit несколько архивных записей;
     - dbo.CellColors     ручная подсветка пары ячеек;
     - dbo.[User]         пара старых пользователей WPF (для импорта в Identity).

   Безопасен к повторному запуску: каждый блок выполняется только если
   соответствующая таблица пуста.

   Запуск: SSMS → New Query → Execute (F5). База должна уже существовать
   (см. create_SnabDriveDB.sql).
   ========================================================================== */

USE [SnabDriveDB];
GO

/* ----------------------------------------------------------------------------
   1. Справочники (если пусты)
---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.B2BStatus)
    INSERT INTO dbo.B2BStatus (NameB2B) VALUES
        (N'Не подан'), (N'Подан'), (N'Отклонён'), (N'Победа');

IF NOT EXISTS (SELECT 1 FROM dbo.TypeOfPurchase)
    INSERT INTO dbo.TypeOfPurchase (NameOfPurchase, ColorCode) VALUES
        (N'Электронный аукцион', N'#FF4CAF50'),
        (N'Запрос котировок',    N'#FF2196F3'),
        (N'Открытый конкурс',    N'#FFFFC107'),
        (N'Единственный поставщик', N'#FF9C27B0');

IF NOT EXISTS (SELECT 1 FROM dbo.ExecutionStatus)
    INSERT INTO dbo.ExecutionStatus (NameExecution) VALUES
        (N'Не начато'), (N'В работе'), (N'Завершено');
GO

/* ----------------------------------------------------------------------------
   2. Реестр (если пуст)
---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Regedit)
BEGIN
    INSERT INTO dbo.Regedit
    (
        NameLink, Customer, PlaceOfDelivery, ReserveNumber, NationalMode,
        DateOfTransferForPlacement, DateOfPlacement, BiddingDate, DateResults, DateOfConclusionOfTheContract,
        NMCK, MinPrice, ResultPrice, Winner, DeliveryTime, Description, Note,
        TypeOfPurchaseId, B2BStatusId, ExecutionStatusId, IsFinished
    )
    SELECT v.NameLink, v.Customer, v.PlaceOfDelivery, v.ReserveNumber, v.NationalMode,
           v.DateOfTransferForPlacement, v.DateOfPlacement, v.BiddingDate, v.DateResults, v.DateOfConclusionOfTheContract,
           v.NMCK, v.MinPrice, v.ResultPrice, v.Winner, v.DeliveryTime, v.Description, v.Note,
           (SELECT TOP 1 ID FROM dbo.TypeOfPurchase WHERE NameOfPurchase = v.Purchase),
           (SELECT TOP 1 ID FROM dbo.B2BStatus      WHERE NameB2B      = v.B2B),
           (SELECT TOP 1 ID FROM dbo.ExecutionStatus WHERE NameExecution = v.Exec),
           v.IsFinished
    FROM
    (
        VALUES
        (N'Поставка оргтехники для нужд учреждения', N'ГКУ КК «Центр закупок»', N'г. Краснодар', N'Р-101', N'Нет',
         '20250201','20250205','20250215','20250220','20250301', 1500000.00, 1450000.00, 1420000.00, N'ООО «Техно-Юг»', N'30 дней', N'Ноутбуки и МФУ', N'', N'Электронный аукцион', N'Победа', N'Завершено', CAST(1 AS BIT)),
        (N'Поставка канцелярских товаров', N'МБУ «Управление образования»', N'г. Сочи', N'Р-102', N'Нет',
         '20250210','20250212','20250225', NULL, NULL, 350000.00, 340000.00, 0, NULL, N'14 дней', N'Бумага, ручки', N'', N'Запрос котировок', N'Подан', N'В работе', NULL),
        (N'Поставка медицинского оборудования', N'ГБУЗ «Краевая больница»', N'г. Краснодар', N'Р-103', N'Да',
         '20250115','20250120','20250201','20250206', NULL, 8200000.00, 8000000.00, 0, NULL, N'60 дней', N'Аппарат УЗИ', N'Нац. режим', N'Открытый конкурс', N'Подан', N'В работе', NULL),
        (N'Поставка ГСМ на 1 квартал', N'Администрация МО г. Краснодар', N'г. Краснодар', N'Р-104', N'Нет',
         '20250105','20250109','20250120','20250124','20250201', 2100000.00, 2050000.00, 2010000.00, N'АО «Лукойл-Юг»', N'10 дней', N'Бензин АИ-95', N'', N'Электронный аукцион', N'Победа', N'Завершено', CAST(1 AS BIT)),
        (N'Оказание услуг по охране зданий', N'ГКУ КК «Хозуправление»', N'г. Краснодар', N'Р-105', N'Нет',
         '20250301','20250303','20250315', NULL, NULL, 1200000.00, 1180000.00, 0, NULL, N'12 месяцев', N'ЧОП, круглосуточно', N'', N'Открытый конкурс', N'Не подан', N'Не начато', NULL),
        (N'Поставка мебели для офиса', N'МКУ «Управление имущества»', N'г. Новороссийск', N'Р-106', N'Нет',
         '20250218','20250220','20250305', NULL, NULL, 780000.00, 760000.00, 0, NULL, N'21 день', N'Столы, кресла', N'', N'Запрос котировок', N'Отклонён', N'Не начато', NULL),
        (N'Поставка продуктов питания в школы', N'МБУ «Управление образования»', N'г. Анапа', N'Р-107', N'Да',
         '20250125','20250128','20250210','20250214','20250225', 4600000.00, 4500000.00, 4480000.00, N'ООО «Продторг»', N'по графику', N'Крупы, молоко', N'Нац. режим', N'Электронный аукцион', N'Победа', N'В работе', NULL),
        (N'Текущий ремонт кровли', N'ГБУ КК «Спортшкола»', N'г. Армавир', N'Р-108', N'Нет',
         '20250310','20250312','20250325', NULL, NULL, 2900000.00, 2850000.00, 0, NULL, N'45 дней', N'Мягкая кровля', N'', N'Открытый конкурс', N'Подан', N'В работе', NULL),
        (N'Поставка серверного оборудования', N'ГАУ КК «Инфоцентр»', N'г. Краснодар', N'Р-109', N'Нет',
         '20250205','20250207','20250218','20250221', NULL, 5400000.00, 5300000.00, 0, NULL, N'30 дней', N'Серверы, СХД', N'', N'Электронный аукцион', N'Подан', N'В работе', NULL),
        (N'Оказание транспортных услуг', N'ГКУ КК «Хозуправление»', N'г. Краснодар', N'Р-110', N'Нет',
         '20250112','20250115','20250127','20250130','20250210', 950000.00, 940000.00, 930000.00, N'ИП Смирнов А.В.', N'по заявкам', N'Аренда автобусов', N'', N'Запрос котировок', N'Победа', N'Завершено', CAST(0 AS BIT)),
        (N'Поставка средств связи', N'МВД по Краснодарскому краю', N'г. Краснодар', N'Р-111', N'Нет',
         '20250315','20250317','20250330', NULL, NULL, 1750000.00, 1700000.00, 0, NULL, N'20 дней', N'Радиостанции', N'', N'Электронный аукцион', N'Не подан', N'Не начато', NULL),
        (N'Благоустройство парковой зоны', N'Администрация МО г. Сочи', N'г. Сочи', N'Р-112', N'Нет',
         '20250222','20250225','20250310', NULL, NULL, 6800000.00, 6700000.00, 0, NULL, N'90 дней', N'Озеленение, освещение', N'', N'Открытый конкурс', N'Подан', N'В работе', NULL),
        (N'Поставка лабораторного оборудования', N'ФГБОУ ВО «КубГУ»', N'г. Краснодар', N'Р-113', N'Да',
         '20250118','20250121','20250203','20250207', NULL, 3300000.00, 3250000.00, 0, NULL, N'40 дней', N'Микроскопы, центрифуги', N'Нац. режим', N'Электронный аукцион', N'Отклонён', N'Не начато', NULL),
        (N'Поставка спецодежды и СИЗ', N'ГУП КК «Кубаньводоканал»', N'г. Краснодар', N'Р-114', N'Нет',
         '20250208','20250210','20250224','20250227','20250308', 1100000.00, 1080000.00, 1060000.00, N'ООО «Спецзащита»', N'15 дней', N'Костюмы, каски', N'', N'Запрос котировок', N'Победа', N'Завершено', CAST(1 AS BIT)),
        (N'Оказание услуг по уборке помещений', N'ГБУ КК «Краевой Дом культуры»', N'г. Краснодар', N'Р-115', N'Нет',
         '20250305','20250307','20250320', NULL, NULL, 640000.00, 630000.00, 0, NULL, N'6 месяцев', N'Клининг', N'', N'Единственный поставщик', N'Подан', N'В работе', NULL),
        (N'Поставка котельного оборудования', N'МУП «Теплоэнерго»', N'ст. Динская', N'Р-116', N'Нет',
         '20250120','20250123','20250205','20250209', NULL, 7400000.00, 7300000.00, 0, NULL, N'50 дней', N'Котлы, насосы', N'', N'Открытый конкурс', N'Подан', N'В работе', NULL)
    ) AS v(NameLink, Customer, PlaceOfDelivery, ReserveNumber, NationalMode,
           DateOfTransferForPlacement, DateOfPlacement, BiddingDate, DateResults, DateOfConclusionOfTheContract,
           NMCK, MinPrice, ResultPrice, Winner, DeliveryTime, Description, Note, Purchase, B2B, Exec, IsFinished);
END
GO

/* ----------------------------------------------------------------------------
   3. Архив (если пуст)
---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.ArchiveRegedit)
BEGIN
    INSERT INTO dbo.ArchiveRegedit
    (
        IdOld, NameLink, Customer, PlaceOfDelivery, ReserveNumber, NationalMode,
        BiddingDate, NMCK, MinPrice, ResultPrice, Winner, Description,
        TypeOfPurchaseId, B2BStatusId, ExecutionStatusId, ArchivateDate
    )
    SELECT 9001, N'Поставка оргтехники (2024)', N'ГКУ КК «Центр закупок»', N'г. Краснодар', N'Р-090', N'Нет',
           '20241115', 1200000.00, 1150000.00, 1130000.00, N'ООО «Компьютер-Сервис»', N'Архив 2024',
           (SELECT TOP 1 ID FROM dbo.TypeOfPurchase WHERE NameOfPurchase = N'Электронный аукцион'),
           (SELECT TOP 1 ID FROM dbo.B2BStatus WHERE NameB2B = N'Победа'),
           (SELECT TOP 1 ID FROM dbo.ExecutionStatus WHERE NameExecution = N'Завершено'),
           '20250110'
    UNION ALL
    SELECT 9002, N'Поставка бумаги (2024)', N'МБУ «Управление образования»', N'г. Сочи', N'Р-091', N'Нет',
           '20241201', 280000.00, 270000.00, 265000.00, N'ООО «Офис-Маркет»', N'Архив 2024',
           (SELECT TOP 1 ID FROM dbo.TypeOfPurchase WHERE NameOfPurchase = N'Запрос котировок'),
           (SELECT TOP 1 ID FROM dbo.B2BStatus WHERE NameB2B = N'Победа'),
           (SELECT TOP 1 ID FROM dbo.ExecutionStatus WHERE NameExecution = N'Завершено'),
           '20250112';
END
GO

/* ----------------------------------------------------------------------------
   4. Подсветка ячеек (если пуста) — ключи колонок как в WPF
---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.CellColors)
BEGIN
    INSERT INTO dbo.CellColors (RegeditId, ColumnName, ColorCode)
    SELECT r.Id, N'Customer', N'#FFFFEB3B'
    FROM dbo.Regedit r WHERE r.ReserveNumber = N'Р-103';

    INSERT INTO dbo.CellColors (RegeditId, ColumnName, ColorCode)
    SELECT r.Id, N'BiddingDate', N'#FFC8E6C9'
    FROM dbo.Regedit r WHERE r.ReserveNumber = N'Р-107';

    INSERT INTO dbo.CellColors (RegeditId, ColumnName, ColorCode)
    SELECT r.Id, N'NMCK', N'#FFFFCDD2'
    FROM dbo.Regedit r WHERE r.ReserveNumber = N'Р-112';
END
GO

/* ----------------------------------------------------------------------------
   5. Старые пользователи WPF (для импорта в Identity при ImportLegacyUsers=true)
---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.[User])
BEGIN
    INSERT INTO dbo.[User] (Login, Password, Email, IsAdmin) VALUES
        (N'ivanov',  N'ivanov123',  N'ivanov@snabdrive.ru',  0),
        (N'petrova', N'petrova123', N'petrova@snabdrive.ru', 0),
        (N'sidorov', N'sidorov123', N'sidorov@snabdrive.ru', 1);
END
GO

PRINT N'Тестовые данные загружены: '
      + CAST((SELECT COUNT(*) FROM dbo.Regedit) AS NVARCHAR(10)) + N' записей реестра, '
      + CAST((SELECT COUNT(*) FROM dbo.ArchiveRegedit) AS NVARCHAR(10)) + N' в архиве.';
GO
