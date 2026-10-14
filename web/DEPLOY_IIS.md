# Развёртывание SnabDrive на Windows Server + IIS (БД на отдельном SQL Server)

Приложение — ASP.NET Core 9 / Blazor Server. Ниже — что поставить и как настроить,
чтобы сайт работал на отдельном Win-сервере с IIS, а база — на отдельном SQL Server.

---

## 1. Что установить на веб-сервер

| Компонент | Зачем | Где взять |
|---|---|---|
| **.NET 9 Hosting Bundle** | ASP.NET Core Runtime + модуль ANCM для IIS (обязательно именно *Hosting Bundle*, не только SDK) | dotnet.microsoft.com → .NET 9 → «Hosting Bundle» |
| **Роль IIS** + **WebSocket Protocol** | Blazor Server работает через SignalR/WebSocket — без WebSocket сайт не откроется | Server Manager → Add Roles and Features → Web Server (IIS) → Application Development Features → **WebSocket Protocol** |
| **Git / GitHub Desktop** | тянуть обновления репозитория | github.com |
| **Браузер для KonturExport** (Chromium/Яндекс) + сам `KonturExport` | выгрузка из Контур/ЕИС (Playwright) | см. раздел 7 |
| (опц.) **SSMS** | прогнать SQL-скрипт, проверить БД | Microsoft |

> Проверка, что Hosting Bundle встал: `C:\Program Files\IIS\Asp.Net Core Module\V2\` существует,
> в IIS появился «ASP.NET Core Module V2».

---

## 2. Публикация приложения

На машине с репозиторием (или на сервере после `git pull`):

```
cd web/src/SnabDrive.Web
dotnet publish -c Release -o C:\publish\snabdrive
```

Скопируйте содержимое `C:\publish\snabdrive` в папку сайта на сервере, напр. `C:\inetpub\snabdrive`.
В папке должен быть `web.config` (его генерирует publish — он подключает ASP.NET Core Module).

---

## 3. Сайт в IIS

1. **Application Pool**:
   - .NET CLR version = **No Managed Code** (приложение self-hosted через ANCM).
   - Managed pipeline = Integrated.
   - **Idle Time-out = 0** и **Start Mode = AlwaysRunning**, **Idle Time-out Action = Suspend** —
     иначе IIS «усыпит» приложение и фоновый планировщик Контур остановится (см. раздел 8).
2. **Site**: укажите физический путь `C:\inetpub\snabdrive`, привяжите пул, порт 80 (и 443 + SSL-сертификат, если нужен https).
3. **Переменная окружения** `ASPNETCORE_ENVIRONMENT = Production` — в `web.config`:
   ```xml
   <aspNetCore processPath="dotnet" arguments=".\SnabDrive.Web.dll" stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout">
     <environmentVariables>
       <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
     </environmentVariables>
   </aspNetCore>
   ```
   Тогда подхватится `appsettings.Production.json`.

---

## 4. Подключение к отдельному SQL Server

В `appsettings.Production.json`:
```json
"ConnectionStrings": {
  "Registry": "Server=ИМЯ_SQL_СЕРВЕРА;Database=snabdrive_db;User Id=snabdrive;Password=ПАРОЛЬ;TrustServerCertificate=True;MultipleActiveResultSets=True"
}
```
На SQL-сервере:
- **SQL Server Configuration Manager → TCP/IP = Enabled**, порт 1433; перезапустить службу SQL Server.
- **Брандмауэр**: открыть входящий TCP 1433 на SQL-сервере; на веб-сервере разрешить исходящие к SQL.
- **Логин**: проще всего — SQL-логин (не Windows), напр. `snabdrive` с доступом к `snabdrive_db`
  (db_owner). Если хотите Windows-auth — придётся выдать права учётке пула приложений (`IIS AppPool\ИмяПула`), это сложнее.
- Прогнать на SQL-сервере скрипт **`web/sql/snabdrive_full.sql`** (создаст `snabdrive_db` и все таблицы).

> `TrustServerCertificate=True` — чтобы не падать на самоподписанном сертификате шифрования соединения.

---

## 5. Права на папки (важно!)

Учётка пула приложений (`IIS AppPool\ИмяПула`) должна иметь:
- **Чтение** — папка сайта `C:\inetpub\snabdrive`.
- **Запись** — в подпапки, куда приложение пишет:
  - `wwwroot\uploads` — вложения закупок;
  - `logs` — логи stdout (если включены);
  - корень приложения — файл `kontur-schedule.json` (расписание пишется в ContentRootPath).
    Либо заранее создайте эти папки и дайте Modify.

Дать права: ПКМ по папке → Security → Add → `IIS AppPool\ИмяПула` → Modify.

---

## 6. Брандмауэр веб-сервера
- Разрешить входящие **80/443** (сайт).
- Разрешить исходящие в интернет (Bitrix24 REST, KonturExport, авторизация в ЕИС/Контур).

---

## 7. KonturExport (самая тонкая часть на сервере)

Экспортер использует браузер (Playwright) и требует **одноразовый интерактивный вход** (Login.cmd).
На сервере IIS работает как служба **без рабочего стола**, поэтому напрямую «под IIS» залогиниться в браузере не получится.

Рекомендуемая схема:
1. Залейте папку `KonturExport` целиком, напр. в `C:\KonturExport`.
2. По **RDP** (интерактивная сессия) запустите `Login.cmd` один раз под той учёткой, от которой будет работать экспорт; сессия/куки сохранятся в профиле.
3. В `appsettings.Production.json` укажите:
   ```json
   "Kontur": { "ExporterPath": "C:\\KonturExport\\KonturExport.exe", "OutputDirectory": "C:\\KonturExport\\out", "TimeoutMinutes": 15 }
   ```
4. Экспортер запускается планировщиком сайта. Чтобы у него был доступ к браузерному профилю и рабочему столу,
   **лучше вынести экспорт в отдельную Scheduled Task** под той же учёткой, что и логин, а сайт пусть только импортирует готовые xlsx из `OutputDirectory`.
   (Либо настройте пул/службу под доменной учёткой с профилем и разрешите «Load User Profile».)
5. Экспортер запускается **без `--headless`** (вы просили) — т.е. нужен доступ к графической сессии; на «голом» сервере это работает только в интерактивной/RDP-сессии.

> Если экспорт на сервере не заведётся — самый надёжный вариант: оставить выгрузку на машине с браузером,
> а на сервер только импортировать файлы.

---

## 8. Чтобы планировщик Контур не «засыпал»
Фоновый сервис (`KonturSchedulerService`) живёт внутри приложения. IIS по умолчанию глушит неактивный процесс.
Поэтому в пуле: **Idle Time-out = 0**, **Start Mode = AlwaysRunning**, плюс включите
**Application Initialization** (роль IIS) и `preloadEnabled = true` у сайта — тогда приложение стартует само и не останавливается.

---

## 9. Bitrix24
В `appsettings.Production.json` → `Bitrix:WebhookUrl` — URL входящего вебхука
(`https://вашпортал.bitrix24.ru/rest/1/код/`) с правом `im.notify`. Пусто — уведомления просто не шлются.

