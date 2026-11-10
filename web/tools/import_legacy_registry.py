#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Разбор старой выгрузки реестра закупок (Excel → CSV) под сайт Снабдрайв.

Скрипт делает то же самое, что страница «Импорт реестра» в сайте
(web/src/SnabDrive.Web/Services/LegacyImportService.cs), только на Python:
читает файл, раскладывает колонки по полям dbo.RegistryItems и печатает
подробный отчёт — что куда легло, что не подошло и почему.

Нужен, когда залить данные требуется без сборки и публикации сайта:
режим --sql строит готовый скрипт для SSMS (идемпотентный — повторный
запуск не создаёт дублей).

Запуск:
    python3 import_legacy_registry.py "Реестр закупок_2025.csv"            # отчёт
    python3 import_legacy_registry.py "Реестр закупок_2025.csv" --sql out.sql
"""

from __future__ import annotations

import argparse
import csv
import io
import re
import sys
from collections import Counter

# ---------------------------------------------------------------------------
# Соответствие колонок файла полям сайта
# ---------------------------------------------------------------------------
# Берём только колонки A..Q — так задано. R «Статус исполнения контракта» и
# колонки без заголовка (S..W — рабочие пометки в несколько строк) не трогаем.
FIRST_COLUMN = 0          # A — ссылка на закупку или текст площадки
LAST_COLUMN = 16          # Q — «Примечание»

# Ключ — заголовок, приведённый к нижнему регистру без знаков препинания.
COLUMNS = {
    "заказчик":                          "Customer",
    "типзакупкизакон":                   "TypeText",
    "типзакупки":                        "TypeText",
    "местопоставкитовара":               "PlaceOfDelivery",
    "местопоставки":                     "PlaceOfDelivery",
    "номеррезерва":                      "ReserveNumber",
    "нацрежимограничениепогодам":        "NationalMode",
    "нацрежим":                          "NationalMode",
    "статусрезервав2в":                  "B2BText",
    "статусрезерваb2b":                  "B2BText",
    "датапередачинаразмещение":          "DateOfTransferForPlacement",
    "датаразмещениянаплощадке":          "DateOfPlacement",
    "дататоргов":                        "BiddingDate",
    "датаподведенияитогов":              "DateResults",
    "нмцк":                              "NMCK",
    "нашаминимальнаясумма":              "MinPrice",
    "итоговаясумма":                     "ResultPrice",
    "датазаключенияконтракта":           "DateOfConclusionOfTheContract",
    "срокпоставки":                      "DeliveryTerms",
    "примечание":                        "Note",
    "примечания":                        "Note",
}

# Заголовки колонок в отчёте (для человека).
FIELD_TITLES = {
    "Customer": "Заказчик",
    "TypeText": "Тип закупки (справочник)",
    "PlaceOfDelivery": "Место поставки товара",
    "ReserveNumber": "Номер резерва",
    "NationalMode": "Нац режим",
    "B2BText": "Статус резерва B2B",
    "DateOfTransferForPlacement": "Дата передачи на размещение",
    "DateOfPlacement": "Дата размещения на площадке",
    "BiddingDate": "Дата торгов",
    "DateResults": "Дата подведения итогов",
    "NMCK": "НМЦК",
    "MinPrice": "Наша минимальная сумма",
    "ResultPrice": "Итоговая сумма",
    "DateOfConclusionOfTheContract": "Дата заключения контракта",
    "DeliveryTerms": "Сроки поставки",
    "Note": "Примечание",
    "SourceRef": "Ссылка / номер закупки",
}

# Сокращения, которые нельзя «схлопывать» в строчные при приведении к обычному виду.
ABBREVIATIONS = {
    "ООО", "АО", "ПАО", "ОАО", "ИП", "ФГУП", "ГУП", "МУП", "ГБУ", "ГБУЗ", "МБУ",
    "МБУЗ", "МБОУ", "ГБОУ", "ФГБУ", "ФГБОУ", "ГКУ", "МКУ", "МКУП", "ФКУ", "ФГКУ",
    "ЦРБ", "РЖД", "НЭСК", "ГРЧЦ", "ЕИС", "ГОЗ", "РФ", "СНГ", "УВО", "БГ", "НДС",
}

# Стадии воркфлоу (WorkflowStages.All в Services/WorkflowService.cs).
STAGES = {
    1: "1. Контур и подготовка",
    3: "3. Подача заявки на площадку",
    4: "4. Торги",
    6: "6. Контракт",
    8: "8. Ожидание доставки",
    11: "Завершена",
}

DATE_RE = re.compile(r"\b(\d{1,2})\.(\d{1,2})\.(\d{4})\b")
ISO_RE = re.compile(r"\b(\d{4})-(\d{1,2})-(\d{1,2})\b")
TIME_RE = re.compile(r"[^\d]{0,6}(\d{1,2})[:.](\d{2})")
NUMBER_RE = re.compile(r"\d{1,3}(?:[\s\u00a0\u202f]\d{3})+(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?")
URL_RE = re.compile(r"https?://\S+|www\.\S+", re.IGNORECASE)


# ---------------------------------------------------------------------------
# Мелкие помощники
# ---------------------------------------------------------------------------
def normalize_header(value: str) -> str:
    """Заголовок к виду, по которому ищем соответствие: без пробелов и знаков."""
    text = (value or "").lower().replace("ё", "е")
    return "".join(ch for ch in text if ch.isalnum())


def clean_text(value: str, limit: int) -> str:
    """Текст из ячейки: единые переводы строк, без хвостовых пробелов, по длине колонки."""
    text = (value or "").replace("\r\n", "\n").replace("\r", "\n")
    text = re.sub(r"[ \t]+\n", "\n", text)
    text = re.sub(r"\n{3,}", "\n\n", text).strip()
    if len(text) > limit:
        text = text[: limit - 1].rstrip() + "…"
    return text


def sentence_case(text: str) -> str:
    """ВСЕ ЗАГЛАВНЫЕ → Обычный вид; сокращения (ООО, ГБУЗ, ЦРБ…) остаются заглавными."""
    if not text:
        return text
    letters = [ch for ch in text if ch.isalpha()]
    if not letters:
        return text
    upper = sum(1 for ch in letters if ch.isupper())
    if upper / len(letters) < 0.85:
        return text          # уже набрано в обычном регистре — не трогаем

    text = text.lower()

    def fix(word: str) -> str:
        stripped = word.strip().strip('«»".,;:')
        if stripped.upper() in ABBREVIATIONS and len(stripped) <= 6:
            return word.replace(stripped, stripped.upper())
        return word

    words = [fix(word) for word in text.split(" ")]
    text = " ".join(words)
    # Поднимаем только первую букву — по-русски остальные слова пишутся строчными.
    return text[:1].upper() + text[1:]


def short_customer(customer: str, limit: int = 140) -> str:
    """Читаемое имя заказчика для «Наименования»: название в кавычках, если есть."""
    text = clean_text(customer, 1000).replace("\n", " ")
    match = re.search(r"[«\"]([^«»\"]{6,})[»\"]", text)
    base = match.group(1).strip() if match else text
    base = sentence_case(base).strip()
    if len(base) > limit:
        cut = base[:limit].rsplit(" ", 1)[0]
        base = cut.rstrip(" ,;:-") + "…"
    return base or text[:limit]


def parse_date(value: str):
    """Дата из свободной ячейки: «подписан 05.11.2025», «2.10.2025 в 12.00».

    Возвращает (дата со временем или None, остаток текста без даты).
    """
    text = (value or "").strip()
    if not text:
        return None, ""

    moment = None
    matched = ""
    match = DATE_RE.search(text)
    if match:
        day, month, year = int(match.group(1)), int(match.group(2)), int(match.group(3))
        matched = match.group(0)
    else:
        iso = ISO_RE.search(text)
        if iso:
            year, month, day = int(iso.group(1)), int(iso.group(2)), int(iso.group(3))
            matched = iso.group(0)
        else:
            return None, text

    if not (1 <= month <= 12 and 1 <= day <= 31 and 1990 <= year <= 2100):
        return None, text

    moment = "%04d-%02d-%02d" % (year, month, day)

    # Время ищем сразу за датой — «Дата торгов» в сайте хранится с временем.
    tail = text[match.end() if match else ISO_RE.search(text).end():]
    time = TIME_RE.match(tail)
    hour = minute = 0
    if time:
        hour, minute = int(time.group(1)), int(time.group(2))
        if hour <= 23 and minute <= 59:
            moment += "T%02d:%02d:00" % (hour, minute)
            tail = tail[time.end():]
        else:
            hour = minute = 0

    leftover = (text[: match.start() if match else ISO_RE.search(text).start()] + " " + tail)
    leftover = re.sub(r"\s+", " ", leftover).strip(" \t,;:-—")
    # «подписан», «в 12.00», «заявка» — служебные слова, датой они не являются.
    leftover = re.sub(r"\b(подписан\w*|заявк\w*|в)\b", " ", leftover, flags=re.IGNORECASE)
    leftover = re.sub(r"\s+", " ", leftover).strip(" \t,;:-—")
    return moment, leftover


def parse_money(value: str):
    """Сумма из свободной ячейки: «380000швв», «2 545 178,02», «990 000/ 61042»."""
    text = (value or "").replace("\u00a0", " ").replace("\u202f", " ").strip()
    if not text:
        return None, False
    call = "звон" in text.lower()          # «звони», «звонить» → отметка «Звони»

    for match in NUMBER_RE.finditer(text):
        raw = match.group(0).replace(" ", "").replace(",", ".")
        try:
            number = float(raw)
        except ValueError:
            continue
        # «строго 2026!» — это год, а не сумма.
        if "." not in raw and 1990 <= number <= 2100:
            continue
        return round(number, 2), call
    return None, call


def extract_number(reference: str):
    """Номер закупки из ссылки: regNumber, purchaseNoticeNumber, id площадки."""
    text = (reference or "").strip()
    if not text:
        return None
    for pattern in (r"[?&](?:regNumber|purchaseNoticeNumber)=(\d{6,})",
                    r"[?&]noticeInfoId=(\d{6,})",
                    r"[?&]noticeGuid=([0-9a-fA-F-]{8,})",
                    r"[?&](?:id|sell|procedures)=(\d{6,})"):
        match = re.search(pattern, text)
        if match:
            return match.group(1)
    tail = re.findall(r"/(\d{6,})", text)
    return tail[-1] if tail else None


def derive_stage(row: dict) -> int:
    """Стадия по данным файла: чем дальше зашла закупка, тем позже этап."""
    if row["DateOfConclusionOfTheContract"]:
        return 8            # контракт подписан → «8. Ожидание доставки»
    if row["ResultPrice"]:
        return 6            # есть итог → «6. Контракт»
    if row["BiddingDate"]:
        return 4            # торги назначены → «4. Торги»
    if row["DateOfPlacement"]:
        return 3            # размещена на площадке → «3. Подача заявки»
    if row["IsApproved"]:
        return 3            # «Согласован» — как в сайте: отметка ведёт на этап 3
    return 1                # «1. Контур и подготовка»


# ---------------------------------------------------------------------------
# Разбор файла
# ---------------------------------------------------------------------------
def read_text(path: str) -> str:
    """Читает файл: UTF-8 (с BOM и без), UTF-16, иначе Windows-1251 (Excel «CSV»)."""
    raw = open(path, "rb").read()
    for encoding in ("utf-8-sig", "utf-16"):
        try:
            return raw.decode(encoding)
        except (UnicodeDecodeError, UnicodeError):
            continue
    return raw.decode("cp1251", errors="replace")


def split_rows(text: str):
    """Разбивает CSV, сам определяет разделитель (запятая у Excel EN, точка с запятой у RU)."""
    lines = [line for line in text.splitlines() if line.strip()]
    if not lines:
        return []
    first = next(csv.reader([lines[0]]))
    if len(first) < 3:
        sample = [line for line in lines[:6]]
        if max(len(r) for r in csv.reader(sample, delimiter=";")) > max(len(r) for r in csv.reader(sample)):
            return list(csv.reader(io.StringIO(text), delimiter=";"))
    return list(csv.reader(io.StringIO(text)))


def find_header(rows):
    """Строка заголовков — та, где есть «Заказчик» и «НМЦК» (смотрим первые 6 строк)."""
    for index, row in enumerate(rows[:6]):
        keys = {normalize_header(cell) for cell in row}
        if "заказчик" in keys and any(k.startswith("нмцк") for k in keys):
            return index
    raise SystemExit("Не найдена строка заголовков («Заказчик», «НМЦК») в первых шести строках.")


def parse(path: str, types: dict, b2b: dict):
    """Разбирает файл. types/b2b — справочники сайта {имя: id} (пустые = ничего не найдём)."""
    rows = split_rows(read_text(path))
    header_index = find_header(rows)
    header = rows[header_index]

    mapping, ignored = {}, []
    for index, title in enumerate(header):
        if index > LAST_COLUMN:
            if title.strip():
                ignored.append("%s (за пределами A..Q)" % chr(ord("A") + index))
            continue
        key = normalize_header(title)
        if index == FIRST_COLUMN:
            mapping[index] = "SourceRef"
        elif key in COLUMNS:
            mapping[index] = COLUMNS[key]
        elif title.strip():
            ignored.append(title.strip())
    for index in range(len(header), LAST_COLUMN + 1):
        ignored.append("колонка %s — в файле нет" % chr(ord("A") + index))

    items, warnings = [], Counter()
    unknown_types, unknown_b2b = Counter(), Counter()
    leftover_notes = Counter()

    for number, raw in enumerate(rows[header_index + 1:], header_index + 2):
        if not any(cell.strip() for cell in raw):
            continue

        cells = {}
        for index, field in mapping.items():
            cells[field] = raw[index].strip() if index < len(raw) else ""

        item = {
            "SourceRow": number,
            "Customer": clean_text(cells.get("Customer", ""), 2000),
            "PlaceOfDelivery": clean_text(cells.get("PlaceOfDelivery", ""), 1000),
            "ReserveNumber": clean_text(cells.get("ReserveNumber", ""), 1000),
            "NationalMode": clean_text(cells.get("NationalMode", ""), 1000),
            "DeliveryTerms": clean_text(cells.get("DeliveryTerms", ""), 500),
            "EisLink": "",
            "PurchaseNumber": "",
            "NameLink": "",
            "TypeOfPurchaseId": None,
            "TypeText": "",
            "B2BStatusId": None,
            "B2BText": "",
            "IsApproved": False,
            "IsCall": False,
            "IsByRequest": False,
            "ContractStatusId": None,
            "Description": "",
            "Note": "",
            "Stage": 1,
            "Problems": [],
        }

        # --- A: ссылка на закупку или текст площадки -------------------------
        reference = clean_text(cells.get("SourceRef", ""), 1000)
        url = URL_RE.search(reference.replace("\n", " "))
        if url:
            item["EisLink"] = url.group(0).strip()[:1000]
            item["PurchaseNumber"] = extract_number(url.group(0)) or ""
        platform = "" if url else reference

        # --- тип закупки -----------------------------------------------------
        type_text = clean_text(cells.get("TypeText", ""), 200)
        item["TypeText"] = type_text
        if type_text:
            item["TypeOfPurchaseId"] = types.get(type_text.lower())
            if item["TypeOfPurchaseId"] is None:
                unknown_types[type_text] += 1
                warnings["тип закупки не найден в справочнике"] += 1
        else:
            warnings["тип закупки не указан"] += 1

        # --- статус резерва B2B ----------------------------------------------
        status_text = clean_text(cells.get("B2BText", ""), 200)
        item["B2BText"] = status_text
        if status_text:
            item["B2BStatusId"] = b2b.get(status_text.lower())
            if item["B2BStatusId"] is None:
                unknown_b2b[status_text] += 1
                warnings["статус B2B не найден в справочнике"] += 1
            if status_text.lower() == "согласован":
                item["IsApproved"] = True    # в сайте «Согласован» — своя отметка

        # --- даты ------------------------------------------------------------
        for field in ("DateOfTransferForPlacement", "DateOfPlacement",
                      "BiddingDate", "DateResults", "DateOfConclusionOfTheContract"):
            moment, leftover = parse_date(cells.get(field, ""))
            item[field] = moment
            if moment is None and cells.get(field, "").strip():
                warnings["дата не распознана"] += 1
                item["Problems"].append("%s: %s" % (FIELD_TITLES[field], cells[field][:120]))
            if leftover:
                # Текст вокруг даты («торгов не будет, мы единственные») не теряем.
                item.setdefault("_extra", []).append("%s: %s" % (FIELD_TITLES[field], leftover[:200]))
                leftover_notes[FIELD_TITLES[field]] += 1

        # --- суммы -----------------------------------------------------------
        for field in ("NMCK", "MinPrice", "ResultPrice"):
            amount, call = parse_money(cells.get(field, ""))
            item[field] = amount
            if amount is None and cells.get(field, "").strip():
                warnings["сумма не распознана"] += 1
                item["Problems"].append("%s: %s" % (FIELD_TITLES[field], cells[field][:120]))
            item["IsCall"] = item["IsCall"] or (call and field == "MinPrice")
        if any("зна" in cells.get(f, "").lower() for f in ("NMCK", "MinPrice", "ResultPrice")):
            item["IsCall"] = True

        # --- срок поставки → «Сроки доставки», «по заявкам» -------------------
        if "по заявк" in item["DeliveryTerms"].lower():
            item["IsByRequest"] = True

        # --- контракт --------------------------------------------------------
        contract_text = cells.get("DateOfConclusionOfTheContract", "").lower()
        if "подписан" in contract_text or "скан" in contract_text:
            item["ContractStatusId"] = 3     # «Подписан»

        # --- примечание + всё, чему не нашлось поля --------------------------
        note = clean_text(cells.get("Note", ""), 4000)
        extra = item.pop("_extra", [])
        parts = [part for part in ([note] + extra) if part]
        combined = "\n".join(parts)
        item["Description"] = combined
        item["Note"] = combined

        # --- наименование: заказчик + тип закупки -----------------------------
        kind = type_text or platform or "Закупка"
        base = short_customer(item["Customer"]) or "Закупка без заказчика"
        item["NameLink"] = clean_text("%s — %s" % (base, kind), 1000)

        if not item["Customer"]:
            item["Problems"].append("нет заказчика")
        if not item["PurchaseNumber"]:
            warnings["нет номера закупки (дубль не проверить)"] += 1

        item["Stage"] = derive_stage(item)
        items.append(item)

    report = {
        "ignored": ignored,
        "warnings": warnings,
        "unknown_types": unknown_types,
        "unknown_b2b": unknown_b2b,
        "leftover_notes": leftover_notes,
    }
    return items, report, mapping


# ---------------------------------------------------------------------------
# Отчёт и SQL
# ---------------------------------------------------------------------------
def print_report(items, report, mapping, path):
    total = len(items)
    print("=" * 78)
    print("Файл: %s" % path)
    print("Строк с данными: %d" % total)
    print("=" * 78)

    print("\nКуда легли колонки A..Q:")
    filled = Counter()
    for item in items:
        for field in ("Customer", "PlaceOfDelivery", "ReserveNumber", "NationalMode",
                      "DeliveryTerms", "EisLink", "PurchaseNumber", "TypeText",
                      "TypeOfPurchaseId", "B2BText",
                      "B2BStatusId", "IsApproved", "IsCall", "IsByRequest",
                      "ContractStatusId", "Description",
                      "DateOfTransferForPlacement", "DateOfPlacement", "BiddingDate",
                      "DateResults", "DateOfConclusionOfTheContract",
                      "NMCK", "MinPrice", "ResultPrice"):
            if item[field] not in (None, "", False):
                filled[field] += 1
    order = ["Customer", "SourceRef", "TypeText", "PlaceOfDelivery", "ReserveNumber",
             "NationalMode", "B2BText", "DateOfTransferForPlacement", "DateOfPlacement",
             "BiddingDate", "DateResults", "NMCK", "MinPrice", "ResultPrice",
             "DateOfConclusionOfTheContract", "DeliveryTerms", "Note"]
    for field in order:
        if field == "SourceRef":
            print("   %-34s ссылка в «ЕИС»: %d, номер закупки: %d"
                  % ("A — ссылка / площадка", filled["EisLink"], filled["PurchaseNumber"]))
            continue
        if field == "TypeText":
            print("   %-34s значение: %d, связано со справочником: %d"
                  % (FIELD_TITLES[field], filled["TypeText"], filled["TypeOfPurchaseId"]))
            continue
        if field == "B2BText":
            print("   %-34s значение: %d, связано со справочником: %d"
                  % (FIELD_TITLES[field], filled["B2BText"], filled["B2BStatusId"]))
            continue
        target = {"Note": "Description"}.get(field, field)
        title = FIELD_TITLES.get(field, field)
        print("   %-34s %3d из %d" % (title, filled[target], total))

    print("\nПроизводные поля:")
    for field, title in [("IsApproved", "«Согласован»"), ("IsCall", "«Звони»"),
                         ("IsByRequest", "«По заявкам»"), ("ContractStatusId", "Статус контракта «Подписан»")]:
        print("   %-34s %3d" % (title, filled[field]))

    print("\nСтадии, на которые встанут закупки:")
    for stage, count in sorted(Counter(item["Stage"] for item in items).items()):
        print("   %-34s %3d" % (STAGES.get(stage, stage), count))

    if report["ignored"]:
        print("\nНе импортируется (нет поля в сайте или за пределами A..Q):")
        for title in report["ignored"]:
            print("   — %s" % title)

    if report["unknown_types"]:
        print("\nЗначения, которых нет в справочнике «Тип закупки» (поле останется пустым):")
        for value, count in report["unknown_types"].most_common():
            print("   — %-24s %d" % (value, count))
    if report["unknown_b2b"]:
        print("\nЗначения, которых нет в справочнике «Статус резерва B2B»:")
        for value, count in report["unknown_b2b"].most_common():
            print("   — %-24s %d" % (value, count))

    if report["warnings"]:
        print("\nПредупреждения разбора:")
        for value, count in report["warnings"].most_common():
            print("   — %-42s %d" % (value, count))

    problems = [item for item in items if item["Problems"]]
    if problems:
        print("\nЯчейки, которые не удалось разобрать (%d строк):" % len(problems))
        for item in problems[:40]:
            print("   строка %s: %s" % (item["SourceRow"], "; ".join(item["Problems"])[:150]))

    print("\nПример первых пяти записей:")
    for item in items[:5]:
        print("   • %s" % item["NameLink"][:92])
        print("     стадия: %s | НМЦК %s | итог %s | номер %s"
              % (STAGES.get(item["Stage"]), item["NMCK"], item["ResultPrice"],
                 item["PurchaseNumber"] or "—"))


def sql_literal(value):
    if value is None:
        return "NULL"
    return "N'" + str(value).replace("'", "''") + "'"


def sql_number(value):
    return "NULL" if value is None else ("%.2f" % value)


def sql_date(value):
    return "NULL" if not value else "CAST(N'%s' AS DATETIME2(7))" % value


def build_sql(items, path):
    """Идемпотентный скрипт для SSMS: дубли по номеру закупки не вставляются."""
    out = io.StringIO()
    w = out.write
    w("""/* ============================================================================
   Импорт старой выгрузки реестра в dbo.RegistryItems.
   Источник: %s
   Скрипт построен web/tools/import_legacy_registry.py (правила разбора — там же).

   Повторный запуск безопасен: строка не вставляется, если в реестре уже есть
   закупка с тем же номером (или тот же заказчик + НМЦК + дата размещения,
   если номера в файле не было).

   Перед запуском должен быть выполнен web/sql/updates_2026.sql —
   там появляется колонка DeliveryTerms («Сроки доставки»).
   ========================================================================== */

