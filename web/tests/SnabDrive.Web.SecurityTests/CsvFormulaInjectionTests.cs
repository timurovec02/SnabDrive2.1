using SnabDrive.Web.Services.Export;
using Xunit;

namespace SnabDrive.Web.SecurityTests;

/// <summary>
/// Инъекция формул в CSV: выгрузку реестра открывают в Excel, а ячейка, начинающаяся
/// со знака формулы, исполняется. Так пользователь с правом редактировать реестр мог бы
/// передать DDE-команду (=cmd|'/C calc'!A0) тому, кто откроет файл.
/// </summary>
public sealed class CsvFormulaInjectionTests
{
    [Fact]
    public void Formula_cells_are_neutralised()
    {
        Assert.StartsWith("'", CsvExporter.Escape("=cmd|' /C calc'!A0"));
        Assert.StartsWith("'", CsvExporter.Escape("+1+1"));
        Assert.StartsWith("'", CsvExporter.Escape("@SUM(A1:A9)"));
        Assert.StartsWith("'", CsvExporter.Escape("-2+3+cmd"));
        Assert.StartsWith("'", CsvExporter.Escape("\t=1+1"));
    }

    [Fact]
    public void Ordinary_text_and_numbers_are_untouched()
    {
        Assert.Equal("Поставка труб", CsvExporter.Escape("Поставка труб"));
        Assert.Equal("12,5", CsvExporter.Escape("12,5"));
        Assert.Equal("01.09.2026", CsvExporter.Escape("01.09.2026"));
    }

    [Fact]
    public void Negative_numbers_keep_their_sign()
        => Assert.Equal("-1500", CsvExporter.Escape("-1500"));

    [Fact]
    public void Separators_and_quotes_are_escaped()
        => Assert.Equal("\"ООО \"\"Ромашка\"\"; Москва\"", CsvExporter.Escape("ООО \"Ромашка\"; Москва"));

    [Fact]
    public void Empty_value_stays_empty()
        => Assert.Equal(string.Empty, CsvExporter.Escape(string.Empty));
}
