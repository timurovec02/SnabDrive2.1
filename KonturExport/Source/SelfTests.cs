using System.IO.Compression;
using System.Text;
using Microsoft.Playwright;

namespace KonturExport;

internal static class SelfTests
{
    public static int RunUnitTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "kontur-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string xlsx = Path.Combine(root, "uuid-without-extension");
            File.WriteAllBytes(xlsx, CreateWorkbook());
            Check(OutputFiles.DetectFormat(xlsx, "guid")?.Extension == ".xlsx", "XLSX определяется по содержимому, даже без расширения");
            string zip = Path.Combine(root, "ordinary.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntry("test.txt");
            Check(OutputFiles.DetectFormat(zip, "wrong.xlsx") == null, "Обычный ZIP не считается Excel");
            string html = Path.Combine(root, "login.html");
            File.WriteAllText(html, "<!doctype html><html><body>Login</body></html>");
            Check(OutputFiles.DetectFormat(html, "export.xlsx") == null, "HTML входа не переименовывается в XLSX");
            string json = Path.Combine(root, "error.json");
            File.WriteAllText(json, "{\"error\":\"login required\"}");
            Check(OutputFiles.DetectFormat(json, "export.xlsx") == null, "JSON-ошибка не считается Excel");
            string ole = Path.Combine(root, "old.xls");
            File.WriteAllBytes(ole, [0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1, 0]);
            Check(OutputFiles.DetectFormat(ole, "old.xls")?.Extension == ".xls", "OLE-сигнатура распознаётся для старого Excel");
            Check(!OutputFiles.SafeStem("a:b/c\\d?*").Any(c => ":/\\?*".Contains(c)), "Windows-имя очищается");
            Check(Settings.ExpandPath("relative", root, root) == Path.Combine(root, "relative"), "Относительный путь привязан к settings.json");
            Check(CliOptions.Parse(["--output", "D:\\Excel", "--headless", "--no-pause"]).Headless == true, "Аргументы CLI разбираются");
            string executable = Path.Combine(root, "browser.exe");
            File.WriteAllText(executable, "path-resolution-fixture");
            Check(YandexBrowser.Resolve(executable) == executable, "Яндекс: явный путь имеет приоритет");
            Check(YandexBrowser.FirstExisting([Path.Combine(root, "missing.exe"), executable]) == executable, "Яндекс: выбор первого существующего кандидата");
            bool missingRejected = false;
            try { YandexBrowser.Resolve(Path.Combine(root, "not-installed.exe")); }
            catch (FileNotFoundException) { missingRejected = true; }
            Check(missingRejected, "Яндекс: неверный явный путь не подменяется Chromium");
            var launch = YandexBrowser.LaunchOptions(new Settings(), executable, false);
            Check(launch.ExecutablePath == executable && launch.Channel == null, "Яндекс: ExecutablePath вместо Channel");
            var ignored = launch.IgnoreDefaultArgs ?? Array.Empty<string>();
            Check(ignored.Contains("--disable-extensions") && ignored.Contains("--disable-component-extensions-with-background-pages"), "Яндекс: расширения не отключаются");
            var settings = new Settings { OutputDirectory = root };
            settings.Normalize(Path.Combine(root, "settings.json"), null);
            Check(Path.GetFileName(settings.ProfileDirectory) == "yandex-profile", "Яндекс: новый отдельный профиль, не профиль Chrome for Testing");
            var cli = CliOptions.Parse(["--browser", executable, "--setup-plugin", "--check-browser"]);
            Check(cli.BrowserExecutablePath == executable && cli.SetupPlugin && cli.CheckBrowser, "Яндекс: команды выбора и настройки плагина");
            Check(new Settings().UseCertificateLogin && new Settings().CertificateOwnerName == "Титов Андрей Николаевич", "вход по сертификату и нужное ФИО заданы по умолчанию");
            var nameMatcher = CertificateLogin.OwnerTextRegex("Титов Андрей Николаевич");
            Check(nameMatcher.IsMatch("ТИТОВ\u00a0АНДРЕЙ НИКОЛАЕВИЧ") && !nameMatcher.IsMatch("Другой владелец"), "ФИО сопоставляется точно, с нормализацией пробелов/регистра");
            Check(new Settings().SingleCertificateExpected, "один сертификат: повтор ФИО на карточке разрешён");
            Check(launch.ChromiumSandbox == true, "песочница браузера включена, а не скрыто предупреждение");
            Console.WriteLine("UNIT TESTS: 19/19 PASS");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    public static async Task<int> RunBrowserTestsAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "kontur-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var log = new RunLog(root);
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chromium", Headless = true });
            string base64 = Convert.ToBase64String(CreateWorkbook());
            int passed = 0;
            foreach (string scenario in new[] { "dialog", "direct", "already-open", "popup-close" })
            {
                await using var context = await browser.NewContextAsync(new() { AcceptDownloads = true });
                IPage page = await context.NewPageAsync();
                await page.SetContentAsync(Fixture(scenario, base64));
                var settings = new Settings
                {
                    OutputDirectory = Path.Combine(root, scenario), UiTimeoutSeconds = 5,
                    ExportTimeoutSeconds = 15, TransferTimeoutSeconds = 20, FileNamePrefix = "TEST"
                };
                OutputResult result = await ExportFlow.RunAsync(context, page, settings, log);
                Check(File.Exists(result.Path) && result.Bytes > 0 && result.Path.EndsWith(".xlsx"), "browser: " + scenario);
                passed++;
            }
            // Отдельно закрываем источник ПОСЛЕ получения файла, но ДО SaveAsAsync.
            await using (var context = await browser.NewContextAsync(new() { AcceptDownloads = true }))
            {
                IPage page = await context.NewPageAsync();
                await page.SetContentAsync(Fixture("popup-close", base64));
                using var observer = new DownloadObserver(context, log);
                await page.GetByRole(AriaRole.Button, new() { Name = "Выгрузить в Excel" }).ClickAsync();
                IDownload download = await observer.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Check(await download.FailureAsync() == null, "popup-файл успешно получен");
                if (!download.Page.IsClosed) await download.Page.CloseAsync();
                Check(download.Page.IsClosed, "источник скачивания закрыт ДО SaveAsAsync");
                OutputResult result = await OutputFiles.SaveAsync(download, new Settings
                {
                    OutputDirectory = Path.Combine(root, "closed-source"), TransferTimeoutSeconds = 20
                }, log);
                Check(File.Exists(result.Path), "сохранение из уже закрытой вкладки при живом контексте");
                passed++;
            }
            string profile = Path.Combine(root, "persistent-profile");
            var launch = new BrowserTypeLaunchPersistentContextOptions { Channel = "chromium", Headless = true };
            await using (var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, launch))
            {
                await context.AddCookiesAsync(new[] { new Cookie
                {
                    Name = "remember", Value = "local-fixture", Domain = "kontur-fixture.test", Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(), Secure = true
                } });
            }
            await using (var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, launch))
            {
                var cookies = await context.CookiesAsync(new[] { "https://kontur-fixture.test/" });
                Check(cookies.Any(c => c.Name == "remember" && c.Value == "local-fixture"), "cookie восстановлена после перезапуска постоянного профиля");
                passed++;
            }
            // Проверяем наш новый запуск по ExecutablePath и разрешение расширений.
            // Здесь используется установленный тестовый Chromium, НЕ настоящий Яндекс.
            string extension = Path.Combine(root, "fixture-extension");
            Directory.CreateDirectory(extension);
            File.WriteAllText(Path.Combine(extension, "manifest.json"), """
                {"manifest_version":3,"name":"Kontur local fixture","version":"1.0","background":{"service_worker":"worker.js"}}
                """);
            File.WriteAllText(Path.Combine(extension, "worker.js"), "chrome.runtime.onInstalled.addListener(() => {});");
            var externalSettings = new Settings { OutputDirectory = Path.Combine(root, "external-executable"), ExportTimeoutSeconds = 15 };
            var externalOptions = YandexBrowser.LaunchOptions(externalSettings, playwright.Chromium.ExecutablePath, true);
            // Только тестовый Linux-контейнер не поддерживает песочницу Chromium.
            // В рабочем запуске Яндекса ChromiumSandbox остаётся true.
            if (!OperatingSystem.IsWindows()) externalOptions.ChromiumSandbox = false;
            externalOptions.Args = new[] { "--disable-extensions-except=" + extension, "--load-extension=" + extension };
            await using (var context = await playwright.Chromium.LaunchPersistentContextAsync(Path.Combine(root, "external-profile"), externalOptions))
            {
                IPage page = await context.NewPageAsync();
                ICDPSession cdp = await context.NewCDPSessionAsync(page);
                bool extensionLoaded = false;
                for (int i = 0; i < 80 && !extensionLoaded; i++)
                {
                    var targets = await cdp.SendAsync("Target.getTargets");
                    extensionLoaded = targets.HasValue && targets.Value.GetProperty("targetInfos").EnumerateArray()
                        .Any(t => t.GetProperty("url").GetString()?.StartsWith("chrome-extension://") == true);
                    if (!extensionLoaded) await Task.Delay(100);
                }
                await cdp.DetachAsync();
                Check(extensionLoaded, "запуск по ExecutablePath: MV3-расширение действительно загружено");
                await page.SetContentAsync(Fixture("dialog", base64));
                OutputResult result = await ExportFlow.RunAsync(context, page, externalSettings, log);
                Check(File.Exists(result.Path), "выгрузка при запуске по ExecutablePath с разрешёнными расширениями");
                passed++;
            }
            passed += await CertificateLoginTests.RunAsync(browser, root, log);
            Console.WriteLine($"BROWSER TESTS: {passed}/19 PASS (локальная модель, не Яндекс и не аккаунт Контура)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine("Тестовые данные: " + root); return 1; }
    }

    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    private static string Fixture(string mode, string base64)
    {
        string downloadJs = $$"""
            function sendFile() {
                const bytes = Uint8Array.from(atob('{{base64}}'), x => x.charCodeAt(0));
                const url = URL.createObjectURL(new Blob([bytes], {type:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'}));
                const a = document.createElement('a'); a.href=url; a.download='site-export.xlsx';
                document.body.append(a); a.click(); a.remove();
            }
            """;
        if (mode == "popup-close")
        {
            string popup = "<html><body><script>" + downloadJs + "setTimeout(()=>{sendFile();setTimeout(()=>window.close(),100);},150);</script></body></html>";
            string escaped = System.Text.Json.JsonSerializer.Serialize(popup);
            return $$"""
                <html><body><button onclick="openExport()">Выгрузить в Excel</button>
                <script>function openExport(){const p=window.open('','_blank');p.document.write({{escaped}});p.document.close();}</script></body></html>
                """;
        }
        string show = mode == "already-open" ? "block" : "none";
        string primary = mode == "direct" ? "sendFile()" : "document.getElementById('dlg').style.display='block'";
        return $$"""
            <html><body>
            <button onclick="{{primary}}">Выгрузить в Excel</button>
            <div id="dlg" role="dialog" style="display:{{show}}">
              <button onclick="sendFile();document.getElementById('dlg').style.display='none'">Выгрузить в Excel</button>
            </div><script>{{downloadJs}}</script></body></html>
            """;
    }

    public static byte[] CreateWorkbook()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Entry(string path, string xml)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
            Entry("[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
                """);
            Entry("_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            Entry("xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Test" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            Entry("xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
                """);
            Entry("xl/worksheets/sheet1.xml", """
                <?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Playwright test</t></is></c></row></sheetData></worksheet>
                """);
        }
        return memory.ToArray();
    }
}
