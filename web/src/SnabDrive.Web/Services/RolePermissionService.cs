using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

public interface IRolePermissionService
{
    /// <summary>Права роли (из БД; если не заданы — значения по умолчанию).</summary>
    Task<RolePermissionSet> GetAsync(string roleName, CancellationToken cancellationToken = default);

    /// <summary>Все роли, существующие в системе.</summary>
    Task<IReadOnlyList<string>> GetAllRoleNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Сохранить права роли.</summary>
    Task SetAsync(string roleName, RolePermissionSet set, CancellationToken cancellationToken = default);

    /// <summary>Эффективный доступ пользователя к вкладкам (объединение по его ролям; Admin — всё).</summary>
    Task<Dictionary<string, bool>> GetEffectiveFeaturesAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Эффективная карта колонок по ролям пользователя (без учёта личного override — его добавляет ColumnAccessService).</summary>
    Task<ColumnAccessMap> GetEffectiveColumnMapAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Эффективные стадии воркфлоу пользователя (объединение по ролям; Admin — все). Возвращает все Id, если ограничений нет.</summary>
    Task<IReadOnlyList<int>> GetEffectiveStagesAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class RolePermissionService : IRolePermissionService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<AppIdentityDbContext> _factory;
    private readonly UserManager<ApplicationUser> _users;

    public RolePermissionService(IDbContextFactory<AppIdentityDbContext> factory, UserManager<ApplicationUser> users)
    {
        _factory = factory;
        _users = users;
    }

    public async Task<RolePermissionSet> GetAsync(string roleName, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await ctx.RolePermissions.AsNoTracking().FirstOrDefaultAsync(r => r.RoleName == roleName, cancellationToken);
        return row is null ? RolePermissionDefaults.For(roleName) : Deserialize(row);
    }

    public async Task<IReadOnlyList<string>> GetAllRoleNamesAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(cancellationToken);
        return await ctx.Roles.Where(r => r.Name != null).Select(r => r.Name!).ToListAsync(cancellationToken);
    }

    public async Task SetAsync(string roleName, RolePermissionSet set, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await ctx.RolePermissions.FirstOrDefaultAsync(r => r.RoleName == roleName, cancellationToken);
        if (row is null)
        {
            row = new RolePermission { RoleName = roleName };
            ctx.RolePermissions.Add(row);
        }
        row.FeaturesJson = JsonSerializer.Serialize(set.Features, Json);
        row.ColumnsJson = JsonSerializer.Serialize(set.Columns, Json);
        row.StagesJson = JsonSerializer.Serialize(set.Stages, Json);
        row.UpdatedAtUtc = DateTime.UtcNow;
        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async Task<Dictionary<string, bool>> GetEffectiveFeaturesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var result = AppFeatures.All.ToDictionary(f => f.Key, _ => false, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(userId)) return result;

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return result;

        var roles = await _users.GetRolesAsync(user);
        if (roles.Contains(AppRoles.Admin))
        {
            foreach (var key in result.Keys.ToList()) result[key] = true;
            return result;
        }

        foreach (var role in roles)
        {
            var set = await GetAsync(role, cancellationToken);
            foreach (var kv in set.Features)
            {
                if (kv.Value && result.ContainsKey(kv.Key)) result[kv.Key] = true;
            }
        }
        return result;
    }

    public async Task<ColumnAccessMap> GetEffectiveColumnMapAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return ColumnAccessMap.Full;

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return ColumnAccessMap.Full;

        var roles = await _users.GetRolesAsync(user);
        if (roles.Contains(AppRoles.Admin)) return ColumnAccessMap.Full;

        var merged = new Dictionary<string, (bool view, bool edit)>(StringComparer.OrdinalIgnoreCase);
        var anyCustom = false;
        foreach (var role in roles)
        {
            var set = await GetAsync(role, cancellationToken);
            if (set.Columns.Count == 0) continue; // роль даёт все колонки
            anyCustom = true;
            foreach (var c in set.Columns)
            {
                merged.TryGetValue(c.Key, out var cur);
                merged[c.Key] = (cur.view || c.CanView, cur.edit || c.CanEdit);
            }
        }

        if (!anyCustom) return ColumnAccessMap.Full;
        var perms = merged.Select(kv => new ColumnPermission(kv.Key, kv.Value.view, kv.Value.edit)).ToList();
        return ColumnAccessMap.Build(perms);
    }

    public async Task<IReadOnlyList<int>> GetEffectiveStagesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var all = WorkflowStages.All.Select(s => s.Id).ToList();
        if (string.IsNullOrWhiteSpace(userId)) return all;

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return all;

        var roles = await _users.GetRolesAsync(user);
        if (roles.Contains(AppRoles.Admin)) return all;

        var merged = new HashSet<int>();
        var anyCustom = false;
        foreach (var role in roles)
        {
            var set = await GetAsync(role, cancellationToken);
            if (set.Stages.Count == 0) continue; // роль отвечает за все стадии
            anyCustom = true;
            foreach (var s in set.Stages) merged.Add(s);
        }

        return anyCustom ? merged.ToList() : all;
    }

    private static RolePermissionSet Deserialize(RolePermission row)
    {
        var set = new RolePermissionSet();
        try
        {
            set.Features = JsonSerializer.Deserialize<Dictionary<string, bool>>(row.FeaturesJson ?? "{}", Json)
                           ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            set.Features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(row.ColumnsJson))
        {
            try { set.Columns = JsonSerializer.Deserialize<List<ColumnPermission>>(row.ColumnsJson, Json) ?? new List<ColumnPermission>(); }
            catch { set.Columns = new List<ColumnPermission>(); }
        }

        if (!string.IsNullOrWhiteSpace(row.StagesJson))
        {
            try { set.Stages = JsonSerializer.Deserialize<List<int>>(row.StagesJson, Json) ?? new List<int>(); }
            catch { set.Stages = new List<int>(); }
        }
        return set;
    }
}
