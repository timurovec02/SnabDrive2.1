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

    /// <summary>
    /// ID общего чата техподдержки в Битрикс24 («chat42» или просто «42»).
    /// Пусто — обращения уходят уведомлением владельцу вебхука или SupportToUserId.
    /// </summary>
    public string SupportChatId { get; set; } = string.Empty;

    /// <summary>
    /// Числовой ID сотрудника Битрикс24, которому адресованы обращения, если чат не задан.
    /// Пусто — владельцу входящего вебхука.
    /// </summary>
    public string SupportToUserId { get; set; } = string.Empty;
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
    /// Битрикс24), уведомление придёт лично ему; иначе — владельцу вебхука. Возвращает результат доставки.
    /// </summary>
    public async Task<Result> NotifyAsync(string title, string body, CancellationToken cancellationToken = default, string? toUserId = null)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            _logger.LogInformation("Bitrix24 вебхук не настроен — уведомление пропущено: {Title}", title);
            return Result.Fail("Вебхук Битрикс24 не настроен (Bitrix:WebhookUrl в appsettings).");
        }

        var pairs = new List<KeyValuePair<string, string>>
        {
            new("MESSAGE", $"{title}\n{body}")
        };

        // Личный получатель. Без TO Битрикс шлёт уведомление владельцу вебхука.
        if (!string.IsNullOrWhiteSpace(toUserId))
        {
            pairs.Add(new KeyValuePair<string, string>("TO", toUserId));
        }

        return await PostAsync("im.notify.json", pairs, cancellationToken);
    }

    /// <summary>
    /// Отправляет обращение в техподдержку: при заданном Bitrix:SupportChatId — в общий чат Битрикс24
    /// (im.message.add), иначе — уведомлением (im.notify) получателю Bitrix:SupportToUserId
    /// или владельцу вебхука. Возвращает результат доставки.
    /// </summary>
    public async Task<Result> SendSupportMessageAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            return Result.Fail("Вебхук Битрикс24 не настроен — задайте Bitrix:WebhookUrl (инструкция: web/BITRIX24.md).");
        }

        var message = $"{title}\n{body}";
        var chat = _options.SupportChatId?.Trim();
        if (!string.IsNullOrWhiteSpace(chat))
        {
            // «chat42» и «42» — оба варианта записи ID чата.
            var dialogId = chat.All(char.IsDigit) ? "chat" + chat : chat;
            return await PostAsync("im.message.add", new List<KeyValuePair<string, string>>
            {
                new("DIALOG_ID", dialogId),
                new("MESSAGE", message)
            }, cancellationToken);
        }

        var pairs = new List<KeyValuePair<string, string>> { new("MESSAGE", message) };
        if (!string.IsNullOrWhiteSpace(_options.SupportToUserId))
        {
            pairs.Add(new KeyValuePair<string, string>("TO", _options.SupportToUserId.Trim()));
        }

        return await PostAsync("im.notify.json", pairs, cancellationToken);
    }

    /// <summary>Проверка канала техподдержки: сообщение уходит тем же маршрутом, что и обращения.</summary>
    public Task<Result> SendTestSupportAsync(CancellationToken cancellationToken = default)
        => SendSupportMessageAsync(
            "Техподдержка Снабдрайв: проверка доставки",
            "Тестовое сообщение. Канал техподдержки настроен верно — сюда будут приходить обращения.",
            cancellationToken);

    /// <summary>Публикует форму в Битрикс24 и возвращает результат с текстом ошибки сервиса.</summary>
    private async Task<Result> PostAsync(string method, IEnumerable<KeyValuePair<string, string>> pairs, CancellationToken cancellationToken)
    {
        try
        {
            var url = _options.WebhookUrl.TrimEnd('/') + "/" + method;
            using var content = new FormUrlEncodedContent(pairs);
            var response = await _httpFactory.CreateClient().PostAsync(url, content, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bitrix24 {Method} вернул код {Code}: {Body}", method, (int)response.StatusCode, Truncate(text));
                return Result.Fail($"Битрикс24 вернул {(int)response.StatusCode}: {Truncate(text)}");
            }

            if (text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Bitrix24 {Method} сообщил ошибку: {Body}", method, Truncate(text));
                return Result.Fail($"Битрикс24: {Truncate(text)}");
            }

            _logger.LogInformation("Bitrix24 {Method} успешно, код {Code}", method, (int)response.StatusCode);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отправить {Method} в Bitrix24", method);
            return Result.Fail($"Ошибка отправки: {ex.Message}");
        }
    }

    /// <summary>
    /// Тестовая отправка уведомления конкретному сотруднику. Возвращает результат с текстом ошибки Битрикс24,
    /// чтобы сразу понять, верно ли заданы вебхук и ID сотрудника.
    /// </summary>
    public Task<Result> SendTestAsync(string? toUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            return Task.FromResult(Result.Fail("Вебхук Битрикс24 не настроен (Bitrix:WebhookUrl в appsettings)."));
        }

        if (string.IsNullOrWhiteSpace(toUserId))
        {
            return Task.FromResult(Result.Fail("У пользователя не заполнен «ID сотрудника в Битрикс24»."));
        }

        return PostAsync("im.notify.json", new[]
        {
            new KeyValuePair<string, string>("MESSAGE",
                "Тестовое уведомление Снабдрайв: интеграция с Битрикс24 настроена, получение работает."),
            new KeyValuePair<string, string>("TO", toUserId)
        }, cancellationToken);
    }

    private static string Truncate(string? s, int max = 300)
        => string.IsNullOrWhiteSpace(s) ? "(пустой ответ)" : (s.Length <= max ? s : s[..max] + "…");
}