USE [snabdrive_db];
GO

IF COL_LENGTH('dbo.RegistryItems', 'DeliveryTerms') IS NULL
BEGIN
    RAISERROR(N'Сначала выполните web/sql/updates_2026.sql — нет колонки DeliveryTerms.', 16, 1);
    SET NOEXEC ON;
END
GO

SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#import') IS NOT NULL DROP TABLE #import;
CREATE TABLE #import
(
    RowNo          INT,
    NameLink       NVARCHAR(1000),
    EisLink        NVARCHAR(1000) NULL,
    PurchaseNumber NVARCHAR(200)  NULL,
    Customer       NVARCHAR(MAX)  NULL,
    PlaceOfDelivery NVARCHAR(1000) NULL,
    ReserveNumber  NVARCHAR(1000) NULL,
    NationalMode   NVARCHAR(1000) NULL,
    DeliveryTerms  NVARCHAR(500)  NULL,
    Description    NVARCHAR(MAX)  NULL,
    Note           NVARCHAR(MAX)  NULL,
    DateOfTransferForPlacement    DATETIME2(7) NULL,
    DateOfPlacement               DATETIME2(7) NULL,
    BiddingDate                   DATETIME2(7) NULL,
    DateResults                   DATETIME2(7) NULL,
    DateOfConclusionOfTheContract DATETIME2(7) NULL,
    NMCK           DECIMAL(18,2) NULL,
    MinPrice       DECIMAL(18,2) NULL,
    ResultPrice    DECIMAL(18,2) NULL,
    TypeText       NVARCHAR(200) NULL,
    B2BText        NVARCHAR(200) NULL,
    IsApproved     BIT NOT NULL DEFAULT (0),
    IsCall         BIT NOT NULL DEFAULT (0),
    IsByRequest    BIT NOT NULL DEFAULT (0),
    ContractStatusId INT NULL,
    Stage          INT NOT NULL
);
GO

