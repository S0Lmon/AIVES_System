#!/usr/bin/env bash
# Checks .env before "docker compose up" and creates the Data Protection certificate
# when it is missing. Exits non-zero if anything would break the deployment.
#
#   ./scripts/preflight.sh && docker compose up -d --build
set -euo pipefail

cd "$(dirname "$0")/.."
ENV_FILE=.env
CERT_FILE=secrets/dataprotection.pfx
errors=0
warnings=0

error() { echo "  [LỖI]     $*"; errors=$((errors + 1)); }
warn()  { echo "  [CẢNH BÁO] $*"; warnings=$((warnings + 1)); }
ok()    { echo "  [OK]      $*"; }

if [[ ! -f $ENV_FILE ]]; then
    echo "Không tìm thấy $ENV_FILE. Tạo từ mẫu: cp .env.example .env"
    exit 1
fi

# Reads KEY from .env the way Docker Compose does: last assignment wins, surrounding quotes dropped.
value() {
    local line
    line=$(grep -E "^[[:space:]]*$1=" "$ENV_FILE" | tail -n 1 || true)
    line=${line#*=}
    line=${line%$'\r'}
    if [[ $line =~ ^\"(.*)\"$ || $line =~ ^\'(.*)\'$ ]]; then line=${BASH_REMATCH[1]}; fi
    printf '%s' "$line"
}

# SQL Server's policy: at least 8 characters from three of upper, lower, digit, symbol.
check_sql_password() {
    local name=$1 password
    password=$(value "$name")
    if [[ -z $password ]]; then error "$name chưa được đặt."; return; fi
    if [[ $password == ReplaceWith* ]]; then error "$name vẫn là giá trị mẫu trong .env.example."; return; fi
    if [[ $password == *[\;\'\"\$]* ]]; then
        error "$name không được chứa ; ' \" hoặc \$ (làm hỏng connection string/sqlcmd)."; return
    fi
    local classes=0
    [[ $password =~ [A-Z] ]] && classes=$((classes + 1))
    [[ $password =~ [a-z] ]] && classes=$((classes + 1))
    [[ $password =~ [0-9] ]] && classes=$((classes + 1))
    [[ $password =~ [^A-Za-z0-9] ]] && classes=$((classes + 1))
    if (( ${#password} < 8 || classes < 3 )); then
        error "$name cần tối thiểu 8 ký tự và ít nhất 3 trong 4 loại: chữ hoa, chữ thường, số, ký hiệu."
        return
    fi
    ok "$name hợp lệ."
}

echo "SQL Server"
check_sql_password MSSQL_SA_PASSWORD
check_sql_password MSSQL_APP_PASSWORD
if [[ -n $(value MSSQL_SA_PASSWORD) && $(value MSSQL_SA_PASSWORD) == "$(value MSSQL_APP_PASSWORD)" ]]; then
    error "MSSQL_APP_PASSWORD phải khác MSSQL_SA_PASSWORD."
fi

echo "Tên miền và HTTPS"
domain=$(value AIVES_DOMAIN)
if [[ -z $domain ]]; then
    error "AIVES_DOMAIN chưa được đặt."
elif [[ $domain == localhost || $domain == 127.0.0.1 ]]; then
    warn "AIVES_DOMAIN=$domain chỉ phù hợp khi thử trên máy; server thật cần tên miền đã trỏ DNS về server."
else
    ok "AIVES_DOMAIN=$domain (Caddy sẽ xin chứng chỉ Let's Encrypt; cần mở port 80/443)."
fi
https_port=$(value AIVES_HTTPS_PORT)
if [[ -n $https_port && $https_port != 443 && $domain != localhost ]]; then
    warn "AIVES_HTTPS_PORT=$https_port: Let's Encrypt và chuyển hướng HTTP→HTTPS đều cần port 443."
fi

echo "Khóa Data Protection"
cert_password=$(value DATAPROTECTION_CERT_PASSWORD)
if [[ -z $cert_password ]]; then
    error "DATAPROTECTION_CERT_PASSWORD chưa được đặt."
elif [[ $cert_password == ReplaceWith* ]]; then
    error "DATAPROTECTION_CERT_PASSWORD vẫn là giá trị mẫu trong .env.example."
elif ! command -v openssl >/dev/null 2>&1; then
    error "Cần openssl để tạo/kiểm tra $CERT_FILE (Ubuntu: sudo apt install -y openssl)."
elif [[ -f $CERT_FILE ]]; then
    if CERT_PASSWORD=$cert_password openssl pkcs12 -in "$CERT_FILE" -noout -passin env:CERT_PASSWORD 2>/dev/null; then
        ok "$CERT_FILE mở được bằng DATAPROTECTION_CERT_PASSWORD."
    else
        error "$CERT_FILE không mở được bằng DATAPROTECTION_CERT_PASSWORD (sai mật khẩu hoặc file hỏng)."
    fi
else
    # A relative work dir: Git Bash on Windows needs MSYS_NO_PATHCONV for "/CN=...", which
    # would also stop it translating absolute paths such as /tmp for the native openssl.
    workdir=secrets/.tmp-$$
    mkdir -p "$workdir"
    trap 'rm -rf "$workdir"' EXIT
    MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
        -subj "/CN=AIVES Data Protection" \
        -keyout "$workdir/key.pem" -out "$workdir/cert.pem" 2>/dev/null
    CERT_PASSWORD=$cert_password openssl pkcs12 -export -inkey "$workdir/key.pem" -in "$workdir/cert.pem" \
        -out "$CERT_FILE" -passout env:CERT_PASSWORD
    # The web container runs as a non-root user and must read the file; the PFX itself is password-protected.
    chmod 644 "$CERT_FILE"
    ok "Đã tạo $CERT_FILE (hiệu lực 10 năm). Sao lưu file này cùng mật khẩu; mất nó thì mọi người dùng phải đăng nhập lại."
fi

echo "Dịch vụ tùy chọn"
[[ -z $(value GEMINI_API_KEY) && $(value OLLAMA_ENABLED) != true ]] && warn "Chưa có GEMINI_API_KEY và Ollama đang tắt: chức năng sinh câu hỏi AI sẽ không hoạt động."
[[ -z $(value GMAIL_APP_PASSWORD) ]] && warn "Chưa cấu hình Gmail SMTP: người dùng mới không nhận được mã xác minh email."
[[ -z $(value ADMIN_EMAIL) ]] && warn "ADMIN_EMAIL trống: sẽ không có tài khoản nào được cấp quyền Admin."
if [[ $(value DEMO_ACCOUNT_ENABLED) == true && $(value DEMO_ACCOUNT_RESET_PASSWORD) == true ]]; then
    warn "DEMO_ACCOUNT_RESET_PASSWORD=true: mật khẩu tài khoản demo bị đặt lại mỗi lần deploy."
fi

echo
if (( errors > 0 )); then
    echo "Có $errors lỗi, $warnings cảnh báo. Sửa .env rồi chạy lại."
    exit 1
fi
echo "Sẵn sàng deploy ($warnings cảnh báo): docker compose up -d --build"
