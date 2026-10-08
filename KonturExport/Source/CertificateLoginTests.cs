using Microsoft.Playwright;

namespace KonturExport;

internal static class CertificateLoginTests
{
    private const string Owner = "Титов Андрей Николаевич";
    public static async Task<int> RunAsync(IBrowser browser, string root, RunLog log)
    {
        int passed = 0;
        foreach (string mode in new[] { "button", "tab", "popup", "missing", "duplicate", "wrong-selector", "metadata", "repeated-card", "already-open" })
        {
            await using var context = await browser.NewContextAsync();
            IPage page = await context.NewPageAsync();
            var settings = new Settings { UiTimeoutSeconds = 2, CertificateOwnerName = Owner };
            if (mode == "duplicate") settings.SingleCertificateExpected = false;
            if (mode == "wrong-selector") settings.CertificateOwnerSelector = "#other";
            string ownerButton = $"<button onclick=\"window.selected='target'\">{Owner}</button>";
            if (mode == "tab") ownerButton = "<button onclick=\"window.selected='target'\">ТИТОВ&nbsp;АНДРЕЙ НИКОЛАЕВИЧ</button>";
            if (mode == "metadata") ownerButton = $"<button aria-label=\"{Owner}, действующий сертификат\" onclick=\"window.selected='target'\">Выбрать</button>";
            if (mode == "missing") ownerButton = "";
            if (mode == "duplicate") ownerButton += $"<button onclick=\"window.selected='second'\">{Owner}</button>";
            if (mode is "repeated-card" or "already-open")
                ownerButton = $"<div role='button' onclick=\"window.selected='target'\"><h2>{Owner}</h2><p>TEST_IDENTIFIER</p><small>{Owner}</small></div>";
            string list = ownerButton + "<button id='other' onclick=\"window.selected='other'\">Другой владелец</button>";
            if (mode == "popup")
            {
                string child = System.Text.Json.JsonSerializer.Serialize("<script>window.selected='';</script>" + list);
                await page.SetContentAsync($$"""
                    <button onclick="pick()">Сертификат</button><script>
                    function pick(){let p=window.open('','_blank');p.document.write({{child}});p.document.close();}
                    </script>
                    """);
            }
            else
            {
                string role = mode is "tab" or "already-open" ? "role='tab'" : "";
                string display = mode == "already-open" ? "block" : "none";
                await page.SetContentAsync($$"""
                    <script>window.selected='';window.tabClicks=0;</script>
                    <button {{role}} onclick="window.tabClicks++;document.getElementById('list').style.display='block'">Сертификат</button>
                    <div id='list' style='display:{{display}}'>{{list}}</div>
                    """);
            }
            CertificateSelection result = await CertificateLogin.SelectAsync(context, settings, log);
            bool expected = mode is "button" or "tab" or "popup" or "metadata" or "repeated-card" or "already-open";
            Assert(result.Selected == expected, "certificate: " + mode);
            IPage selectedPage = result.AuthPage ?? page;
            string? selected = await selectedPage.EvaluateAsync<string?>("window.selected");
            Assert(expected ? selected == "target" : selected != "other" && selected != "second" && selected != "target", "не выбирается посторонний/неоднозначный сертификат: " + mode);
            if (mode == "already-open") Assert(await page.EvaluateAsync<int>("window.tabClicks") == 0, "активная вкладка не нажимается повторно");
            passed++;
        }

        // Рабочая страница: новая вкладка, навигация той же, callback в главную.
        foreach (string navigation in new[] { "new-tab", "same-tab", "opener" })
        await using (var context = await browser.NewContextAsync())
        {
            bool authenticated = false;
            bool oldTabUsed = false;
            string host = "https://cert-login-fixture.test";
            string enter = navigation switch
            {
                "new-tab" => "window.open('/complete','_blank')",
                "same-tab" => "location.href='/complete'",
                _ => "window.opener.location.href='/complete';window.close()"
            };
            await context.RouteAsync(host + "/**", async route =>
            {
                string path = new Uri(route.Request.Url).AbsolutePath;
                if (path == "/complete")
                {
                    authenticated = true;
                    await route.FulfillAsync(new() { Status = 302, Headers = new Dictionary<string, string> { ["location"] = host + "/Grid" } });
                    return;
                }
                if (path == "/old-favorites") oldTabUsed = true;
                string html = path switch
                {
                    "/" => "<button onclick=\"window.open('/login','_blank')\">Войти</button>",
                    "/login" => $"<button role='tab'>Сертификат</button><div role='button' onclick=\"{enter}\"><h2>{Owner}</h2><small>{Owner}</small></div>",
                    "/old" => "<a href='/old-favorites'>Избранное</a><button>Выгрузить в Excel</button>",
                    "/old-favorites" => "<button>Выгрузить в Excel</button>",
                    "/Grid" when authenticated => "<a href='/favorites'>Избранное</a>",
                    "/favorites" when authenticated => "<button>Выгрузить в Excel</button>",
                    _ => "<p>Не авторизован</p>"
                };
                await route.FulfillAsync(new() { Status = 200, ContentType = "text/html; charset=utf-8", Body = "<html><body>" + html + "</body></html>" });
            });
            string folder = Path.Combine(root, "certificate-auth-flow-" + navigation);
            Directory.CreateDirectory(folder);
            var settings = new Settings
            {
                StartUrl = host + "/", ApplicationUrl = host + "/Grid", ProfileDirectory = Path.Combine(folder, "profile"),
                UiTimeoutSeconds = 5, CertificateLoginTimeoutSeconds = 10
            };
            IPage startupBlank = await context.NewPageAsync();
            IPage oldTab = await context.NewPageAsync();
            await oldTab.GotoAsync(host + "/old");
            IPage work = await new LoginFlow(context, settings, log).GetAuthenticatedPageAsync(forceLogin: true, headless: false);
            Assert(authenticated && !oldTabUsed && work != oldTab && work.Url.EndsWith("/favorites") && File.Exists(settings.ProfileDirectory + ".ready"),
                "рабочая страница после сертификата: " + navigation + "; старое Избранное игнорируется");
            Assert(startupBlank.Url.StartsWith(host) && !context.Pages.Any(p => p.Url == "about:blank"), "стартовая пустая вкладка использована, лишняя about:blank не оставлена: " + navigation);
            if (navigation == "same-tab") Assert(context.Pages.Count == 3, "после Титова не создана лишняя третья рабочая вкладка");
            passed++;
        }
        Console.WriteLine($"CERTIFICATE WEB TESTS: {passed}/12 PASS (локальная HTML-модель, без сертификатов/ключей)");
        return passed;
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