""" % path.replace("*/", "* /"))

    w("INSERT INTO #import VALUES\n")
    body = []
    for item in items:
        body.append("(%d, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %d, %d, %d, %s, %d)" % (
            item["SourceRow"],
            sql_literal(item["NameLink"]),
            sql_literal(item["EisLink"] or None),
            sql_literal(item["PurchaseNumber"] or None),
            sql_literal(item["Customer"]),
            sql_literal(item["PlaceOfDelivery"]),
            sql_literal(item["ReserveNumber"]),
            sql_literal(item["NationalMode"]),
            sql_literal(item["DeliveryTerms"] or None),
            sql_literal(item["Description"] or None),
            sql_literal(item["Note"] or None),
            sql_date(item["DateOfTransferForPlacement"]),
            sql_date(item["DateOfPlacement"]),
            sql_date(item["BiddingDate"]),
            sql_date(item["DateResults"]),
            sql_date(item["DateOfConclusionOfTheContract"]),
            sql_number(item["NMCK"]),
            sql_number(item["MinPrice"]),
            sql_number(item["ResultPrice"]),
            sql_literal(item["TypeText"] or None),
            sql_literal(item["B2BText"] or None),
            1 if item["IsApproved"] else 0,
            1 if item["IsCall"] else 0,
            1 if item["IsByRequest"] else 0,
            sql_number(item["ContractStatusId"]) if item["ContractStatusId"] else "NULL",
            item["Stage"],
        ))
    w(",\n".join(body))
    w(";\nGO\n\n")

    w("""DECLARE @before INT = (SELECT COUNT(*) FROM dbo.RegistryItems);

