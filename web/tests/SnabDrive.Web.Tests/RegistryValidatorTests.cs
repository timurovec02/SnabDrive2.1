using SnabDrive.Web.Domain;
using SnabDrive.Web.Services;
using SnabDrive.Web.Services.Export;
using SnabDrive.Web.Services.Validation;
using Xunit;

namespace SnabDrive.Web.Tests;

public class RegistryValidatorTests
{
    [Fact]
    public void Валидный_запрос_проходит_без_ошибок()
    {
        var errors = RegistryValidator.Validate(TestServices.ValidRequest());
        Assert.Empty(errors);
    }

    [Fact]
    public void Пустой_заказчик_отклоняется()
    {
        var request = TestServices.ValidRequest() with { Customer = "" };

        var errors = RegistryValidator.Validate(request);

        Assert.True(errors.ContainsKey(nameof(RegeditUpsertRequest.Customer)));
    }

    [Fact]
    public void Отрицательная_сумма_отклоняется()
    {
        var request = TestServices.ValidRequest() with { NMCK = -5m };

        var errors = RegistryValidator.Validate(request);

        Assert.True(errors.ContainsKey(nameof(RegeditUpsertRequest.NMCK)));
    }

    [Fact]
    public void Итоговая_сумма_без_победителя_отклоняется()
    {
        var request = TestServices.ValidRequest() with { ResultPrice = 100m, Winner = "" };

        var errors = RegistryValidator.Validate(request);

        Assert.True(errors.ContainsKey(nameof(RegeditUpsertRequest.Winner)));
    }

    [Fact]
    public void Минимальная_сумма_больше_НМЦК_отклоняется()
    {
        var request = TestServices.ValidRequest() with { NMCK = 100m, MinPrice = 500m };

        var errors = RegistryValidator.Validate(request);

        Assert.True(errors.ContainsKey(nameof(RegeditUpsertRequest.MinPrice)));
    }

    [Fact]
    public void Дата_итогов_раньше_даты_торгов_отклоняется()
    {
        var request = TestServices.ValidRequest() with
        {
            BiddingDate = new DateTime(2025, 5, 10),
            DateResults = new DateTime(2025, 5, 1)
        };

        var errors = RegistryValidator.Validate(request);

        Assert.True(errors.ContainsKey(nameof(RegeditUpsertRequest.DateResults)));
    }
}

public class RegistryQueryTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void Normalize_приводит_номер_страницы_к_допустимому(int page, int expected)
    {
        var query = new RegistryQuery { Page = page }.Normalize();
        Assert.Equal(expected, query.Page);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(50, 50)]
    [InlineData(10000, RegistryQuery.MaxPageSize)]
    public void Normalize_ограничивает_размер_страницы(int pageSize, int expected)
    {
        var query = new RegistryQuery { PageSize = pageSize }.Normalize();
        Assert.Equal(expected, query.PageSize);
    }
}

public class DateParsingTests
{
    [Theory]
    [InlineData("2025-03-10", 2025, 3, 10)]
    [InlineData("10.03.2025", 2025, 3, 10)]
    [InlineData("10/03/2025", 2025, 3, 10)]
    public void Разбирает_даты_в_разных_форматах(string input, int year, int month, int day)
    {
        Assert.True(RegistryService.TryParseDate(input, out var date));
        Assert.Equal(new DateTime(year, month, day), date.Date);
    }

    [Fact]
    public void Не_разбирает_мусор()
    {
        Assert.False(RegistryService.TryParseDate("не дата", out _));
    }
}

public class CsvExporterTests
{
    [Fact]
    public void Экранирует_значения_с_разделителем()
    {
        var rows = new[]
        {
            new RegistryRowDto
            {
                Id = 1,
                NameLink = "Закупка; с точкой с запятой",
                Customer = "ООО \"Ромашка\"",
                NMCK = 1500.5m,
                BiddingDate = new DateTime(2025, 3, 10)
            }
        };

        var columns = RegistryColumns.All
            .Where(c => c.Key is "NameLink" or "Customer" or "NMCK" or "BiddingDate")
            .ToList();

        var csv = CsvExporter.Export(rows, columns);
        var lines = csv.Replace("\r\n", "\n").Trim('\n', '\uFEFF').Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Contains("\"Закупка; с точкой с запятой\"", lines[1]);
        Assert.Contains("\"ООО \"\"Ромашка\"\"\"", lines[1]);
        Assert.Contains("10.03.2025", lines[1]);
    }
}

public class AccountEndpointUrlTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/registry", "/registry")]
    [InlineData("//evil.com", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("https://evil.com", "/")]
    public void Принимает_только_локальные_адреса(string? input, string expected)
    {
        Assert.Equal(expected, AccountEndpoints.ToSafeLocalUrl(input));
    }
}
