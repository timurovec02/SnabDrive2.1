namespace SnabDrive.Web.Domain;

/// <summary>Кто выполняет изменение — передаётся явно из UI/API, чтобы сервисы были тестируемыми.</summary>
public sealed record ChangeActor(string UserId, string UserName)
{
    public static readonly ChangeActor System = new("system", "system");
}
