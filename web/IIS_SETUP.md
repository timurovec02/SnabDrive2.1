# Полная настройка IIS для SnabDrive (Windows Server)

Пошагово: от установки роли до работающего сайта. Приложение — ASP.NET Core 9 / Blazor Server.
Будем считать: сайт кладём в `C:\inetpub\snabdrive`, БД `snabdrive_db` — на отдельном SQL Server.

---

## Шаг 0. Порядок действий (важно)
1. Установить роль **IIS** (+ WebSocket, Application Initialization).
2. Установить **.NET 9 Hosting Bundle**.
3. `iisreset`.
4. Опубликовать приложение в папку.
5. Создать пул и сайт в IIS.
6. Дать права на папки.
7. Проверить.

---

## Шаг 1. Установить IIS и нужные компоненты

### Вариант А — PowerShell (от админа, быстрее всего)
```powershell
Install-WindowsFeature -Name `
  Web-Server, Web-WebServer, Web-Common-Http, Web-Static-Content, Web-Default-Doc, `
  Web-Dir-Browsing, Web-Http-Errors, Web-Http-Redirect, Web-Security, Web-Filtering, `
  Web-Request-Monitor, Web-Http-Logging, Web-Stat-Compression, `
  Web-App-Dev, Web-ISAPI-Ext, Web-ISAPI-Filter, `
  Web-AppInit,            # Application Initialization — чтобы приложение не «засыпало»
  Web-WebSockets,         # WebSocket Protocol — ОБЯЗАТЕЛЬНО для Blazor Server
  Web-Mgmt-Tools, Web-Mgmt-Console `
  -IncludeManagementTools
```

### Вариант Б — Server Manager
Add Roles and Features → **Web Server (IIS)**. Внутри обязательно отметить:
- Application Development Features → *(не обязательно ASP.NET 4.x, но не помешает)*
- **Application Development Features / Web Application Initialization** → **Application Initialization**
- **Application Development Features** → **WebSocket Protocol** (в разделе Web Server → Application Development Features)
- Management Tools → **IIS Management Console**

> Критично: **WebSocket Protocol**. Blazor Server держит соединение через WebSocket.
> Без него сайт откроется, но страницы не будут работать (вечная загрузка).

---

## Шаг 2. .NET 9 Hosting Bundle
Скачайте и установите **.NET 9 Hosting Bundle** (не просто SDK!) — он ставит
ASP.NET Core Runtime и **ASP.NET Core Module V2** для IIS.
После установки:
```powershell
iisreset
```
Проверка: `C:\Program Files\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll` существует,
в IIS Manager → (сервер) → Modules появился **AspNetCoreModuleV2**.

---

## Шаг 3. Опубликовать приложение
На машине с репозиторием (или на сервере после `git pull`):
```
cd web/src/SnabDrive.Web
dotnet publish -c Release -o C:\publish\snabdrive
```
Скопируйте **всё содержимое** `C:\publish\snabdrive` в `C:\inetpub\snabdrive`.
Там обязательно должен быть `web.config` (его создал publish).

---

## Шаг 4. Пул приложений (Application Pool)

IIS Manager → **Application Pools** → **Add Application Pool**:
- Name: `snabdrive`
- .NET CLR version: **No Managed Code**  ← обязательно (приложение self-hosted)
- Managed pipeline mode: **Integrated**

Затем ПКМ по пулу → **Advanced Settings**:
- **Start Mode = AlwaysRunning**
- **Idle Time-out (minutes) = 0**
- **Idle Time-out Action = Suspend** (или Terminate — не важно при Time-out 0)
- **Process Model → Identity** = `ApplicationPoolIdentity` (по умолчанию).
  *Если экспорт Контур должен работать под конкретной учёткой — задайте её здесь (Custom account).*
- **Process Model → Load User Profile = True** (нужно, если экспортер/браузер работает под этим пулом)

> Это удерживает приложение (и фоновый планировщик Контур) постоянно запущенным.

---

## Шаг 5. Сайт (Website)

IIS Manager → **Sites** → **Add Website**:
- Site name: `SnabDrive`
- Application pool: **snabdrive** (выбрать созданный)
- Physical path: `C:\inetpub\snabdrive`
- Binding: Type `http`, Port `80` (или `8080`, если 80 занят), Host name — по желанию
- (для https) добавьте Binding `https` + выберите SSL-сертификат

