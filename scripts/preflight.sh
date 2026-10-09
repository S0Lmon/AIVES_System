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
caddy_site=$(value AIVES_CADDY_SITE)
profiles=,$(value COMPOSE_PROFILES),
if [[ $profiles == *,tunnel,* && $profiles == *,quicktunnel,* ]]; then
    error "Chỉ bật một trong hai profile 'tunnel' hoặc 'quicktunnel'."
fi
if [[ $profiles == *,quicktunnel,* ]]; then
    ok "Quick Tunnel bật: địa chỉ https://*.trycloudflare.com đổi mỗi lần container khởi động lại."
    if [[ $caddy_site != :80 && $caddy_site != http://* ]]; then
        error "Quick Tunnel cần AIVES_CADDY_SITE=:80 (hiện là '${caddy_site:-<theo AIVES_DOMAIN>}')."
    fi
    if [[ $(value AIVES_HTTP_PORT) != 127.0.0.1:* ]]; then
        warn "Nên đặt AIVES_HTTP_PORT=127.0.0.1:8088 để Caddy chỉ mở trên máy này; truy cập ngoài đi qua tunnel."
    fi
elif [[ $profiles == *,tunnel,* ]]; then
    # Cloudflare terminates HTTPS; Caddy must serve plain HTTP or it keeps failing ACME challenges.
    if [[ -z $(value CLOUDFLARE_TUNNEL_TOKEN) ]]; then
        error "COMPOSE_PROFILES có 'tunnel' nhưng CLOUDFLARE_TUNNEL_TOKEN trống."
    else
        ok "Cloudflare Tunnel bật (token đã đặt)."
    fi
    if [[ $caddy_site != :80 && $caddy_site != http://* ]]; then
        error "Chế độ tunnel cần AIVES_CADDY_SITE=:80 (hiện là '${caddy_site:-<theo AIVES_DOMAIN>}')."
    fi
    if [[ -z $domain || $domain == localhost ]]; then
        warn "Đặt AIVES_DOMAIN bằng hostname công khai của tunnel (dùng cho Google OAuth và tài liệu)."
    else
        ok "Hostname công khai: https://$domain (cấu hình Public Hostname trỏ tới http://caddy:80)."
    fi
    if [[ $(value AIVES_HTTP_PORT) != 127.0.0.1:* ]]; then
        warn "Nên đặt AIVES_HTTP_PORT=127.0.0.1:8088 để Caddy chỉ mở trên máy này; truy cập ngoài đi qua tunnel."
    fi
elif [[ -z $domain ]]; then
    error "AIVES_DOMAIN chưa được đặt."
elif [[ $domain == localhost || $domain == 127.0.0.1 ]]; then
    warn "AIVES_DOMAIN=$domain chỉ phù hợp khi thử trên máy; server thật cần tên miền đã trỏ DNS về server."
else
    ok "AIVES_DOMAIN=$domain (Caddy sẽ xin chứng chỉ Let's Encrypt; cần mở port 80/443)."
    https_port=$(value AIVES_HTTPS_PORT)
    if [[ -n $https_port && $https_port != 443 ]]; then
        warn "AIVES_HTTPS_PORT=$https_port: Let's Encrypt và chuyển hướng HTTP→HTTPS đều cần port 443."
    fi
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
speech_dir=$(value SPEECH_MODELS_DIR)
speech_dir=${speech_dir:-./AIVES.WebRazor/App_Data/speech-models}
if [[ -f $speech_dir/whisper/ggml-small.bin && -f $speech_dir/tts/vits-piper-vi_VN-vais1000-medium/tokens.txt ]]; then
    ok "Model giọng nói cho site Razor có trong $speech_dir."
else
    warn "Thiếu model giọng nói trong $speech_dir: phòng thi của site Razor chỉ cho gõ câu trả lời. Tải bằng scripts/download-speech-models.ps1 -WithPreviewModel."
fi

echo
if (( errors > 0 )); then
    echo "Có $errors lỗi, $warnings cảnh báo. Sửa .env rồi chạy lại."
    exit 1
fi
echo "Sẵn sàng deploy ($warnings cảnh báo): docker compose up -d --build"
