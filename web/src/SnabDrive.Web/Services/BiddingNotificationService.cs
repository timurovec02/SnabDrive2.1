using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;

namespace SnabDrive.Web.Services;

/// <summary>
/// Фоновая проверка предстоящих торгов: за 1 день до торгов и в день торгов отправляет
/// уведомление в Битрикс24 роли «Руководитель тендерного отдела» (TenderHead).
/// Каждое уведомление отправляется ОДИН раз (факт пишется в [BiddingNotifications]).
/// </summary>
public sealed class BiddingNotificationService : BackgroundService
{
    private const string TenderHeadRole = "TenderHead";

    private readonly IServiceProvider _services;
    private readonly ILogger<BiddingNotificationService> _logger;

    public BiddingNotificationService(IServiceProvider services, ILogger<BiddingNotificationService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка проверки предстоящих торгов");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        // Фабрика БД, BitrixNotifier и UserManager — scoped, берём их из области видимости.
        using var scope = _services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RegistryDbContext>>();
        var bitrix = scope.ServiceProvider.GetRequiredService<BitrixNotifier>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await using var db = await factory.CreateDbContextAsync(ct);

        var upcoming = await db.Regedit.AsNoTracking()
            .Where(r => r.BiddingDate != null)
            .Select(r => new { r.Id, r.NameLink, r.BiddingDate })
            .ToListAsync(ct);

        var dayBefore = upcoming.Where(r => r.BiddingDate!.Value.Date == tomorrow).ToList();
        var dayOf = upcoming.Where(r => r.BiddingDate!.Value.Date == today).ToList();

        if (dayBefore.Count == 0 && dayOf.Count == 0)
        {
            return;
        }

        var users = await userManager.GetUsersInRoleAsync(TenderHeadRole);
        var recipients = users
            .Where(u => !string.IsNullOrWhiteSpace(u.BitrixUserId))
            .Select(u => (BitrixUserId: u.BitrixUserId!, UserName: u.UserName ?? u.Id))
            .ToList();

        if (recipients.Count == 0)
        {
            _logger.LogInformation("Уведомления о торгах: нет получателей роли {Role} с BitrixUserId", TenderHeadRole);
            return;
        }

        foreach (var r in dayBefore)
        {
            await NotifyOnceAsync(db, bitrix, r.Id, r.NameLink, r.BiddingDate!.Value, "DayBefore",
                $"Завтра торги: {r.NameLink}", recipients, ct);
        }

        foreach (var r in dayOf)
        {
            await NotifyOnceAsync(db, bitrix, r.Id, r.NameLink, r.BiddingDate!.Value, "DayOf",
                $"Сегодня торги: {r.NameLink}", recipients, ct);
        }
    }

    private async Task NotifyOnceAsync(RegistryDbContext db, BitrixNotifier bitrix, int regeditId, string name,
        DateTime biddingDate, string kind, string title,
        IReadOnlyList<(string BitrixUserId, string UserName)> recipients, CancellationToken ct)
    {
        var exists = await db.BiddingNotifications.AnyAsync(n => n.RegeditId == regeditId && n.Kind == kind, ct);
        if (exists)
        {
            return; // уже уведомляли — повторно не отправляем
        }

        var body = $"Закупка: {name}\nДата торгов: {biddingDate:dd.MM.yyyy}";
        var sent = 0;
        foreach (var (bitrixUserId, userName) in recipients)
        {
            try
            {
                await bitrix.NotifyAsync(title, body, ct, toUserId: bitrixUserId);
                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось отправить уведомление о торгах пользователю {User}", userName);
            }
        }

        db.BiddingNotifications.Add(new BiddingNotification
        {
            RegeditId = regeditId,
            Kind = kind,
            NotifiedAtUtc = DateTime.UtcNow,
            RecipientsCount = sent
        });
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Уведомление о торгах ({Kind}) по закупке {Id}: отправлено {Count}", kind, regeditId, sent);
    }
}
