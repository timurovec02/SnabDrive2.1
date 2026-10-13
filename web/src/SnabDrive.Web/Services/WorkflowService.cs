using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

/// <summary>Строка воркфлоу: данные закупки + текущая стадия.</summary>
public sealed record WorkflowRow(
    int Id,
    string NameLink,
    string Customer,
    decimal NMCK,
    DateTime? BiddingDate,
    string ReserveNumber,
    decimal ResultPrice,
    string Winner,
    int Stage,
    bool IsLost);

public interface IWorkflowService
{
    Task<IReadOnlyList<WorkflowRow>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Передать задачу: to2 (1→2), won (2→3), lost (проигран), finish (3→4).</summary>
    Task<Result> AdvanceAsync(int regeditId, string action, ChangeActor actor, CancellationToken cancellationToken = default);
}

public sealed class WorkflowService : IWorkflowService
{
    private readonly IDbContextFactory<RegistryDbContext> _factory;
    private readonly BitrixNotifier _bitrix;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(IDbContextFactory<RegistryDbContext> factory, BitrixNotifier bitrix, ILogger<WorkflowService> logger)
    {
        _factory = factory;
        _bitrix = bitrix;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WorkflowRow>> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context.Regedit.AsNoTracking()
            .OrderBy(r => r.Stage).ThenByDescending(r => r.Id)
            .Select(r => new WorkflowRow(r.Id, r.NameLink, r.Customer, r.NMCK, r.BiddingDate,
                r.ReserveNumber, r.ResultPrice, r.Winner, r.Stage, r.IsLost))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result> AdvanceAsync(int regeditId, string action, ChangeActor actor, CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var item = await context.Regedit.FirstOrDefaultAsync(r => r.Id == regeditId, cancellationToken);
        if (item is null)
        {
            return Result.Fail("Запись не найдена.");
        }

        var from = item.Stage;
        int to;
        string act;
        var lost = false;

        switch (action)
        {
            case "to2": to = 2; act = "Передано в закупку"; break;
            case "won": to = 3; act = "Выиграно — на оформление"; break;
            case "lost": to = 0; lost = true; act = "Проиграно"; break;
            case "finish": to = 4; act = "Завершено"; break;
            default: return Result.Fail("Неизвестное действие.");
        }

        item.Stage = to;
        item.IsLost = lost;

        context.RegistryWorkflowLogs.Add(new RegistryWorkflowLog
        {
            RegeditId = regeditId,
            FromStage = from,
            ToStage = to,
            Action = act,
            ByUser = actor.UserName,
            AtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);

        var title = to switch
        {
            2 => "Новая задача: участие в закупке",
            3 => "Новая задача: оформление документов",
            4 => "Закупка завершена",
            0 => "Закупка проиграна",
            _ => "Задача обновлена"
        };
        await _bitrix.NotifyAsync(title, $"{item.NameLink}\nЗаказчик: {item.Customer}", cancellationToken);

        _logger.LogInformation("Воркфлоу: закупка {Id} {From}->{To} ({Action}) пользователем {User}",
            regeditId, from, to, act, actor.UserName);

        return Result.Ok();
    }
}
