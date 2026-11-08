using SnabDrive.Web.Services;
using Xunit;

namespace SnabDrive.Web.SecurityTests;

/// <summary>
/// Загрузка вложений: размер ограничен 20 МБ, тип — положительным списком расширений.
/// Исполняемые файлы, веб-сценарии и разметка (включая SVG, которым удобно прятать
/// чужой скрипт) не принимаются.
/// </summary>
public sealed class AttachmentPolicyTests
{
    [Theory]
    [InlineData("договор.pdf")]
    [InlineData("Отчёт.docx")]
    [InlineData("scan.JPG")]
    [InlineData("архив.zip")]
    [InlineData("фото.png")]
    [InlineData("прайс.xlsx")]
    public void Documents_and_images_are_allowed(string fileName)
        => Assert.True(AttachmentLimits.IsAllowed(fileName), fileName);

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("payload.dll")]
    [InlineData("start.bat")]
    [InlineData("start.cmd")]
    [InlineData("shell.ps1")]
    [InlineData("macro.vbs")]
    [InlineData("script.js")]
    [InlineData("page.html")]
    [InlineData("page.htm")]
    [InlineData("logo.svg")]
    [InlineData("macro.xlsm")]
    [InlineData("shortcut.lnk")]
    [InlineData("noextension")]
    [InlineData("trailing.")]
    [InlineData("")]
    public void Dangerous_types_are_blocked(string fileName)
        => Assert.False(AttachmentLimits.IsAllowed(fileName), fileName);

    [Fact]
    public void Size_limit_is_20_megabytes()
    {
        Assert.Equal(20L * 1024 * 1024, AttachmentLimits.MaxFileSizeBytes);
        Assert.False(AttachmentLimits.IsTooBig(AttachmentLimits.MaxFileSizeBytes));
        Assert.True(AttachmentLimits.IsTooBig(AttachmentLimits.MaxFileSizeBytes + 1));
    }

    [Fact]
    public void Reject_message_names_the_file()
        => Assert.Contains("payload.exe", AttachmentLimits.NotAllowedMessage("payload.exe"));

    [Fact]
    public void Accept_attribute_lists_allowed_types_for_the_browser()
        => Assert.Contains(".pdf", AttachmentLimits.AcceptAttribute);
}