INSERT INTO dbo.RegistryItems
(
    NameLink, EisLink, PurchaseNumber, Customer, PlaceOfDelivery, ReserveNumber, NationalMode,
    DeliveryTerms, Description, Note,
    DateOfTransferForPlacement, DateOfPlacement, BiddingDate, DateResults, DateOfConclusionOfTheContract,
    NMCK, MinPrice, ResultPrice,
    TypeOfPurchaseId, B2BStatusId, IsApproved, IsCall, IsByRequest, ContractStatusId,
    Stage, StageStartedAtUtc, StageStartedBy
)
SELECT
    i.NameLink, i.EisLink, ISNULL(i.PurchaseNumber, ''), i.Customer, i.PlaceOfDelivery, i.ReserveNumber, i.NationalMode,
    i.DeliveryTerms, i.Description, i.Note,
    i.DateOfTransferForPlacement, i.DateOfPlacement, i.BiddingDate, i.DateResults, i.DateOfConclusionOfTheContract,
    ISNULL(i.NMCK, 0), ISNULL(i.MinPrice, 0), ISNULL(i.ResultPrice, 0),
    /* Справочники не трогаем: связываем только с тем, что уже есть в базе по названию. */
    (SELECT TOP 1 t.ID FROM dbo.PurchaseTypes t WHERE LTRIM(RTRIM(t.NameOfPurchase)) = LTRIM(RTRIM(i.TypeText))),
    (SELECT TOP 1 b.ID FROM dbo.B2BStatuses   b WHERE LTRIM(RTRIM(b.NameB2B))        = LTRIM(RTRIM(i.B2BText))),
    i.IsApproved, i.IsCall, i.IsByRequest, i.ContractStatusId,
    i.Stage, SYSUTCDATETIME(), N'Импорт CSV'
