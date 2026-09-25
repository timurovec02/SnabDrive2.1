using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

public interface IAuditService
{
    /// <summary>Записать событие аудита. Ошибки журналирования не должны ронять основную операцию.</summary>
    Task WriteAsync(string action, string entityName, int entityId, ChangeActor actor,
                    IDictionary<string, FieldChange>? changes = null, string? summary = null,
                    CancellationToken cancellationToken = default);

    Task<PagedResult<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}

public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IDbContextFactory<AppIdentityDbContext> _factory;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IDbContextFactory<AppIdentityDbContext> factory, ILogger<AuditService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task WriteAsync(string action, string entityName, int entityId, ChangeActor actor,
        IDictionary<string, FieldChange>? changes = null, string? summary = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken);

            var entry = new AuditLogEntry
            {
                CreatedAtUtc = DateTime.UtcNow,
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                UserId = actor.UserId,
                UserName = actor.UserName,
                Summary = Truncate(summary, 500),
                ChangesJson = changes is { Count: > 0 } ? JsonSerializer.Serialize(changes, JsonOptions) : null
            };

            context.AuditLog.Add(entry);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось записать событие аудита {Action} {Entity}#{Id}", action, entityName, entityId);
        }
    }

    public async Task<PagedResult<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 5, 500);

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        IQueryable<AuditLogEntry> q = context.AuditLog.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityName))
        {
            q = q.Where(x => x.EntityName == query.EntityName);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            q = q.Where(x => x.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.UserName))
        {
            var like = $"%{query.UserName}%";
            q = q.Where(x => EF.Functions.Like(x.UserName, like));
        }

        if (query.FromUtc.HasValue)
        {
            var from = query.FromUtc.Value;
            q = q.Where(x => x.CreatedAtUtc >= from);
        }

        if (query.ToUtc.HasValue)
        {
            var to = query.ToUtc.Value;
            q = q.Where(x => x.CreatedAtUtc <= to);
        }

        var total = await q.CountAsync(cancellationToken);

        var rows = await q
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.CreatedAtUtc,
                x.Action,
                x.EntityName,
                x.EntityId,
                x.UserName,
                x.Summary,
                x.ChangesJson
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new AuditLogDto
        {
            Id = x.Id,
            CreatedAtUtc = x.CreatedAtUtc,
            Action = x.Action,
            EntityName = x.EntityName,
            EntityId = x.EntityId,
            UserName = x.UserName,
            Summary = x.Summary,
            Changes = Deserialize(x.ChangesJson)
        }).ToList();

        return new PagedResult<AuditLogDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    private static IReadOnlyDictionary<string, FieldChange> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, FieldChange>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, FieldChange>>(json, JsonOptions)
                   ?? new Dictionary<string, FieldChange>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, FieldChange>();
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
