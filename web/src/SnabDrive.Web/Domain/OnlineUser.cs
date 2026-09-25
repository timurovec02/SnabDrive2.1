namespace SnabDrive.Web.Domain;

/// <summary>Пользователь, который сейчас работает в системе.</summary>
public sealed record OnlineUser(
    string UserId,
    string UserName,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    int SessionCount)
{
    public string Since => FirstSeenUtc.ToLocalTime().ToString("HH:mm");

    public string LastSeen => LastSeenUtc.ToLocalTime().ToString("HH:mm:ss");
}
