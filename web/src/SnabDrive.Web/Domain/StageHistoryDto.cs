namespace SnabDrive.Web.Domain;

/// <summary>Один перевод закупки между стадиями из журнала передач.</summary>
/// <param name="AtUtc">Когда сделан перевод (UTC).</param>
/// <param name="ByUser">Кто перевёл — это же сотрудник закрыл этап <paramref name="FromStage"/>.</param>
/// <param name="FromStage">Стадия, с которой ушли.</param>
/// <param name="ToStage">Стадия, на которую перевели.</param>
/// <param name="Action">Подпись действия из журнала (например «Проблема с приемкой: …»).</param>
public sealed record StageHistoryEntryDto(
    DateTime AtUtc,
    string ByUser,
    int FromStage,
    int ToStage,
    string Action);

/// <summary>
/// История этапов по одной закупке: где она сейчас, кто ведёт текущий этап,
/// и все переводы между стадиями — по ним видно, кто и какую часть работы сделал.
/// </summary>
public sealed record StageHistoryDto(
    int Stage,
    string StageName,
    string? StageStartedBy,
    DateTime? StageStartedAtUtc,
    string? StageCompletedBy,
    DateTime? StageCompletedAtUtc,
    IReadOnlyList<StageHistoryEntryDto> Entries);
