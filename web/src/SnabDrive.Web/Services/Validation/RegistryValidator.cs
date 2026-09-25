using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services.Validation;

/// <summary>
/// Серверная валидация формы реестра. Используется и API, и Blazor-формой —
/// данные, пришедшие мимо UI, проходят ровно те же проверки.
/// </summary>
public static class RegistryValidator
{
    public static IReadOnlyDictionary<string, string> Validate(RegeditUpsertRequest request)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(request.NameLink))
        {
            errors[nameof(request.NameLink)] = "Укажите наименование закупки";
        }
        else if (request.NameLink.Length > 1000)
        {
            errors[nameof(request.NameLink)] = "Не более 1000 символов";
        }

        if (string.IsNullOrWhiteSpace(request.Customer))
        {
            errors[nameof(request.Customer)] = "Укажите заказчика";
        }
        else if (request.Customer.Length > 500)
        {
            errors[nameof(request.Customer)] = "Не более 500 символов";
        }

        if (request.NMCK < 0)
        {
            errors[nameof(request.NMCK)] = "НМЦК не может быть отрицательной";
        }

        if (request.MinPrice < 0)
        {
            errors[nameof(request.MinPrice)] = "Минимальная сумма не может быть отрицательной";
        }

        if (request.ResultPrice < 0)
        {
            errors[nameof(request.ResultPrice)] = "Итоговая сумма не может быть отрицательной";
        }

        if (request.ResultPrice > 0 && string.IsNullOrWhiteSpace(request.Winner))
        {
            errors[nameof(request.Winner)] = "Укажите победителя — заполнена итоговая сумма";
        }

        if (request.MinPrice > 0 && request.NMCK > 0 && request.MinPrice > request.NMCK)
        {
            errors[nameof(request.MinPrice)] = "Наша минимальная сумма не может превышать НМЦК";
        }

        if (request.BiddingDate.HasValue && request.DateResults.HasValue &&
            request.DateResults.Value.Date < request.BiddingDate.Value.Date)
        {
            errors[nameof(request.DateResults)] = "Дата подведения итогов не может быть раньше даты торгов";
        }

        if (request.DateOfConclusionOfTheContract.HasValue && request.BiddingDate.HasValue &&
            request.DateOfConclusionOfTheContract.Value.Date < request.BiddingDate.Value.Date)
        {
            errors[nameof(request.DateOfConclusionOfTheContract)] =
                "Дата заключения контракта не может быть раньше даты торгов";
        }

        return errors;
    }
}
