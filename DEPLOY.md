# Deploy AIVES bằng Docker Compose

## Yêu cầu máy chủ

- Ubuntu Server 22.04 hoặc 24.04 x86-64
- Docker Engine và Docker Compose plugin
- RAM tối thiểu 8 GB
- SSD còn trống tối thiểu 30 GB
- Mở TCP port 80 và 443 (UDP 443 nếu muốn HTTP/3); trỏ bản ghi DNS `A` của tên miền về IP server

## Kiến trúc triển khai

```text
Internet ──HTTPS──► caddy (TLS tự động, HTTP→HTTPS, cân bằng tải round-robin)
                       │
             web × AIVES_WEB_REPLICAS   (stateless, /health, user SQL aives_app)
                       │
                   sqlserver  ◄── migrate (sa, chạy một lần) ◄── db-init (tạo aives_app)
```

Thứ tự khởi động: `sqlserver` healthy → `migrate` áp dụng migrations và seed rồi thoát → `db-init` tạo/cập nhật login `aives_app` rồi thoát → các instance `web` → `caddy`.

- **Chỉ `migrate` đổi schema** và là container duy nhất (cùng `db-init`) dùng `sa`. Các instance web chạy với `Database:MigrateOnStartup=false` và login `aives_app` chỉ có quyền `db_datareader`/`db_datawriter`, không tạo/sửa bảng được.
- **Khóa Data Protection** (mã hóa cookie đăng nhập và antiforgery token) lưu trong bảng `DataProtectionKeys`, dùng chung giữa các instance. Người dùng không bị đăng xuất khi deploy lại hoặc khi request chuyển sang instance khác. Khóa được **mã hóa bằng chứng chỉ** `secrets/dataprotection.pfx` trước khi ghi vào database, nên lộ file backup database thôi thì không giả mạo được cookie. Chứng chỉ được nạp vào container dưới dạng Docker secret, không nằm trong image hay Git.
- App không tự chuyển hướng HTTPS khi `ReverseProxy:TerminatesHttps=true` (compose đã đặt sẵn) vì Caddy đã làm việc này.
- **Web không mở port ra ngoài**; chỉ `caddy` nhận traffic. Caddy tự lấy chứng chỉ Let's Encrypt cho `AIVES_DOMAIN` và tự phát hiện instance web mới sau mỗi 10 giây.
- `GET /health` trả `Healthy` khi instance kết nối được database; Docker dùng nó làm healthcheck của `web`.

## Cài Docker trên Ubuntu

```bash
sudo apt update
sudo apt install -y ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker "$USER"
newgrp docker
docker --version
docker compose version
```

## Cấu hình lần đầu

```bash
cd AIVES_System
cp .env.example .env
nano .env
```

Các biến bắt buộc:

| Biến | Ý nghĩa |
|---|---|
| `MSSQL_SA_PASSWORD` | Mật khẩu `sa`, chỉ dùng cho `migrate` và `db-init` |
| `MSSQL_APP_PASSWORD` | Mật khẩu login `aives_app` mà các instance web dùng; phải khác mật khẩu `sa` |
| `AIVES_DOMAIN` | Tên miền công khai, ví dụ `aives.example.edu.vn`; để `localhost` khi thử trên máy |
| `DATAPROTECTION_CERT_PASSWORD` | Mật khẩu của chứng chỉ mã hóa khóa cookie `secrets/dataprotection.pfx` |

Hai mật khẩu SQL phải đủ mạnh (tối thiểu 8 ký tự, gồm chữ hoa, chữ thường, số và ký hiệu) và không chứa dấu chấm phẩy, dấu nháy đơn hoặc ký tự `$`. Điền Gemini API key. Google OAuth và Gmail SMTP có thể để trống nếu chưa dùng.

Biến tùy chọn: `AIVES_WEB_REPLICAS` (số instance web, mặc định 2), `AIVES_HTTP_PORT`/`AIVES_HTTPS_PORT` (mặc định 80/443), `ADMIN_EMAIL` (email được cấp quyền Admin), `REGISTRATION_EMAIL_DOMAINS` (tên miền email được phép đăng ký, phân tách bằng dấu phẩy, mặc định `gmail.com,fpt.edu.vn`).

