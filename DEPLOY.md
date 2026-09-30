# Deploy AIVES bằng Docker Compose

## Yêu cầu máy chủ

- Ubuntu Server 22.04 hoặc 24.04 x86-64
- Docker Engine và Docker Compose plugin
- RAM tối thiểu 8 GB
- SSD còn trống tối thiểu 30 GB
- Mở TCP port 5201 để kiểm thử ban đầu

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

Đặt mật khẩu SQL mạnh, không dùng dấu chấm phẩy hoặc ký tự `$`. Điền Gemini API key. Google OAuth và Gmail SMTP có thể để trống nếu chưa dùng.

## Cấu trúc solution

WebMVC là Presentation; BLL xử lý nghiệp vụ; DAL chứa EF Core, repository và migrations; DTO chứa dữ liệu trao đổi. Dockerfile restore cả bốn project trước khi publish. Xem [tài liệu kiến trúc](docs/AIVES-3-Layer-Architecture.md) và lệnh EF Core với `--project AIVES.DAL --startup-project AIVES.WebMVC`. Các biến cấu hình triển khai giữ nguyên.

## Build và chạy

```bash
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

Chạy lại `docker compose up -d --build`. Sau khi đăng nhập thành công, đặt `DEMO_ACCOUNT_RESET_PASSWORD=false` (hoặc `DEMO_ACCOUNT_ENABLED=false`) rồi chạy `docker compose up -d` để mật khẩu không bị đặt lại trong các lần khởi động sau.

Mở ứng dụng bằng địa chỉ:

```text
http://IP_CUA_SERVER:5201
```

## Cập nhật phiên bản mới

```bash
cd AIVES_System
git pull origin main
docker compose up -d --build
docker image prune -f
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

## Google OAuth khi có tên miền

Google OAuth trên server công khai cần HTTPS. Sau khi cấu hình domain và reverse proxy, đặt redirect URI trong Google Cloud Console:

```text
https://TEN_MIEN/signin-google
```

Sau đó điền `GOOGLE_CLIENT_ID` và `GOOGLE_CLIENT_SECRET` trong `.env`, rồi chạy:

```bash
docker compose up -d --force-recreate web
```
