#!/usr/bin/env bash
# Дымовой прогон SnabDrive.Web в CI:
#   1) SQL Server в docker + схема из web/sql (create_SnabDriveDB.sql, updates_2026.sql);
#   2) запуск опубликованного сайта (publish/);
#   3) вход администратором и проверка ВСЕХ страниц;
#   4) негативные проверки: анонимус без входа и пользователь без прав.
# Запускается из .github/workflows/ci.yml (job «smoke»).
# Диагностика: сообщения уходят в annotations (::error::) и GITHUB_STEP_SUMMARY,
# логи приложения/SQL выгружаются артефактом smoke-diagnostics.
set -euo pipefail

SA_PASSWORD="${SA_PASSWORD:?SA_PASSWORD is required}"
SEED_PASSWORD="${SEED_PASSWORD:?SEED_PASSWORD is required}"
PROBE_PASSWORD="${PROBE_PASSWORD:-Ci#Probe-2026-Pw}"
PORT="${PORT:-5088}"
APP_URL="http://localhost:$PORT"
WORK="${GITHUB_WORKSPACE:-$(cd "$(dirname "$0")/../.." && pwd)}"
JAR=/tmp/snab-cookies.txt
PAGE=/tmp/snab-page.html

note() { echo "=== $1 ==="; }

fail() {
    echo "FAIL: $1" >&2
    echo "::error::smoke: $1"
    if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
        echo "### ❌ Дымовой прогон: $1" >> "$GITHUB_STEP_SUMMARY"
    fi
    echo "--- последние строки лога приложения ---" >&2
    tail -30 /tmp/snab-app.log >&2 2>/dev/null || true
    exit 1
}

# Любая необработанная ошибка тоже попадёт в annotations.
trap 'echo "::error::smoke: необработанная ошибка, строка $LINENO"; exit 1' ERR

# ---------------------------------------------------------------- SQL Server
note "SQL Server"
docker rm -f sql1 sqltools >/dev/null 2>&1 || true
docker network create ci-net >/dev/null 2>&1 || true
docker run -d --name sql1 --network ci-net \
    -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$SA_PASSWORD" \
    -p 1433:1433 -v "$WORK:/work:ro" \
    mcr.microsoft.com/mssql/server:2022-latest >/dev/null

# sqlcmd: сначала ищем внутри образа сервера, иначе — отдельный образ mssql-tools.
if docker exec sql1 test -x /opt/mssql-tools18/bin/sqlcmd >/dev/null 2>&1; then
    sqlq() { docker exec sql1 /opt/mssql-tools18/bin/sqlcmd -C -I -S localhost -U sa -P "$SA_PASSWORD" "$@"; }
elif docker exec sql1 test -x /opt/mssql-tools/bin/sqlcmd >/dev/null 2>&1; then
    sqlq() { docker exec sql1 /opt/mssql-tools/bin/sqlcmd -I -S localhost -U sa -P "$SA_PASSWORD" "$@"; }
else
    docker run -d --name sqltools --network ci-net -v "$WORK:/work:ro" \
        --entrypoint sleep mcr.microsoft.com/mssql-tools infinity >/dev/null
    sqlq() { docker exec sqltools /opt/mssql-tools/bin/sqlcmd -I -S sql1 -U sa -P "$SA_PASSWORD" "$@"; }
fi

echo "Ожидание готовности SQL (до 120 с)…"
sql_ok=""
for _ in $(seq 1 60); do
    if sqlq -Q "SELECT 1" >/dev/null 2>&1; then
        sql_ok=1
        break
    fi
    sleep 2
done
[ -n "$sql_ok" ] || { docker logs sql1 2>&1 | tail -20 >&2 || true; fail "SQL Server не поднялся за 120 с"; }

# ---------------------------------------------------------------- Схема
# Полная установка: базовый скрипт + веб-таблицы (права ролей, Контур, воркфлоу)
# + инкременты updates_2026.sql. Каждый скрипт идемпотентен.
note "Схема базы"
: > /tmp/sqlcmd.log
if ! docker exec sql1 test -f /work/web/sql/create_SnabDriveDB.sql >/dev/null 2>&1 && \
   ! docker exec sqltools test -f /work/web/sql/create_SnabDriveDB.sql >/dev/null 2>&1; then
    fail "файл /work/web/sql/create_SnabDriveDB.sql не виден внутри контейнера (монтирование)"
