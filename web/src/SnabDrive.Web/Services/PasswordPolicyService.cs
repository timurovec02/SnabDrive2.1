using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;

namespace SnabDrive.Web.Services;

/// <summary>Чтение, сохранение и проверка политики сложности паролей.</summary>
public interface IPasswordPolicyService
{
    /// <summary>Текущая политика (из БД; при недоступности БД — строгие значения по умолчанию).</summary>
    Task<PasswordPolicy> GetAsync(CancellationToken ct = default);

    Task SaveAsync(PasswordPolicy policy, string actor, CancellationToken ct = default);

    /// <summary>Список нарушений: пустой, если пароль подходит под политику.</summary>
    IReadOnlyList<string> Validate(string? password, PasswordPolicy policy);

    /// <summary>Текст требований для подсказки в интерфейсе.</summary>
    string Describe(PasswordPolicy policy);
}

/// <summary>
/// Политика хранится одной строкой в dbo.PasswordPolicies. Короткий кэш (30 с) снимает
/// запрос к БД с каждого валидатора пароля; после сохранения кэш сбрасывается.
/// </summary>
public sealed class PasswordPolicyService : IPasswordPolicyService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly object CacheGate = new();
    private static PasswordPolicy? _cached;
    private static DateTime _cachedAtUtc = DateTime.MinValue;

    private readonly IDbContextFactory<RegistryDbContext> _factory;

    public PasswordPolicyService(IDbContextFactory<RegistryDbContext> factory) => _factory = factory;

    public async Task<PasswordPolicy> GetAsync(CancellationToken ct = default)
    {
        lock (CacheGate)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
            {
                return Clone(_cached);
            }
        }

        PasswordPolicy? policy = null;
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            policy = await db.PasswordPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == 1, ct);
        }
        catch
        {
            // БД недоступна — действуем строгими значениями по умолчанию.
        }

        policy ??= new PasswordPolicy();

        lock (CacheGate)
        {
            _cached = Clone(policy);
            _cachedAtUtc = DateTime.UtcNow;
        }

        return policy;
    }

    public async Task SaveAsync(PasswordPolicy policy, string actor, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.PasswordPolicies.FirstOrDefaultAsync(p => p.Id == 1, ct);
        if (row is null)
        {
            row = new PasswordPolicy { Id = 1 };
            db.PasswordPolicies.Add(row);
        }

        row.MinLength = Math.Clamp(policy.MinLength, 6, 64);
        row.MinUniqueChars = Math.Clamp(policy.MinUniqueChars, 1, 10);
        row.RequireUppercase = policy.RequireUppercase;
        row.RequireLowercase = policy.RequireLowercase;
        row.RequireDigit = policy.RequireDigit;
        row.RequireSpecial = policy.RequireSpecial;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedBy = actor;

        await db.SaveChangesAsync(ct);

        lock (CacheGate)
        {
            _cached = Clone(row);
            _cachedAtUtc = DateTime.UtcNow;
        }
    }

    public IReadOnlyList<string> Validate(string? password, PasswordPolicy policy)
    {
        var errors = new List<string>();
        var pwd = password ?? string.Empty;

        if (pwd.Length < Math.Max(1, policy.MinLength))
        {
            errors.Add($"Пароль должен содержать не менее {policy.MinLength} символов (сейчас {pwd.Length}).");
        }

        if (pwd.Distinct().Count() < Math.Max(1, policy.MinUniqueChars))
        {
            errors.Add($"Пароль должен содержать не менее {policy.MinUniqueChars} различных символов.");
        }

        if (policy.RequireUppercase && !pwd.Any(char.IsUpper))
        {
            errors.Add("Добавьте хотя бы одну заглавную букву.");
        }

        if (policy.RequireLowercase && !pwd.Any(char.IsLower))
        {
            errors.Add("Добавьте хотя бы одну строчную букву.");
        }

        if (policy.RequireDigit && !pwd.Any(char.IsDigit))
        {
            errors.Add("Добавьте хотя бы одну цифру.");
        }

        if (policy.RequireSpecial && !pwd.Any(c => !char.IsLetterOrDigit(c)))
        {
            errors.Add("Добавьте хотя бы один спецсимвол (не букву и не цифру).");
        }

        return errors;
    }

    public string Describe(PasswordPolicy policy)
    {
        var parts = new List<string> { $"не менее {policy.MinLength} символов" };
        if (policy.MinUniqueChars > 1)
        {
            parts.Add($"не менее {policy.MinUniqueChars} различных");
        }

        if (policy.RequireUppercase)
        {
            parts.Add("заглавная буква");
        }

        if (policy.RequireLowercase)
        {
            parts.Add("строчная буква");
        }

        if (policy.RequireDigit)
        {
            parts.Add("цифра");
        }

        if (policy.RequireSpecial)
        {
            parts.Add("спецсимвол");
        }

        return string.Join(", ", parts);
    }

    private static PasswordPolicy Clone(PasswordPolicy p) => new()
    {
        Id = 1,
        MinLength = p.MinLength,
        MinUniqueChars = p.MinUniqueChars,
        RequireUppercase = p.RequireUppercase,
        RequireLowercase = p.RequireLowercase,
        RequireDigit = p.RequireDigit,
        RequireSpecial = p.RequireSpecial,
        UpdatedAtUtc = p.UpdatedAtUtc,
        UpdatedBy = p.UpdatedBy
    };
}
