using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

/// <summary>Настройки интеграции с Битрикс24 (облако, входящий вебхук REST).</summary>
public sealed class BitrixOptions
{
    /// <summary>
    /// Базовый URL входящего вебхука, напр. https://вашпортал.bitrix24.ru/rest/1/код/.
    /// Пусто — уведомления не отправляются (только лог).
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;
}

/// <summary>Отправляет уведомление в Битрикс24 (im.notify) через входящий вебхук.</summary>
public sealed class BitrixNotifier
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly BitrixOptions _options;
    private readonly ILogger<BitrixNotifier> _logger;

    public BitrixNotifier(IHttpClientFactory httpFactory, IOptions<BitrixOptions> options, ILogger<BitrixNotifier> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Отправляет уведомление (im.notify). Если указан <paramref name="toUserId"/> (числовой ID сотрудника
    /// Битрикс24), уведомление придёт лично ему; иначе — владельцу вебхука.
    /// </summary>
    public async Task NotifyAsync(string title, string body, CancellationToken cancellationToken = default, string? toUserId = null)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            _logger.LogInformation("Bitrix24 вебхук не настроен — уведомление пропущено: {Title}", title);
            return;
        }

        try
        {
            var url = _options.WebhookUrl.TrimEnd('/') + "/im.notify.json";

            var pairs = new List<KeyValuePair<string, string>>
            {
                new("MESSAGE", $"{title}\n{body}")
            };

            // Личный получатель. Без TO Битрикс шлёт уведомление владельцу вебхука.
            if (!string.IsNullOrWhiteSpace(toUserId))
            {
                pairs.Add(new KeyValuePair<string, string>("TO", toUserId));
            }

            using var content = new FormUrlEncodedContent(pairs);

            var response = await _httpFactory.CreateClient().PostAsync(url, content, cancellationToken);
            _logger.LogInformation("Bitrix24 уведомление отправлено (TO={To}), код {Code}", toUserId ?? "владелец вебхука", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отправить уведомление в Bitrix24");
        }
    }

    /// <summary>
    /// Тестовая отправка уведомления конкретному сотруднику. Возвращает результат с текстом ошибки Битрикс24,
    /// чтобы сразу понять, верно ли заданы вебхук и ID сотрудника.
    /// </summary>
    public async Task<Result> SendTestAsync(string? toUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            return Result.Fail("Вебхук Битрикс24 не настроен (Bitrix:WebhookUrl в appsettings).");
        }

        if (string.IsNullOrWhiteSpace(toUserId))
        {
            return Result.Fail("У пользователя не заполнен «ID сотрудника в Битрикс24».");
        }

        try
        {
            var url = _options.WebhookUrl.TrimEnd('/') + "/im.notify.json";
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("MESSAGE",
                    "Тестовое уведомление SnabDrive: интеграция с Битрикс24 настроена, получение работает."),
                new KeyValuePair<string, string>("TO", toUserId)
            });

            var response = await _httpFactory.CreateClient().PostAsync(url, content, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail($"Битрикс24 вернул {(int)response.StatusCode}: {Truncate(text)}");
            }

            if (text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Fail($"Битрикс24: {Truncate(text)}");
            }

            _logger.LogInformation("Тестовое уведомление Битрикс24 отправлено (TO={To})", toUserId);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Тестовое уведомление Битрикс24 не отправлено");
            return Result.Fail($"Ошибка отправки: {ex.Message}");
        }
    }

    private static string Truncate(string? s, int max = 300)
        => string.IsNullOrWhiteSpace(s) ? "(пустой ответ)" : (s.Length <= max ? s : s[..max] + "…");
}
