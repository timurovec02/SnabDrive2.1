namespace SnabDrive.Web.Domain;

/// <summary>
/// Кто выполняет изменение. Передаётся явно из UI/API, чтобы сервисы оставались тестируемыми.
/// <see cref="Access"/> — эффективные права на колонки: по их умолчанию нет ограничений.
/// </summary>
public sealed record ChangeActor(string UserId, string UserName)
{
    public static readonly ChangeActor System = new("system", "system");

    public ColumnAccessMap Access { get; init; } = ColumnAccessMap.Full;
}