Затем ПКМ по сайту → **Manage Website → Advanced Settings**:
- **Preload Enabled = True** (вместе с Application Initialization даёт автозапуск)

---

## Шаг 6. web.config (среда Production)

В папке сайта `C:\inetpub\snabdrive\web.config` должно быть примерно так
(publish создаёт основу — добавьте `environmentVariables`):
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\SnabDrive.Web.dll"
                  stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout"
                  hostingModel="inprocess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```
- `ASPNETCORE_ENVIRONMENT=Production` → подхватится `appsettings.Production.json` (строка к БД, Bitrix, Kontur).
- `stdoutLogEnabled=true` → ошибки старта пишутся в `C:\inetpub\snabdrive\logs\stdout_*.log`
  (после успешной настройки можно поставить `false`).

---

## Шаг 7. Права на папки

Создайте папки для записи и дайте права учётке пула `IIS AppPool\snabdrive`:
```powershell
cd C:\inetpub\snabdrive
mkdir logs, wwwroot\uploads -Force

icacls "C:\inetpub\snabdrive" /grant "IIS AppPool\snabdrive:(OI)(CI)M" /T
```
`(OI)(CI)M` — Modify с наследованием на вложенные папки. Это нужно, потому что приложение:
- пишет вложения в `wwwroot\uploads`;
- пишет логи в `logs`;
- хранит `kontur-schedule.json` в корне приложения (ContentRootPath).

---

## Шаг 8. Брандмауэр веб-сервера
- Разрешить **входящие** TCP **80** и **443**.
- Разрешить **исходящие** в интернет (Bitrix24 REST, KonturExport, вход в ЕИС/Контур).
- На **SQL-сервере** открыть входящие **1433** (см. DEPLOY_IIS.md, раздел про SQL).

---

## Шаг 9. Проверка
1. `iisreset`.
2. На сервере откройте `http://localhost` (или `http://localhost:8080`) — должна открыться страница входа.
3. Если ошибка **500.x / 502.x** — смотрите `logs\stdout_*.log` и **Event Viewer → Windows Logs → Application** (источник IIS AspNetCore Module).
4. Убедитесь, что приложение подключилось к БД: при первом старте с `SeedRolesAndAdmin=true` создадутся роли и админ (`admin` / пароль из `appsettings.Production.json`).

---

## Частые проблемы

| Симптом | Причина / решение |
|---|---|
| **500.30 / 500.31**, в логе «framework not found» | Не установлен **.NET 9 Hosting Bundle** (или только SDK). Поставить Hosting Bundle + `iisreset`. |
| **502.3** | Модуль ANCM не видит приложение: неверный `processPath`/`arguments` в web.config; перепубликуйте. |
| **502.5** | Приложение не стартует: смотрите stdout-лог; часто — неверная строка подключения или нет доступа к папке. |
| Страница грузится и «виснет», нет интерактива | Не включён **WebSocket Protocol** (Web-WebSockets). Включить + `iisreset`. |
| **HTTP Error 500.19** | Повреждён/не найден web.config; проверьте секцию `aspNetCore`. |
| Ошибка «отказано в доступе» при загрузке файла / записи расписания | Не дали **Modify** на `uploads`/корень для `IIS AppPool\snabdrive` (Шаг 7). |
| Планировщик Контур не срабатывает по времени | Пул «уснул»: поставьте **Idle Time-out=0**, **Start Mode=AlwaysRunning**, **Preload Enabled=True**. |

---

## Обновление сайта
Чтобы заменить файлы без ошибок «файл занят»:
1. В корень сайта положите пустой файл `app_offline.htm` (IIS остановит приложение).
2. Скопируйте новую публикацию.
3. Удалите `app_offline.htm`.
Либо: Recycle пула после копирования. Если менялась схема БД — прогоните нужный SQL из `web/sql/`.

---

## Короткий чек-лист
- [ ] IIS + **WebSocket** + **Application Initialization**
- [ ] **.NET 9 Hosting Bundle** + `iisreset`
- [ ] Приложение в `C:\inetpub\snabdrive`, есть `web.config`
- [ ] Пул: **No Managed Code**, **AlwaysRunning**, **Idle 0**
- [ ] Сайт: путь + пул + **Preload Enabled=True**
- [ ] `ASPNETCORE_ENVIRONMENT=Production`
- [ ] `IIS AppPool\snabdrive`: **Modify** на uploads/logs/корень
- [ ] Firewall 80/443
- [ ] Открылся `http://localhost`, вошли под `admin`
