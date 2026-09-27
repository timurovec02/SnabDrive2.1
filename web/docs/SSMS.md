# Подключение проекта к SQL Server через SSMS

Цель: открыть базу `SnabDriveDB` в SQL Server Management Studio, увидеть существующие таблицы
реестра и новые веб-таблицы (Identity, аудит, права на колонки), которые создаёт приложение.

Проект **не хранит** пароли в репозитории, поэтому ниже везде, где стоит `REPLACE_ME`,
подставьте свои значения.

---

## Шаг 0. Что использует проект

- База: **SnabDriveDB** (та же, с которой работает WPF-клиент).
- Сервер из WPF-версии: `192.168.23.163,1433` (если SQL Server стоит на этой машине).
  Если сервер у вас локально — используйте `localhost` или `.`.
- Новые веб-таблицы создаются **EF-миграцией** контекста `AppIdentityDbContext`
  при старте приложения (`Database:AutoMigrateIdentitySchema = true`)
  или командой `dotnet ef database update`.

---

## Шаг 1. Подключитесь в SSMS

1. Откройте SSMS → окно **Connect to Server**.
2. **Server name**:
   - если SQL Server на этом же компьютере: `localhost` (или `.`);
   - если на сервере из сети: `192.168.23.163,1433` (через запятую — сервер,порт).
3. **Authentication**:
   - *SQL Server Authentication* — логин/пароль учётки, у которой есть доступ к `SnabDriveDB`
     (в WPF использовались `Admin` / `1234` — подставьте свои);
   - либо *Windows Authentication*, если ваша Windows-учётка имеет права.
4. **Connect**.

Если подключение не идёт:
- включён ли у SQL Server протокол **TCP/IP** и порт **1433** (SQL Server Configuration Manager
  → SQL Server Network Configuration → Protocols → TCP/IP → Enabled = Yes, затем перезапустить службу);
- не блокирует ли брандмауэр порт 1433;
- для удалённого сервера убедитесь, что имя указано как `хост,порт`.

---

## Шаг 2. Убедитесь, что база и таблицы реестра на месте

В Object Explorer: **Databases → SnabDriveDB → Tables** (кнопка ⟳ Refresh, если не видно).

Должны быть существующие таблицы (их проект читает и **не изменяет**):

```
dbo.Regedit
dbo.ArchiveRegedit
dbo.User
dbo.B2BStatus
dbo.TypeOfPurchase
dbo.ExecutionStatus
dbo.CellColors
```

Если базы `SnabDriveDB` ещё нет — сначала разверните её из вашей WPF-системы:
веб-приложение существующие таблицы **не создаёт**.

---

## Шаг 3. Создайте веб-таблицы (миграция)

Вариант А — автоматически при старте приложения (рекомендуется):

1. Настройте подключение (см. Шаг 4), затем запустите:
   ```
   cd web
   dotnet run --project src/SnabDrive.Web
   ```
2. При старте приложение применит миграцию и создаст веб-таблицы.

Вариант Б — вручную из CLI (если хотите контролировать момент):

```
cd web/src/SnabDrive.Web
dotnet tool install --global dotnet-ef        # один раз
dotnet ef migrations add InitialWebSchema --context AppIdentityDbContext
dotnet ef database update --context AppIdentityDbContext
```

После этого в SSMS в **SnabDriveDB → Tables** появятся новые таблицы:

```
dbo.AspNetUsers  dbo.AspNetRoles  dbo.AspNetUserRoles  dbo.AspNetUserClaims
dbo.AspNetUserLogins  dbo.AspNetUserTokens  dbo.AspNetRoleClaims
dbo.AuditLog
dbo.UserColumnPermission
dbo.__EFMigrationsHistory
```

В `dbo.AspNetUsers` добавлены свои колонки: `DisplayName`, `LegacyUserId`,
`LastLoginUtc`, `LastSeenUtc`, `IsBlocked`, `ColumnAccessMode`.

> Существующие таблицы реестра миграция **не трогает** — они в другом контексте
> (`RegistryDbContext`), у которого миграций нет.

---

## Шаг 4. Скажите приложению, куда подключаться

Скопируйте шаблон и заполните свои значения:

```
copy src\SnabDrive.Web\appsettings.SqlServer.example.json src\SnabDrive.Web\appsettings.Production.json
```

В `appsettings.Production.json` замените `REPLACE_ME`:

```json
"ConnectionStrings": {
  "Registry": "Server=192.168.23.163,1433;Database=SnabDriveDB;User Id=Admin;Password=ВАШ_ПАРОЛЬ;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
},
"Database": {
  "Provider": "SqlServer",
  "AutoMigrateIdentitySchema": true,
  "ImportLegacyUsers": true
}
```

`appsettings.Production.json` **не коммитится** (внесён в `.gitignore`).

Альтернатива без файла — переменная окружения:

```
setx ConnectionStrings__Registry "Server=192.168.23.163,1433;Database=SnabDriveDB;User Id=Admin;Password=ВАШ_ПАРОЛЬ;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

Запуск в нужном окружении:

```
dotnet run --project src/SnabDrive.Web --environment Production
```

---

## Шаг 5. Проверка в SSMS после старта

1. В Object Explorer нажмите ⟳ **Refresh** на узле **Tables** базы `SnabDriveDB`.
2. Откройте `dbo.AspNetUsers` → **Select Top 1000 Rows** — там будет созданный
   администратор (логин из `Database:SeedAdminLogin`) и, при `ImportLegacyUsers: true`,
   импортированные пользователи из старой таблицы `[User]`.
3. `dbo.AuditLog` — журнал изменений; `dbo.UserColumnPermission` — права на колонки.

Если в логе приложения ошибка «В сборке нет миграций веб-схемы…» — значит, миграции ещё не
сгенерированы; выполните команды из Шага 3 (вариант Б).

---

## Частые проблемы

| Симптом | Причина / решение |
|---|---|
| «A network-related or instance-specific error…» | неверный сервер/порт; не включён TCP/IP; брандмауэр |
| «Login failed for user …» | неверный логин/пароль или у учётки нет прав на `SnabDriveDB` |
| «The certificate chain was issued by an authority…» | добавьте `TrustServerCertificate=True` в строку подключения |
| Таблицы не появились | убедитесь, что `Provider = SqlServer` и `AutoMigrateIdentitySchema = true`; смотрите лог |