fi
apply_sql() {
    local file=$1
    echo "--- $file ---" | tee -a /tmp/sqlcmd.log
    if ! sqlq -b -i "/work/web/sql/$file" >>/tmp/sqlcmd.log 2>&1; then
        tail -25 /tmp/sqlcmd.log >&2 || true
        # Последние строки ошибки — прямо в аннотации (единственный канал диагностики).
        local brief
        brief=$(tail -8 /tmp/sqlcmd.log | tr '\n' ' ' | tr '\r' ' ' | cut -c1-700)
        echo "::error::sqlcmd/$file: $brief"
        fail "$file не применился"
    fi
}
apply_sql create_SnabDriveDB.sql
apply_sql role_permissions.sql
apply_sql kontur.sql
apply_sql workflow.sql
apply_sql updates_2026.sql

# ---------------------------------------------------------------- Запуск сайта
note "Запуск сайта"
export ConnectionStrings__Registry="Server=localhost,1433;Database=snabdrive_db;User Id=sa;Password=$SA_PASSWORD;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
export Database__AutoMigrateIdentitySchema=false
export Database__SeedRolesAndAdmin=true
export Database__SeedAdminLogin=admin
export Database__SeedAdminPassword="$SEED_PASSWORD"
export Security__DataProtectionKeysPath=/tmp/snab-keys
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS="http://0.0.0.0:$PORT"

dotnet publish/SnabDrive.Web.dll >/tmp/snab-app.log 2>&1 &
APP_PID=$!
trap 'kill "$APP_PID" 2>/dev/null || true' EXIT
trap 'echo "::error::smoke: необработанная ошибка, строка $LINENO"; exit 1' ERR

app_up=""
for _ in $(seq 1 60); do
    if curl -sf -o /dev/null "$APP_URL/login"; then
        app_up=1
        break
    fi
    kill -0 "$APP_PID" 2>/dev/null || fail "приложение упало при старте"
    sleep 2
done
[ -n "$app_up" ] || fail "сайт не ответил на /login за 120 с"

# ---------------------------------------------------------------- Пользователь без прав
# Оператор — роль без административных вкладок: нужен для негативных проверок.
# Пароль хэшируется в формате ASP.NET Identity V3 (PBKDF2-SHA256).
note "Тестовый пользователь без прав (ci_probe / Operator)"
# Хэш пароля — настоящим PasswordHasher'ом Microsoft.Extensions.Identity.Core
# (тот же пакет, что и в приложении): формат Identity V3 слишком хрупок для ручной сборки.
mkdir -p /tmp/hashgen
cat > /tmp/hashgen/hashgen.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Identity.Core" Version="9.0.22" />
  </ItemGroup>
</Project>
EOF
cat > /tmp/hashgen/Program.cs <<'EOF'
using Microsoft.AspNetCore.Identity;
var hasher = new PasswordHasher<object>();
System.Console.WriteLine(hasher.HashPassword(new object(), args.Length > 0 ? args[0] : "x"));
EOF
if ! HASH=$(dotnet run --project /tmp/hashgen -c Release -- "$PROBE_PASSWORD" 2>/tmp/hashgen/err.log | tail -1) || [ -z "$HASH" ]; then
    tail -20 /tmp/hashgen/err.log >&2 || true
    echo "::error::hashgen: $(grep -i 'error\|NU1' /tmp/hashgen/err.log | head -4 | tr '\n' ' ' | tr '\r' ' ' | cut -c1-600)"
    fail "не удалось сгенерировать хэш пароля"
fi
sqlq -b -Q "USE [snabdrive_db];
DECLARE @rid NVARCHAR(450) = (SELECT TOP 1 Id FROM dbo.AspNetRoles WHERE NormalizedName = N'OPERATOR');
IF @rid IS NULL RAISERROR('нет роли OPERATOR (bootstrap не создал роли)', 16, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = N'ci-probe-1')
BEGIN
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail,
        EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
        PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
    VALUES (N'ci-probe-1', N'ci_probe', N'CI_PROBE', N'ci_probe@ci.local', N'CI_PROBE@CI.LOCAL',
        1, N'$HASH', CONVERT(NVARCHAR(36), NEWID()), CONVERT(NVARCHAR(36), NEWID()), 0, 0, 1, 0);
    INSERT INTO dbo.AspNetUserRoles (UserId, RoleId) VALUES (N'ci-probe-1', @rid);
END" >/tmp/sqlcmd-probe.log 2>&1 || { tail -12 /tmp/sqlcmd-probe.log >&2; fail "не удалось создать ci_probe"; }


# ---------------------------------------------------------------- Анонимус
note "Анонимус: страницы закрыты"
for p in /registry /users /admin /workflow; do
    code=$(curl -s -o /dev/null -w "%{http_code}" "$APP_URL$p") || true
    case "$code" in
        302|401) echo "OK   $p → $code" ;;
        *) fail "аноним видит $p (HTTP $code)" ;;
    esac
done

token_from() {
    grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' "$1" | head -1 | sed 's/.*value="//; s/"$//'
}

