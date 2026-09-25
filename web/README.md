# SnabDrive Web — реестр закупок в браузере

Веб-версия реестра SnabDrive: ASP.NET Core 8 + Blazor Server + Web API + SignalR + ASP.NET Core Identity.
Работает с **той же базой SnabDriveDB**, что и WPF-клиент — существующие таблицы не изменяются.

---

## 1. Что уже сделано

| Этап | Статус | Что именно |
|---|---|---|
| 1. Решение и БД | ✅ | Blazor Server + EF Core, два DbContext, переключение SqlServer/SQLite |
| 2. API | ✅ | CRUD реестра, архив, справочники, аудит, пользователи, онлайн, CSV-экспорт, Swagger |
| 3. UI реестра | ✅ | Таблица «как Excel»: поиск, сортировка, фильтры по колонкам, пагинация, настройка колонок, формы, подсветка ячеек |
| 4. Realtime | ✅ | Шина событий + SignalR-хаб: таблицы обновляются сами, без кнопки «Обновить» |
| 5. Авторизация и роли | ✅ | Identity, cookie-вход, роли Admin/Operator, блокировка после 5 попыток, страница пользователей |
| Журналирование | ✅ | Кто/когда/что изменил, с диффом «было → стало» |

### Экраны

- `/` — обзор: счётчики, кто онлайн, последние события;
- `/registry` — реестр (основная таблица);
- `/archive` — архив (только Admin);
- `/dictionaries` — справочники (только Admin);
- `/users` — пользователи и роли (только Admin);
- `/audit` — журнал изменений (только Admin);
- `/login` — вход;
- `/swagger` — документация API (в Development).

---

## 2. Быстрый старт (без SQL Server)

По умолчанию приложение настроено на локальный SQLite-файл, чтобы его можно было запустить сразу:

```bash
cd web
dotnet restore
dotnet run --project src/SnabDrive.Web
```

Откроется `http://localhost:5080`. Логин `admin`, пароль `ChangeMe-123!`
(задаются в `appsettings.json` → `Database:SeedAdminLogin` / `SeedAdminPassword`).

Прогон тестов:

```bash
cd web
dotnet test
```

---

## 3. Подключение к боевой базе SnabDriveDB

1. Скопируйте `src/SnabDrive.Web/appsettings.SqlServer.example.json`
   в `appsettings.Production.json` (или используйте user-secrets / переменную окружения
   `ConnectionStrings__Registry`).
2. Впишите реальные `User Id` / `Password`.
3. Поставьте `"Database": { "Provider": "SqlServer", "EnsureLegacySchema": false }`.

```bash
dotnet user-secrets init --project src/SnabDrive.Web
dotnet user-secrets set "ConnectionStrings:Registry" "Server=192.168.23.163,1433;Database=SnabDriveDB;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=True"
dotnet user-secrets set "Database:Provider" "SqlServer"
```

### Важно про схему базы

- Таблицы `Regedit`, `ArchiveRegedit`, `User`, `B2BStatus`, `TypeOfPurchase`,
  `ExecutionStatus`, `CellColors` описаны в `RegistryDbContext` **один в один** с WPF-моделями.
  Этот контекст **не имеет миграций** и никогда не вызывает `Database.Migrate()` — боевая схема не трогается.
- Новые таблицы (`AspNet*` для Identity и `AuditLog`) живут в `AppIdentityDbContext` и создаются
  **только им**. При первом старте: если миграции есть — применяется `Migrate`, если нет — `EnsureCreated`.

### Миграция для веб-таблиц

Миграции в репозиторий не положены намеренно: их нужно сгенерировать под вашу версию EF Core
и под реального провайдера. Один раз выполните:

```bash
cd web/src/SnabDrive.Web
dotnet ef migrations add InitialWebSchema --context AppIdentityDbContext
```

После этого при старте веб-таблицы создадутся/обновятся автоматически
(`Database:AutoMigrateIdentitySchema = true`).

> `EnsureLegacySchema` для SQL Server по умолчанию `false` — приложение только проверяет наличие
> ключевых таблиц и пишет предупреждение в лог, если что-то не найдено.

---

## 4. Импорт старых пользователей

Пароли в таблице `[User]` хранились открытым текстом. Веб-версия использует Identity (хэши PBKDF2),
поэтому выполняется **однократный импорт**:

```json
"Database": { "ImportLegacyUsers": true }
```

При старте каждый логин из `[User]` переносится в `AspNetUsers`, пароль берётся как есть
и сразу хэшируется, роль назначается по признаку `IsAdmin` (Admin / Operator).
Повторно существующие логины не трогаются. После импорта флаг лучше вернуть в `false`.

---

## 5. Роли и права

| Возможность | Admin | Operator |
|---|---|---|
| Смотреть реестр, искать, выгружать CSV | ✅ | ✅ |
| Создавать / изменять / удалять записи | ✅ | ✅ |
| Подсветка ячеек | ✅ | ✅ |
| Архивация и возврат из архива | ✅ | ❌ |
| Справочники | ✅ | ❌ |
| Пользователи и роли | ✅ | ❌ |
| Журнал изменений | ✅ | ❌ |

Политики объявлены в `Services/DatabaseBootstrapper.cs` (`AppPolicies`) и применяются
и к страницам (`[Authorize(Policy = ...)]`), и к контроллерам API.

