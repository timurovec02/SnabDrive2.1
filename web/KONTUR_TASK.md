# Запуск экспортёра Контура через Планировщик заданий ( headed-браузер на сервере)

## Зачем
IIS работает в **Session 0** без интерактивного рабочего стола, поэтому headed-браузер
(KonturExport без `--headless`) оттуда не запускается, а headless Контур блокирует.
Решение: экспортёр запускается **отдельной задачей Планировщика заданий Windows**
в интерактивной сессии залогиненного пользователя. Сайт (и кнопка «Выгрузить сейчас»,
и внутренний планировщик) лишь **триггерит** эту задачу и подхватывает готовый Excel.

## Как это работает
1. Сайт пишет аргументы в файл `export-request.txt` (параметр `Kontur:RequestFile`):
   `--no-pause --favorites --output "C:\KonturExport\out\favorites"` (или `--templates`).
2. Сайт выполняет `schtasks /run /tn "SnabDriveKontur"`.
3. Задача выполняет обёртку `run-export.cmd`, которая читает `export-request.txt`
   и запускает `KonturExport.exe` **в интерактивной сессии** (браузер открывается нормально).
4. Сайт ждёт появления нового `.xlsx` в папке выгрузки и импортирует его.

## Настройка (один раз, на сервере)

### 1. Интерактивная сессия
Пользователь, от которого работает задача, должен быть **залогинен интерактивно**
(иначе браузеру некуда рисовать). Настройте **автовход** на сервере под этой учёткой
(netplwiz → снять «требовать ввод имени и пароля» → задать пароль) или держите активную
консольную сессию. RDP-сессию не отключайте полностью (отключение может остановить отрисовку).

### 2. Обёртка
Скопируйте `kontur/run-export.cmd` на сервер (например, в `C:\KonturExport\run-export.cmd`)
и укажите в нём путь к `KonturExport.exe` (строка `set "EXE=..."`).
Файл запроса `export-request.txt` обёртка ищет рядом с `.exe`.

### 3. Создать задачи Планировщика (запуск .exe напрямую, с аргументами)
Создайте **две задачи** — для избранного и для ловушки. В действии каждой укажите
**сам .exe и полные аргументы** (обязательно `--output` в ту папку, куда смотрит сайт!).

`taskschd.msc` → **Create Task**:

**Задача «Избранное»** — имя `SnabDriveKontur`:
- General: «Run only when user is logged on», интерактивный пользователь, «Run with highest privileges».
- Actions → New:
  - Program/script: `C:\KonturExport\KonturExport.exe`
  - Add arguments: `--no-pause --favorites --output "C:\KonturExport\out\favorites"`
  - Start in: `C:\KonturExport`
- Triggers: не добавлять. Conditions: снять «only if on AC power».

**Задача «Контур ловушка»** — имя `SnabDriveKonturTemplates`:
- То же, но аргументы: `--no-pause --templates --output "C:\KonturExport\out\templates"`

⚠️ Папки в `--output` должны **точно совпадать** с тем, куда смотрит сайт:
сайт ищет файл в `OutputDirectory\favorites` и `OutputDirectory\templates`.
При `OutputDirectory = C:\KonturExport\out` это `C:\KonturExport\out\favorites` и `...\templates`.

Проверить вручную: `schtasks /run /tn "SnabDriveKontur"` — откроется браузер и Excel
ляжет в `C:\KonturExport\out\favorites`.

> Обёртка `run-export.cmd` (вариант с файлом запроса) — альтернатива; если используете
> .exe напрямую с аргументами, обёртка и `RequestFile` не нужны.

### 4. Дать пулу IIS право запускать задачу
Учётка пула (`IIS AppPool\snabdrive`) должна иметь право выполнять задачу. Проще всего —
выдать ей права на папку задачи или запустить от имени с достаточными правами. Если
`schtasks /run` из-под пула возвращает «Отказано в доступе», выполните на сервере:
```
schtasks /change /tn "SnabDriveKontur" /RU "IIS AppPool\snabdrive"
```
или создайте задачу от имени учётки, которая может запускаться из пула, и выдайте
`IIS AppPool\snabdrive` право «Log on as a batch job» (secpol.msc → Local Policies →
User Rights Assignment).

### 5. Настройки приложения (`appsettings.Production.json`)
```json
"Kontur": {
  "ExporterPath": "C:\\KonturExport\\KonturExport.exe",
  "OutputDirectory": "C:\\KonturExport\\out",
  "TimeoutMinutes": 15,
  "TaskName": "SnabDriveKontur",
  "TaskNameTemplates": "SnabDriveKonturTemplates",
  "RequestFile": "C:\\KonturExport\\export-request.txt"
}
```
- `TaskName` — задача для избранного, `TaskNameTemplates` — для ловушки (п.3).
  Если `TaskNameTemplates` пусто — для шаблонов используется `TaskName`.
- `OutputDirectory` — база; сайт ищет файлы в `OutputDirectory\favorites` и `\templates`
  (должно совпадать с `--output` в задачах).

### 6. Вход в Контур
Выполните `Login.cmd` **под тем же интерактивным пользователем** (от которого работает задача),
чтобы сессия Контура (`%LOCALAPPDATA%\kontur.export`) была доступна задаче.

## Проверка
1. Откройте «Избранное Контур» → «Выгрузить сейчас».
2. В логе планировщика (`/kontur-scheduler`) должно быть:
   «Аргументы для задачи записаны…», «schtasks /run…», «Задача выполнилась, появился файл…».
3. Если «Файл выгрузки не появился» — проверьте: задача создана и запускается вручную,
   пользователь залогинен интерактивно, выполнен `Login.cmd`, у пула есть право на запуск задачи.

## Если кнопка и планировщик нужны одновременно
Оба сценария идут через один код (`RunAsync`) и одну задачу. Встроенный планировщик
(`/kontur-scheduler`) и кнопка используют общий механизм, поэтому настроить задачу
достаточно один раз — работают оба.
