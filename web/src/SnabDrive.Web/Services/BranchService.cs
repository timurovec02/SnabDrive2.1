using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;

namespace SnabDrive.Web.Services;

/// <summary>Элемент справочника «Филиалы».</summary>
public sealed record BranchDto(int Id, string Name, string? BitrixUserId);

public interface IBranchService
{
    /// <summary>Активные филиалы (для выпадающего списка на этапе 7).</summary>
    Task<IReadOnlyList<BranchDto>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Филиал по Id (для оповещения); null, если не найден.</summary>
    Task<BranchDto?> GetAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class BranchService : IBranchService
{
    private readonly IDbContextFactory<RegistryDbContext> _factory;

    public BranchService(IDbContextFactory<RegistryDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<BranchDto>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.Branches.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BranchDto(b.Id, b.Name, b.BitrixUserId))
            .ToListAsync(cancellationToken);
    }

    public async Task<BranchDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.Branches.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BranchDto(b.Id, b.Name, b.BitrixUserId))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
