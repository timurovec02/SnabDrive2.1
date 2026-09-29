using System.Collections.Concurrent;

namespace SnabDrive.Web.Services;

/// <summary>Кто и какую ячейку реестра сейчас редактирует (инлайн, как в Excel).</summary>
public sealed record CellEditLock(int RecordId, string ColumnKey, string UserId, string UserName, DateTime SinceUtc)
{
    public string Key => $"{RecordId}|{ColumnKey}";
}

/// <summary>
/// Короткоживущие блокировки ячеек «как в Excel»: пока один пользователь редактирует
/// ячейку, у остальных она заблокирована и видно, кто её правит. Хранится в памяти —
/// для одного сервера и ~10 одновременных пользователей этого достаточно.
/// Блокировка живёт не дольше <see cref="Ttl"/> и снимается при сохранении/отмене/выходе.
/// </summary>
public interface ICellEditLockService
{
    event Action? LocksChanged;

    IReadOnlyList<CellEditLock> GetActive();

    CellEditLock? Find(int recordId, string columnKey);

    /// <summary>true — заблокировали (или обновили свою блокировку); false — ячейку правит кто-то другой.</summary>
    bool TryAcquire(int recordId, string columnKey, string userId, string userName);

    void Release(int recordId, string columnKey, string userId);

    void ReleaseAll(string userId);
}

public sealed class CellEditLockService : ICellEditLockService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    private readonly ConcurrentDictionary<string, CellEditLock> _locks = new(StringComparer.Ordinal);

    public event Action? LocksChanged;

    private static string Key(int recordId, string columnKey) => $"{recordId}|{columnKey}";

    public IReadOnlyList<CellEditLock> GetActive()
    {
        Purge();
        return _locks.Values.OrderBy(x => x.SinceUtc).ToList();
    }

    public CellEditLock? Find(int recordId, string columnKey)
    {
        var key = Key(recordId, columnKey);

        if (!_locks.TryGetValue(key, out var lockInfo))
        {
            return null;
        }

        if (DateTime.UtcNow - lockInfo.SinceUtc > Ttl)
        {
            _locks.TryRemove(key, out _);
            return null;
        }

        return lockInfo;
    }

    public bool TryAcquire(int recordId, string columnKey, string userId, string userName)
    {
        Purge();

        var key = Key(recordId, columnKey);

        if (_locks.TryGetValue(key, out var existing) && existing.UserId != userId)
        {
            return false;
        }

        _locks[key] = new CellEditLock(recordId, columnKey, userId, userName, DateTime.UtcNow);
        LocksChanged?.Invoke();
        return true;
    }

    public void Release(int recordId, string columnKey, string userId)
    {
        var key = Key(recordId, columnKey);

        if (_locks.TryGetValue(key, out var existing) && existing.UserId == userId)
        {
            _locks.TryRemove(key, out _);
            LocksChanged?.Invoke();
        }
    }

    public void ReleaseAll(string userId)
    {
        var keys = _locks.Values.Where(x => x.UserId == userId).Select(x => x.Key).ToList();

        if (keys.Count == 0)
        {
            return;
        }

        foreach (var key in keys)
        {
            _locks.TryRemove(key, out _);
        }

        LocksChanged?.Invoke();
    }

    private void Purge()
    {
        var now = DateTime.UtcNow;
        var expired = _locks.Values.Where(x => now - x.SinceUtc > Ttl).Select(x => x.Key).ToList();

        foreach (var key in expired)
        {
            _locks.TryRemove(key, out _);
        }
    }
}
