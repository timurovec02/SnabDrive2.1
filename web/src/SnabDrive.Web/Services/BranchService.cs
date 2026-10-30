using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

/// <summary>Элемент справочника «Филиалы».</summary>
public sealed record BranchDto(int Id, string Name, string? BitrixUserId);

public interface IBranchService
{
    /// <summary>Активные филиалы (для выпадающего списка на этапе 7).</summary>
    Task<IReadOnlyList<BranchDto>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Филиал по Id (для оповещения); null, если не найден.</summary>
    Task<BranchDto?> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<DictionaryItemDto>> CreateAsync(string name, string? bitrixUserId, CancellationToken cancellationToken = default);
    Task<Result<DictionaryItemDto>> UpdateAsync(int id, string name, string? bitrixUserId, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
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

    public async Task<Result<DictionaryItemDto>> CreateAsync(string name, string? bitrixUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<DictionaryItemDto>.Fail("Укажите название филиала.");
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var branch = new Branch { Name = name.Trim(), BitrixUserId = Norm(bitrixUserId), IsActive = true };
        context.Branches.Add(branch);
        await context.SaveChangesAsync(cancellationToken);
        return Result<DictionaryItemDto>.Ok(new DictionaryItemDto(branch.Id, branch.Name, branch.BitrixUserId));
    }

    public async Task<Result<DictionaryItemDto>> UpdateAsync(int id, string name, string? bitrixUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<DictionaryItemDto>.Fail("Укажите название филиала.");
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var branch = await context.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null)
        {
            return Result<DictionaryItemDto>.Fail("Филиал не найден.");
        }

        branch.Name = name.Trim();
        branch.BitrixUserId = Norm(bitrixUserId);
        await context.SaveChangesAsync(cancellationToken);
        return Result<DictionaryItemDto>.Ok(new DictionaryItemDto(branch.Id, branch.Name, branch.BitrixUserId));
    }

    private static string? Norm(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var branch = await context.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null)
        {
            return Result.Fail("Филиал не найден.");
        }

        var used = await context.Regedit.AnyAsync(r => r.BranchId == id, cancellationToken);
        if (used)
        {
            return Result.Fail("Филиал используется в закупках — удаление отклонено.");
        }

        context.Branches.Remove(branch);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