`GMAIL_USERNAME` có thể là tài khoản Gmail hoặc Google Workspace (ví dụ email `@fpt.edu.vn`). Google chỉ cho đăng nhập SMTP bằng **App Password** (https://myaccount.google.com/apppasswords, cần bật xác minh 2 bước); mật khẩu đăng nhập thường bị từ chối với lỗi `534 5.7.9`. Một số trường tắt App Password cho tài khoản Workspace — khi đó hãy dùng một tài khoản Gmail riêng để gửi.

## Cấu trúc solution

WebMVC là Presentation; BLL xử lý nghiệp vụ; DAL chứa EF Core, repository và migrations; DTO chứa dữ liệu trao đổi. Dockerfile restore cả bốn project trước khi publish. Xem [tài liệu kiến trúc](docs/AIVES-3-Layer-Architecture.md) và lệnh EF Core với `--project AIVES.DAL --startup-project AIVES.WebMVC`. Các biến cấu hình triển khai giữ nguyên.

## Kiểm tra cấu hình (preflight)

```bash
chmod +x scripts/preflight.sh
./scripts/preflight.sh
```

Script đọc `.env` và dừng với mã lỗi nếu: thiếu biến bắt buộc, còn giá trị mẫu, mật khẩu SQL yếu hoặc chứa ký tự cấm, hai mật khẩu SQL trùng nhau, hoặc chứng chỉ không mở được bằng mật khẩu. Lần đầu chạy, script **tự tạo** `secrets/dataprotection.pfx` (RSA 3072, hiệu lực 10 năm; cần `openssl`). Các mục chưa cấu hình như Gemini, SMTP, `ADMIN_EMAIL` hay tên miền `localhost` chỉ hiện cảnh báo.

**Sao lưu `secrets/dataprotection.pfx` cùng mật khẩu** ra nơi an toàn ngoài server. Nếu mất file này, các khóa trong database không giải mã được nữa: mọi người dùng bị đăng xuất, và phải xóa bảng `DataProtectionKeys` để app tạo khóa mới.

## Build và chạy

```bash
./scripts/preflight.sh
docker compose config
docker compose up -d --build
docker compose ps
docker compose logs -f web
```

### Bật tài khoản demo (tùy chọn)

Tài khoản demo Development không được tạo tự động trong môi trường Production. Nếu cần tài khoản kiểm thử, đặt các biến sau trong `.env`:

```dotenv
DEMO_ACCOUNT_ENABLED=true
DEMO_ACCOUNT_EMAIL=demo.aives@gmail.com
DEMO_ACCOUNT_PASSWORD=MatKhauManhCuaBan
DEMO_ACCOUNT_DISPLAY_NAME=AIVES Demo
DEMO_ACCOUNT_RESET_PASSWORD=true
```

Chạy lại `docker compose up -d --build`; tài khoản được tạo bởi container `migrate`. Sau khi đăng nhập thành công, đặt `DEMO_ACCOUNT_RESET_PASSWORD=false` (hoặc `DEMO_ACCOUNT_ENABLED=false`) rồi chạy `docker compose up -d` để mật khẩu không bị đặt lại trong các lần khởi động sau.

Mở ứng dụng bằng địa chỉ:

```text
https://TEN_MIEN
```

Khi thử trên máy với `AIVES_DOMAIN=localhost`, Caddy dùng chứng chỉ nội bộ nên trình duyệt sẽ cảnh báo chứng chỉ. Nếu đổi `AIVES_HTTPS_PORT` khác 443, hãy mở thẳng `https://localhost:<port>` vì chuyển hướng HTTP→HTTPS luôn trỏ về port 443.

Kiểm tra nhanh:

```bash
docker compose ps -a          # migrate, db-init: Exited (0); web: healthy
curl -fsS https://TEN_MIEN/health
```

## Chạy trên máy Windows qua Cloudflare Tunnel

Dùng khi server là một máy Windows ở nhà/văn phòng (Docker Desktop), không mở được port router hoặc không muốn lộ IP. Cloudflare nhận HTTPS ở tên miền của bạn rồi chuyển vào máy qua kết nối đi ra (outbound) của container `cloudflared`; Caddy chỉ phục vụ HTTP nội bộ.

```text
Người dùng ──HTTPS──► Cloudflare ──tunnel──► cloudflared ──HTTP──► caddy:80 ──► web × N
```

**1. Tạo tunnel (một lần).** Trong Cloudflare dashboard: *Zero Trust → Networks → Tunnels → Create a tunnel → Cloudflared*, đặt tên (ví dụ `aives`). Ở bước cài đặt, chọn Docker và copy phần token sau `--token` (chuỗi dài bắt đầu bằng `eyJ`). Ở bước *Public Hostname*: chọn subdomain + tên miền (ví dụ `aives.example.com`), *Service* = `HTTP`, *URL* = `caddy:80`.

**2. Cấu hình `.env`:**

```dotenv
COMPOSE_PROFILES=tunnel
CLOUDFLARE_TUNNEL_TOKEN=<token vừa copy>
AIVES_DOMAIN=aives.example.com
AIVES_CADDY_SITE=:80
AIVES_HTTP_PORT=127.0.0.1:8088
AIVES_HTTPS_PORT=127.0.0.1:8443
```

`AIVES_CADDY_SITE=:80` tắt việc Caddy tự xin chứng chỉ (Cloudflare đã lo HTTPS). Hai dòng port giữ Caddy chỉ mở trên `127.0.0.1`, nên máy khác trong mạng LAN không vào thẳng được; vẫn thử nhanh trên chính máy bằng `http://127.0.0.1:8088`. Caddy tin header `X-Forwarded-Proto` từ dải IP nội bộ (mạng Docker), nên app biết người dùng đang dùng HTTPS: cookie có cờ `Secure` và trả về HSTS.

**3. Chạy** (Git Bash hoặc WSL; trong PowerShell dùng `bash scripts/preflight.sh`):

```bash
./scripts/preflight.sh
docker compose up -d --build
docker compose logs cloudflared | grep -i "registered tunnel connection"
```

Trong dashboard, tunnel chuyển sang *Healthy*; mở `https://aives.example.com/health` phải thấy `Healthy`.

**4. Để máy chạy như server:**

- Đặt mã nguồn trên ổ ổn định (SSD hệ thống), không đặt trên ổ có lỗi đọc/ghi.
- Docker Desktop → *Settings → General* → bật **Start Docker Desktop when you sign in**. Docker Desktop chỉ chạy sau khi đăng nhập Windows, nên cần bật đăng nhập tự động (`netplwiz`) hoặc đăng nhập lại sau mỗi lần khởi động. Các container có `restart: unless-stopped` nên tự chạy lại khi Docker lên.
- *Settings → System → Power*: không cho máy ngủ khi cắm điện.
- *Windows Update → Advanced options → Active hours*: tránh tự khởi động lại trong giờ sử dụng.
- Không chạy `docker compose down -v` (xóa database).

## Scale số instance web

```bash
docker compose up -d --scale web=4 --no-recreate
```

Hoặc đặt `AIVES_WEB_REPLICAS=4` trong `.env` rồi `docker compose up -d`. Caddy tự đưa instance mới vào vòng cân bằng tải trong khoảng 10 giây. Mỗi instance web dùng khoảng 150–300 MB RAM; SQL Server bản Express giới hạn khoảng 1,4 GB bộ nhớ đệm và database 10 GB, nên khi dữ liệu hoặc tải tăng cần chuyển `MSSQL_PID` sang bản phù hợp (có bản quyền) hoặc tách database sang máy riêng.

## Ollama (tùy chọn)

Mặc định Ollama tắt trong Docker. Để chạy model cục bộ cạnh ứng dụng:

```bash
docker compose --profile ollama up -d
docker compose exec ollama ollama pull phi3:mini
```

Đặt `OLLAMA_ENABLED=true` (và `OLLAMA_MODEL` nếu dùng model khác) trong `.env`, rồi `docker compose up -d`. Nếu Ollama chạy ở máy khác (khuyến nghị máy có GPU), chỉ cần đặt `OLLAMA_BASE_URL` trỏ tới máy đó, không cần profile `ollama`.

## Cập nhật phiên bản mới

```bash
cd AIVES_System
git pull origin main
./scripts/preflight.sh
docker compose up -d --build
docker image prune -f
```

### Nâng cấp server đã chạy bản trước khi có chứng chỉ

Các khóa tạo trước khi bật chứng chỉ vẫn nằm trong database ở dạng chưa mã hóa cho tới khi hết hạn (90 ngày). Muốn loại bỏ ngay thì xóa chúng một lần sau khi deploy bản mới. Mọi người dùng sẽ phải đăng nhập lại một lần:

```bash
docker exec -e SQLCMDPASSWORD="$(grep '^MSSQL_SA_PASSWORD=' .env | cut -d= -f2-)" aives-sqlserver   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -d AIVES   -Q "DELETE FROM DataProtectionKeys WHERE Xml LIKE '%<masterKey%'"
docker compose restart web
```

## Dừng và khởi động lại

```bash
docker compose stop
docker compose start
```

## Xem log

```bash
docker compose logs -f --tail=200 web
docker compose logs -f --tail=200 sqlserver
```

## Sao lưu database

```bash
mkdir -p backups
docker exec aives-sqlserver /bin/bash -c \
  '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "BACKUP DATABASE [AIVES] TO DISK = N'"'"'/var/opt/mssql/data/AIVES.bak'"'"' WITH INIT, COMPRESSION"'
docker cp aives-sqlserver:/var/opt/mssql/data/AIVES.bak ./backups/AIVES.bak
```

Không chạy `docker compose down -v` trên server đang có dữ liệu vì tùy chọn `-v` xóa volume SQL Server.

Bản backup chứa khóa cookie đã mã hóa; để khôi phục sang server khác, mang theo cả `secrets/dataprotection.pfx` và `DATAPROTECTION_CERT_PASSWORD`, nếu không người dùng sẽ phải đăng nhập lại.

## Google OAuth khi có tên miền

Google OAuth trên server công khai cần HTTPS; Caddy đã cung cấp HTTPS khi `AIVES_DOMAIN` là tên miền thật. Đặt redirect URI trong Google Cloud Console:

```text
https://TEN_MIEN/signin-google
```

Sau đó điền `GOOGLE_CLIENT_ID` và `GOOGLE_CLIENT_SECRET` trong `.env`, rồi chạy:

```bash
docker compose up -d --force-recreate web
```
