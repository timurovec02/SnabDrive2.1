using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
}