login_as() {
    local user=$1 pass=$2
    rm -f "$JAR"
    curl -s -c "$JAR" -o "$PAGE" "$APP_URL/login"
    local token
    token=$(token_from "$PAGE")
    [ -n "$token" ] || fail "нет antiforgery-токена на /login"
    curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code} %{redirect_url}" \
        --data-urlencode "login=$user" \
        --data-urlencode "password=$pass" \
        --data-urlencode "returnUrl=/" \
        --data-urlencode "__RequestVerificationToken=$token" \
        "$APP_URL/account/login"
}

# ---------------------------------------------------------------- Вход администратором
note "Вход администратором"
resp=$(login_as admin "$SEED_PASSWORD") || fail "запрос входа админа не прошёл"
code=${resp%% *}
loc=${resp#* }
case "$loc" in
    *change-password*)
        echo "— первый вход: принудительная смена пароля"
        curl -s -b "$JAR" -c "$JAR" -o "$PAGE" "$APP_URL/change-password?first=1"
        token=$(token_from "$PAGE")
        [ -n "$token" ] || fail "нет antiforgery-токена на /change-password"
        NEW_PASSWORD="${SEED_PASSWORD}Aa1!"
        resp2=$(curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code} %{redirect_url}" \
            --data-urlencode "currentPassword=$SEED_PASSWORD" \
            --data-urlencode "newPassword=$NEW_PASSWORD" \
            --data-urlencode "confirmPassword=$NEW_PASSWORD" \
            --data-urlencode "__RequestVerificationToken=$token" \
            "$APP_URL/account/change-password")
        case "${resp2#* }" in
            "$APP_URL/"|"") echo "OK   смена пароля" ;;
            *) fail "смена пароля: $resp2" ;;
        esac
        ;;
    "$APP_URL/"|"") [ "$code" = "302" ] || fail "логин админа: HTTP $code без redirect"; echo "OK   вход без смены пароля" ;;
    *) fail "логин админа: HTTP $code, redirect '$loc'" ;;
esac

check_page() {
    local path=$1
    local code
    code=$(curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code}" "$APP_URL$path") || true
    [ "$code" = "200" ] || fail "$path → HTTP $code"
    if grep -q "Нет доступа к этой вкладке" "$PAGE"; then
        fail "$path → админ заблокирован"
    fi
    echo "OK   $path"
}

note "Страницы (админ)"
for p in / /registry /archive /workflow /kontur /kontur-templates /kontur-scheduler \
         /director /dictionaries /audit /stats /users /roles /import-registry \
         /maintenance /admin; do
    check_page "$p"
done

# ---------------------------------------------------------------- Негативные проверки
note "Пользователь без прав (ci_probe / Operator)"
resp=$(login_as ci_probe "$PROBE_PASSWORD") || fail "запрос входа ci_probe не прошёл"
code=${resp%% *}
loc=${resp#* }
case "$loc" in
    "$APP_URL/"|"") [ "$code" = "302" ] || fail "логин ci_probe: HTTP $code без redirect"; echo "OK   вход ci_probe" ;;
    *) fail "логин ci_probe: HTTP $code, redirect '$loc' (вход не прошёл)" ;;
esac

# Контроль: сессия ci_probe действительно авторизована (иначе дальше тесты бессмысленны).
code=$(curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code}" "$APP_URL/registry") || true
[ "$code" = "200" ] || fail "ci_probe не авторизован: /registry → HTTP $code"
echo "OK   сессия ci_probe авторизована"

# Дыра закрыта: скрытая в меню вкладка не открывается и прямой ссылкой.
code=$(curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code}" "$APP_URL/kontur-scheduler") || true
[ "$code" = "200" ] || fail "ci_probe /kontur-scheduler → HTTP $code"
grep -q "Нет доступа к этой вкладке" "$PAGE" || fail "ДЫРА: ci_probe видит «Планировщик Контура»"
echo "OK   /kontur-scheduler закрыт"

code=$(curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code}" "$APP_URL/users") || true
if [ "$code" = "200" ] && grep -q "Политика паролей" "$PAGE"; then
    fail "ДЫРА: ci_probe видит страницу «Пользователи»"
fi
echo "OK   /users закрыт политикой"

# А вот реестр — единственная вкладка оператора — должен открываться.
code=$(curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code}" "$APP_URL/registry") || true
[ "$code" = "200" ] || fail "ci_probe /registry → HTTP $code"
grep -q "Нет доступа к этой вкладке" "$PAGE" && fail "реестр должен быть доступен оператору"
echo "OK   /registry доступен"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    echo "### ✅ Дымовой прогон: все проверки пройдены" >> "$GITHUB_STEP_SUMMARY"
fi
note "ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ"
