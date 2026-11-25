#!/usr/bin/env bash
# Дымовой прогон SnabDrive.Web в CI:
#   1) SQL Server в docker + схема из web/sql (create_SnabDriveDB.sql, updates_2026.sql);
#   2) запуск опубликованного сайта (publish/);
#   3) вход администратором и проверка ВСЕХ страниц;
#   4) негативные проверки: анонимус без входа и пользователь без прав.
# Запускается из .github/workflows/ci.yml (job «smoke»).
set -euo pipefail

SA_PASSWORD="${SA_PASSWORD:?SA_PASSWORD is required}"
SEED_PASSWORD="${SEED_PASSWORD:?SEED_PASSWORD is required}"
PROBE_PASSWORD="${PROBE_PASSWORD:-Ci#Probe-2026-Pw}"
PORT="${PORT:-5088}"
APP_URL="http://localhost:$PORT"
WORK="${GITHUB_WORKSPACE:-$(cd "$(dirname "$0")/../.." && pwd)}"
JAR=/tmp/snab-cookies.txt
PAGE=/tmp/snab-page.html

fail() {
    echo "FAIL: $1" >&2
    echo "--- последние строки лога приложения ---" >&2
    tail -30 /tmp/snab-app.log >&2 2>/dev/null || true
    exit 1
}

# ---------------------------------------------------------------- SQL Server
echo "=== SQL Server ==="
docker rm -f sql1 >/dev/null 2>&1 || true
docker run -d --name sql1 \
    -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$SA_PASSWORD" \
    -p 1433:1433 -v "$WORK:/work:ro" \
    mcr.microsoft.com/mssql/server:2022-latest >/dev/null

SQLCMD_BIN=""
for bin in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do
    if docker exec sql1 test -x "$bin" >/dev/null 2>&1; then
        SQLCMD_BIN=$bin
        break
    fi
done
[ -n "$SQLCMD_BIN" ] || fail "sqlcmd не найден в образе mssql"
TRUST=""
case "$SQLCMD_BIN" in
    *tools18*) TRUST="-C" ;;
esac

sqlq() {
    # shellcheck disable=SC2086
    docker exec sql1 "$SQLCMD_BIN" $TRUST -S localhost -U sa -P "$SA_PASSWORD" "$@"
}

echo "Ожидание готовности SQL (до 120 с)…"
sql_ok=""
for _ in $(seq 1 60); do
    if sqlq -Q "SELECT 1" >/dev/null 2>&1; then
        sql_ok=1
        break
    fi
    sleep 2
done
if [ -z "$sql_ok" ]; then
    docker logs sql1 2>&1 | tail -20 >&2 || true
    fail "SQL Server не поднялся за 120 с"
fi

# ---------------------------------------------------------------- Схема
echo "=== Схема базы ==="
sqlq -b -i /work/web/sql/create_SnabDriveDB.sql || fail "create_SnabDriveDB.sql"
sqlq -b -i /work/web/sql/updates_2026.sql || fail "updates_2026.sql"

# ---------------------------------------------------------------- Пользователь без прав
# Оператор — роль без административных вкладок: нужен для негативных проверок.
# Пароль хэшируется в формате ASP.NET Identity V3 (PBKDF2-SHA256).
echo "=== Тестовый пользователь без прав (ci_probe / Operator) ==="
HASH=$(python3 - "$PROBE_PASSWORD" <<'PY'
import base64, hashlib, os, struct, sys
pwd = sys.argv[1].encode()
salt = os.urandom(16)
iters = 100_000
dk = hashlib.pbkdf2_hmac('sha256', pwd, salt, iters, dklen=32)
blob = (b'\x01' + struct.pack('>I', iters) + struct.pack('>I', len(salt)) + salt
        + struct.pack('>I', len(dk)) + dk)
print(base64.b64encode(blob).decode())
PY
)
sqlq -b -Q "USE [snabdrive_db];
DECLARE @rid NVARCHAR(450) = (SELECT TOP 1 Id FROM dbo.AspNetRoles WHERE NormalizedName = N'OPERATOR');
IF @rid IS NULL RAISERROR('нет роли OPERATOR', 16, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = N'ci-probe-1')
BEGIN
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail,
        EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
        PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
    VALUES (N'ci-probe-1', N'ci_probe', N'CI_PROBE', N'ci_probe@ci.local', N'CI_PROBE@CI.LOCAL',
        1, N'$HASH', CONVERT(NVARCHAR(36), NEWID()), CONVERT(NVARCHAR(36), NEWID()), 0, 0, 1, 0);
    INSERT INTO dbo.AspNetUserRoles (UserId, RoleId) VALUES (N'ci-probe-1', @rid);
END" || fail "создание ci_probe"

# ---------------------------------------------------------------- Запуск сайта
echo "=== Запуск сайта ==="
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

# ---------------------------------------------------------------- Анонимус
echo "=== Анонимус: страницы закрыты ==="
for p in /registry /users /admin /workflow; do
    code=$(curl -s -o /dev/null -w "%{http_code}" "$APP_URL$p")
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
echo "=== Вход администратором ==="
resp=$(login_as admin "$SEED_PASSWORD")
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
            "$APP_URL"*) echo "OK   смена пароля" ;;
            *) fail "смена пароля: $resp2" ;;
        esac
        ;;
    "$APP_URL"*) echo "OK   вход без смены пароля" ;;
    *) fail "логин админа: HTTP $code, redirect '$loc'" ;;
esac

check_page() {
    local path=$1
    local code
    code=$(curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code}" "$APP_URL$path")
    [ "$code" = "200" ] || fail "$path → HTTP $code"
    if grep -q "Нет доступа к этой вкладке" "$PAGE"; then
        fail "$path → админ заблокирован"
    fi
    echo "OK   $path"
}

echo "=== Страницы (админ) ==="
for p in / /registry /archive /workflow /kontur /kontur-templates /kontur-scheduler \
         /director /dictionaries /audit /stats /users /roles /import-registry \
         /maintenance /admin; do
    check_page "$p"
done

# ---------------------------------------------------------------- Негативные проверки
echo "=== Пользователь без прав (ci_probe / Operator) ==="
resp=$(login_as ci_probe "$PROBE_PASSWORD")
code=${resp%% *}
loc=${resp#* }
case "$loc" in
    "$APP_URL"*) echo "OK   вход ci_probe" ;;
    *) fail "логин ci_probe: HTTP $code, redirect '$loc'" ;;
esac

# Дыра закрыта: скрытая в меню вкладка не открывается и прямой ссылкой.
code=$(curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code}" "$APP_URL/kontur-scheduler")
[ "$code" = "200" ] || fail "ci_probe /kontur-scheduler → HTTP $code"
grep -q "Нет доступа к этой вкладке" "$PAGE" || fail "ДЫРА: ci_probe видит «Планировщик Контура»"

code=$(curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code}" "$APP_URL/users")
if [ "$code" = "200" ] && grep -q "Политика паролей" "$PAGE"; then
    fail "ДЫРА: ci_probe видит страницу «Пользователи»"
fi
echo "OK   /users закрыт политикой"

# А вот реестр — единственная вкладка оператора — должен открываться.
code=$(curl -s -b "$JAR" -c "$JAR" -o "$PAGE" -w "%{http_code}" "$APP_URL/registry")
[ "$code" = "200" ] || fail "ci_probe /registry → HTTP $code"
grep -q "Нет доступа к этой вкладке" "$PAGE" && fail "реестр должен быть доступен оператору"
echo "OK   /registry доступен"

echo "=== ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ ==="