FROM #import i
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.RegistryItems r
    WHERE (ISNULL(i.PurchaseNumber, '') <> '' AND r.PurchaseNumber = i.PurchaseNumber)
       OR (ISNULL(i.PurchaseNumber, '') = ''
           AND ISNULL(r.Customer, '') = ISNULL(i.Customer, '')
           AND ISNULL(r.NMCK, 0) = ISNULL(i.NMCK, 0)
           AND ISNULL(r.DateOfPlacement, '1900-01-01') = ISNULL(i.DateOfPlacement, '1900-01-01'))
);

DECLARE @added INT = (SELECT COUNT(*) FROM dbo.RegistryItems) - @before;

PRINT N'В реестре было записей: ' + CAST(@before AS NVARCHAR(20));
PRINT N'Добавлено из файла:     ' + CAST(@added AS NVARCHAR(20));
PRINT N'Пропущено дублей:       ' + CAST((SELECT COUNT(*) FROM #import) - @added AS NVARCHAR(20));

SELECT i.RowNo AS [Строка файла], i.NameLink AS [Наименование], i.PurchaseNumber AS [Номер],
       i.NMCK, i.ResultPrice, i.Stage AS [Стадия],
       CASE WHEN EXISTS
       (
           SELECT 1 FROM dbo.RegistryItems r
           WHERE (ISNULL(i.PurchaseNumber, '') <> '' AND r.PurchaseNumber = i.PurchaseNumber)
              OR (ISNULL(i.PurchaseNumber, '') = ''
                  AND ISNULL(r.Customer, '') = ISNULL(i.Customer, '')
                  AND ISNULL(r.NMCK, 0) = ISNULL(i.NMCK, 0)
                  AND ISNULL(r.DateOfPlacement, '1900-01-01') = ISNULL(i.DateOfPlacement, '1900-01-01'))
       ) THEN N'да' ELSE N'нет' END AS [Дубль в реестре]
FROM #import i
ORDER BY i.RowNo;

DROP TABLE #import;
GO

SET NOEXEC OFF;
GO
""")
    return out.getvalue()


def main():
    parser = argparse.ArgumentParser(description="Разбор старой выгрузки реестра под сайт")
    parser.add_argument("csv", help="файл выгрузки (CSV из Excel)")
    parser.add_argument("--sql", help="куда сохранить скрипт для SSMS")
    parser.add_argument("--types", help="CSV со справочником «Тип закупки»: id;название")
    parser.add_argument("--b2b", help="CSV со справочником «Статус резерва B2B»: id;название")
    args = parser.parse_args()

    def load_dict(path):
        if not path:
            return {}
        result = {}
        for row in csv.reader(io.open(path, encoding="utf-8-sig")):
            if len(row) >= 2 and row[0].strip().isdigit():
                result[row[1].strip().lower()] = int(row[0])
        return result

    items, report, mapping = parse(args.csv, load_dict(args.types), load_dict(args.b2b))
    print_report(items, report, mapping, args.csv)

    if args.sql:
        # BOM обязателен: без него SSMS читает файл в системной кодировке и кириллица плывёт.
        with io.open(args.sql, "w", encoding="utf-8-sig", newline="\r\n") as handle:
            handle.write(build_sql(items, args.csv))
        print("\nСкрипт для SSMS сохранён: %s (%d записей)" % (args.sql, len(items)))


if __name__ == "__main__":
    main()
