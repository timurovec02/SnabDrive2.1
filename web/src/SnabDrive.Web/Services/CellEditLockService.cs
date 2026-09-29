using System.Collections.Concurrent;

namespace SnabDrive.Web.Services;

/// <summary>Кто и какую ячейку реестра сейчас редактирует (инлайн, как в Excel).</summary>
public sealed record CellEditLock(int RecordId, string ColumnKey, string UserId, string UserName, DateTime SinceUtc)
{
    public string Key => $"{RecordId}|{ColumnKey}";
}

/// <summary>
/// Блокировки ячеек «как в Excel»: пока один пользователь редактирует ячейку, у остальных
/// она заблокирована и видно, кто её правит. Хранится в памяти (один сервер, ~10 пользователей).
///
/// Таймер бездействия: блокировка живёт, пока есть активность (ввод в ячейке). Если ничего
/// не меняется <see cref="IdleLimit"/> (2 минуты), блокировка снимается и ячейка открывается
/// для всех. Активность продлевает блокировку через <see cref="Touch"/>.
/// </summary>
public interface ICellEditLockService
{
    event Action? LocksChanged;

    IReadOnlyList<CellEditLock> GetActive();

    CellEditLock? Find(int recordId, string columnKey);

    /// <summary>true — заблокировали (или обновили свою блокировку); false — ячейку правит кто-то другой.</summary>
    bool TryAcquire(int recordId, string columnKey, string userId, string userName);

    /// <summary>Отметить активность (ввод) — продлевает блокировку ещё на 2 минуты.</summary>
    void Touch(int recordId, string columnKey, string userId);

    void Release(int recordId, string columnKey, string userId);

    void ReleaseAll(string userId);
}

public sealed class CellEditLockService : ICellEditLockService, IDisposable
{
    /// <summary>Сколько ячейка ждёт без активности, прежде чем открыться.</summary>
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan SweepPeriod = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, CellEditLock> _locks = new(StringComparer.Ordinal);

    private readonly Timer _sweepTimer;

    public CellEditLockService()
    {
        _sweepTimer = new Timer(_ => SweepAndNotify(), null, SweepPeriod, SweepPeriod);
    }

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

        if (DateTime.UtcNow - lockInfo.SinceUtc > IdleLimit)
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

    public void Touch(int recordId, string columnKey, string userId)
    {
        var key = Key(recordId, columnKey);

        // Продлеваем только свою блокировку; событие не дёргаем — остальным важно лишь
        // то, что ячейка занята, а не точное время последней активности.
        if (_locks.TryGetValue(key, out var existing) && existing.UserId == userId)
        {
            _locks[key] = existing with { SinceUtc = DateTime.UtcNow };
        }
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

    private void SweepAndNotify()
    {
        if (Purge() > 0)
        {
            LocksChanged?.Invoke();
        }
    }

    private int Purge()
    {
        var now = DateTime.UtcNow;
        var expired = _locks.Values.Where(x => now - x.SinceUtc > IdleLimit).Select(x => x.Key).ToList();

        foreach (var key in expired)
        {
            _locks.TryRemove(key, out _);
        }

        return expired.Count;
    }

    public void Dispose() => _sweepTimer.Dispose();
}