Безопасность:
- cookie `SnabDrive.Auth`, `HttpOnly`, `SameSite=Lax`, скользящее истечение 8 часов;
- `FallbackPolicy = RequireAuthenticatedUser` — закрыто всё, кроме `/login`;
- antiforgery-токен на формах входа и выхода;
- `returnUrl` проверяется на локальность (защита от open redirect);
- блокировка учётки на 10 минут после 5 неудачных попыток;
- авторизация обязательна и для Web API, и для SignalR-хаба (`[Authorize]`).

---

## 6. Realtime: как это устроено

Изменение записи проходит через `IRegistryNotifier` (`Services/SignalRRegistryNotifier.cs`),
который публикует событие **в два канала**:

1. **`IRegistryEventBus`** — внутрипроцессная шина. Blazor-компоненты (`Registry.razor`,
   `ArchivePage.razor`, `Home.razor`, `PresenceWidget.razor`) подписаны на неё и обновляют
   таблицу мгновенно по тому же WebSocket-соединению, которое уже есть у Blazor Server.
   Отдельного подключения на пользователя не требуется.
2. **SignalR-хаб `/hubs/registry`** — метод `registryChanged` уходит всем подключённым клиентам.
   Это канал для внешних потребителей: JS-виджетов, интеграций, мобильных клиентов.

Онлайн-пользователи (`IPresenceService`) собираются из двух источников — Blazor-циркулов
(регистрация в `MainLayout`, снятие при закрытии вкладки) и SignalR-подключений.
Список рассылается событием `presenceChanged` и доступен по `GET /api/presence`.

Браузерные уведомления через SignalR включаются отдельно — см. `wwwroot/js/realtime.js`
(нужен `@microsoft/signalr` в `wwwroot/lib/signalr/`).

---

## 7. Web API

| Метод | Маршрут | Права |
|---|---|---|
| GET | `/api/regedit` | авторизован |
| GET | `/api/regedit/export` | авторизован |
| GET | `/api/regedit/{id}` | авторизован |
| POST / PUT / DELETE | `/api/regedit`, `/api/regedit/{id}` | Admin, Operator |
| POST | `/api/regedit/{id}/archive` | Admin |
| PUT | `/api/regedit/{id}/cell-color` | Admin, Operator |
| GET | `/api/regedit/lookup` | авторизован |
| GET | `/api/archive` | Admin |
| POST | `/api/archive/{id}/restore` | Admin |
| GET/POST/PUT/DELETE | `/api/dictionaries/*` | чтение — все, запись — Admin |
| GET | `/api/audit` | Admin |
| GET/POST/PUT/DELETE | `/api/users` | Admin |
| GET | `/api/presence` | авторизован |

Пример выборки:

```
GET /api/regedit?search=газ&sortBy=NMCK&sortDescending=true&page=1&pageSize=50&f[Customer]=газ&f[NMCK]=>=1000000
```

---

## 8. Отличия от WPF-версии (осознанные)

1. **Поиск без учёта регистра** — в WPF оба значения приводились к нижнему регистру.
   Здесь то же самое (`LOWER(...)`), чтобы результат не зависел от collation базы.
   Обратная сторона: `LOWER()` не использует индекс. Для объёмов реестра это незаметно;
   если записей станет десятки тысяч — добавим полнотекстовый индекс.
2. **Нет инлайн-редактирования ячейки** — вместо него модальная форма (пункт 3.1 ТЗ допускал оба варианта).
3. **Подсветка ячеек** хранится в той же таблице `CellColors` с теми же ключами
   (имена свойств: `Customer`, `BiddingDate`, …), поэтому старые цвета продолжают работать.
   Как и в WPF, цвета привязаны к `Regedit.Id`, поэтому при архивации они теряются вместе со строкой.
4. **Фильтр по сумме** поддерживает операторы: `>=1000000`, `<500000`, `=123`.

---

## 9. Структура

```
web/
├── SnabDrive.Web.sln
├── src/SnabDrive.Web/
│   ├── Program.cs                  # DI, авторизация, конвейер, старт БД
│   ├── AccountEndpoints.cs         # вход/выход (cookie + antiforgery)
│   ├── Api/                        # контроллеры Web API
│   ├── Components/                 # Blazor: страницы, layout, общие компоненты
│   ├── Data/                       # DbContext, сущности, ApplicationUser, бутстрап БД
│   ├── Domain/                     # DTO, запросы, описание колонок, события
│   ├── Hubs/RegistryHub.cs         # SignalR
│   ├── Services/                   # бизнес-логика: реестр, справочники, аудит, онлайн
│   └── wwwroot/                    # css, js
└── tests/SnabDrive.Web.Tests/      # xUnit: сервисы, валидация, экспорт, онлайн
```

---

## 10. Что дальше (не сделано)

- [ ] Миграция `InitialWebSchema` — генерируется на машине с доступом к вашей EF Core;
- [ ] Настройка колонок сохраняется между сессиями (сейчас — только в рамках открытой вкладки);
- [ ] Браузерные уведомления SignalR (нужен `@microsoft/signalr` в `wwwroot/lib/`);
- [ ] Экспорт в XLSX (сейчас CSV);
- [ ] История версий одной записи (сейчас — лента изменений в журнале);
- [ ] Docker-образ и публикация на IIS.