---

## 10. Обновления (GitHub Desktop / git)
Вариант А (проще): публикация на своей машине → копирование `publish` на сервер.
Вариант Б: на сервере `git pull` (или GitHub Desktop) → `dotnet publish -c Release -o C:\inetpub\snabdrive` → перезапуск пула (`iisreset` или Recycle).
После обновления, если менялась схема БД, — прогоните соответствующий SQL из `web/sql/`.

---

## 11. Итоговый чек-лист
- [ ] .NET 9 **Hosting Bundle** установлен
- [ ] IIS + **WebSocket Protocol** включён
- [ ] Приложение опубликовано, есть `web.config`
- [ ] Пул: No Managed Code, Idle Time-out 0, AlwaysRunning
- [ ] `ASPNETCORE_ENVIRONMENT=Production`
- [ ] `appsettings.Production.json`: строка к `snabdrive_db` на SQL-сервере
- [ ] SQL: TCP/IP 1433, firewall, логин, прогнан `snabdrive_full.sql`
- [ ] Права `IIS AppPool\...`: Modify на uploads/logs/корень
- [ ] Firewall: 80/443 вх., интернет исх.
- [ ] KonturExport + браузер + одноразовый Login (лучше отдельной Scheduled Task)
- [ ] Bitrix:WebhookUrl (если нужны уведомления)
- [ ] Первый вход под `admin` (пароль из SeedAdminPassword), затем сменить
