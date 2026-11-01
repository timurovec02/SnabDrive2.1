using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

public interface IStageHistoryService
{
    /// <summary>Текущая стадия закупки и все её переводы по журналу (новые сверху).</summary>
    Task<StageHistoryDto?> GetAsync(int regeditId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Отдаёт историю этапов закупки для выпадающего списка «все пройденные стадии».
/// Источник — журнал передач <c>RegistryWorkflowLogs</c>: в него пишется каждый перевод
/// между стадиями вместе с сотрудником и временем, поэтому список показывает не только
/// последний этап, но и то, кто вёл каждый из предыдущих.
/// </summary>
public sealed class StageHistoryService : IStageHistoryService
{
    private readonly IDbContextFactory<RegistryDbContext> _factory;

    public StageHistoryService(IDbContextFactory<RegistryDbContext> factory) => _factory = factory;

    public async Task<StageHistoryDto?> GetAsync(int regeditId, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);

        var head = await context.Regedit.AsNoTracking()
            .Where(x => x.Id == regeditId)
            .Select(x => new
            {
                x.Stage,
                x.StageStartedBy,
                x.StageStartedAtUtc,
                x.StageCompletedBy,
                x.StageCompletedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (head is null)
        {
            return null;
        }

        // Проекция в анонимный тип, а не в рекорд: так не зависит от того,
        // как EF разбирает конструктор с параметрами.
        var logs = await context.RegistryWorkflowLogs.AsNoTracking()
            .Where(x => x.RegeditId == regeditId)
            .OrderByDescending(x => x.AtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.AtUtc, x.ByUser, x.FromStage, x.ToStage, x.Action })
            .ToListAsync(cancellationToken);

        var entries = logs
            .Select(x => new StageHistoryEntryDto(x.AtUtc, x.ByUser, x.FromStage, x.ToStage, x.Action))
            .ToList();

        return new StageHistoryDto(
            head.Stage,
            WorkflowStages.Name(head.Stage),
            head.StageStartedBy,
            head.StageStartedAtUtc,
            head.StageCompletedBy,
            head.StageCompletedAtUtc,
            entries);
    }
}
