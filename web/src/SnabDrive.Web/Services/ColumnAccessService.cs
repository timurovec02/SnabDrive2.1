using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

public interface IColumnAccessService
{
    /// <summary>Эффективная карта доступа пользователя (администратор всегда без ограничений).</summary>
    Task<ColumnAccessMap> GetAsync(string userId, CancellationToken cancellationToken = default);

    Task<ColumnAccessMode> GetModeAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ColumnPermission>> GetPermissionsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Полностью перезаписывает набор прав пользователя на колонки.</summary>
    Task<Result> SetAsync(string userId, ColumnAccessMode mode, IReadOnlyList<ColumnPermission> permissions,
        ChangeActor actor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Доступ к колонкам реестра «по пользователям». Хранится в таблице [UserColumnPermission];
/// учитывается только при ApplicationUser.ColumnAccessMode = CustomColumns.
/// </summary>
public sealed class ColumnAccessService : IColumnAccessService
{
    private readonly IDbContextFactory<AppIdentityDbContext> _factory;
    private readonly IAuditService _audit;
    private readonly IRegistryNotifier _notifier;

    public ColumnAccessService(IDbContextFactory<AppIdentityDbContext> factory, IAuditService audit, IRegistryNotifier notifier)
    {
        _factory = factory;
        _audit = audit;
        _notifier = notifier;
    }

    public async Task<ColumnAccessMap> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ColumnAccessMap.Full;
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var mode = await context.Users
            .Where(u => u.Id == userId)
            .Select(u => (ColumnAccessMode?)u.ColumnAccessMode)
            .FirstOrDefaultAsync(cancellationToken);

        if (mode is null || mode == ColumnAccessMode.AllColumns)
        {
            return ColumnAccessMap.Full;
        }

        if (await IsAdminAsync(context, userId, cancellationToken))
        {
            // Администратора нельзя «запереть» — иначе он потеряет доступ к настройке прав.
            return ColumnAccessMap.Full;
        }

        var permissions = await LoadPermissionsAsync(context, userId, cancellationToken);
        return ColumnAccessMap.Build(permissions);
    }

    public async Task<ColumnAccessMode> GetModeAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        return await context.Users
                   .Where(u => u.Id == userId)
                   .Select(u => (ColumnAccessMode?)u.ColumnAccessMode)
                   .FirstOrDefaultAsync(cancellationToken)
               ?? ColumnAccessMode.AllColumns;
    }

    public async Task<IReadOnlyList<ColumnPermission>> GetPermissionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await LoadPermissionsAsync(context, userId, cancellationToken);
    }

    public async Task<Result> SetAsync(string userId, ColumnAccessMode mode, IReadOnlyList<ColumnPermission> permissions,
        ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return Result.Fail("Пользователь не найден.");
        }

        var unknown = permissions
            .Select(p => p.Key)
            .FirstOrDefault(key => !RegistryColumns.IsKnown(key));

        if (unknown is not null)
        {
            return Result.Fail($"Неизвестная колонка «{unknown}».");
        }

        var existing = await context.UserColumnPermissions
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        context.UserColumnPermissions.RemoveRange(existing);

        foreach (var permission in permissions.Select(p => p.Normalized()).DistinctBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            context.UserColumnPermissions.Add(new UserColumnPermission
            {
                UserId = userId,
                ColumnKey = permission.Key,
                CanView = permission.CanView,
                CanEdit = permission.CanEdit
            });
        }

        user.ColumnAccessMode = mode;

        await context.SaveChangesAsync(cancellationToken);

        var summary = mode == ColumnAccessMode.AllColumns
            ? $"Пользователю «{user.UserName}» открыт доступ ко всем колонкам"
            : $"Пользователю «{user.UserName}» настроен доступ к колонкам: {permissions.Count(p => p.CanView)} просмотр, {permissions.Count(p => p.CanEdit)} редактирование";

        await _audit.WriteAsync(AuditAction.UserChanged, "UserColumnPermission", 0, actor, summary: summary, cancellationToken: cancellationToken);

        await _notifier.PublishAsync(new RegistryChangeEvent(
            ChangeKind.UserChanged, "UserColumnPermission", 0, actor.UserName, DateTime.UtcNow, summary), cancellationToken);

        return Result.Ok();
    }

    private static async Task<bool> IsAdminAsync(AppIdentityDbContext context, string userId, CancellationToken cancellationToken)
    {
        var roleIds = await context.Roles
            .Where(r => r.Name == AppRoles.Admin)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0)
        {
            return false;
        }

        return await context.UserRoles.AnyAsync(ur => ur.UserId == userId && roleIds.Contains(ur.RoleId), cancellationToken);
    }

    private static async Task<IReadOnlyList<ColumnPermission>> LoadPermissionsAsync(
        AppIdentityDbContext context, string userId, CancellationToken cancellationToken)
    {
        return await context.UserColumnPermissions.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new ColumnPermission(p.ColumnKey, p.CanView, p.CanEdit))
            .ToListAsync(cancellationToken);
    }
}
