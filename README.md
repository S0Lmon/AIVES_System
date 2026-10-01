# AIVES System

AIVES là ứng dụng web hỗ trợ chuẩn bị và quản lý câu hỏi cho thi vấn đáp. Project sử dụng ASP.NET Core MVC, SQL Server, Gemini hoặc Ollama để tạo bản nháp câu hỏi bằng tiếng Việt. Định hướng phát triển là hỗ trợ quy trình thi vấn đáp có AI, trong đó giảng viên duyệt câu hỏi và quyết định điểm.

Phiên bản hiện tại tập trung vào **ngân hàng câu hỏi, danh mục môn học/chủ đề/tài liệu, tài khoản và tạo câu hỏi bằng AI**. Phỏng vấn bằng giọng nói, tổ chức kỳ thi và chấm điểm chưa được triển khai đầy đủ.

## Chức năng hiện có

- **Tài khoản:** đăng ký bằng Gmail, gửi mã xác minh qua SMTP, xác minh email, gửi lại mã, đăng nhập bằng mật khẩu, đăng xuất và đăng nhập Google khi được cấu hình.
- **Ngân hàng câu hỏi:** thêm, xem danh sách/chi tiết, sửa, xóa và lọc theo mức Bloom; lưu ngữ cảnh, đáp án mong đợi, rubric, thứ tự hiển thị và trạng thái hoạt động.
- **Danh mục:** mức Bloom và rubric được truy xuất qua service/repository để chọn khi tạo câu hỏi. Rubric có nghiệp vụ CRUD ở BLL/DAL; chưa có màn hình quản lý rubric riêng.
- **Môn học / chủ đề / tài liệu:** CRUD đầy đủ cho ba cấp danh mục, nhập tài liệu từ tệp `.txt`, `.md`, `.csv`, `.json`. Tài liệu làm ngữ cảnh nền cho câu hỏi AI.
- **Gemini / Ollama:** tạo từ 1 đến 10 câu hỏi theo môn học, chủ đề, chuẩn đầu ra và độ khó, có thể lấy thêm tài liệu của chủ đề làm ngữ cảnh. Router ưu tiên Gemini và tự chuyển sang mô hình Ollama cục bộ khi Gemini chưa cấu hình. Mỗi câu có đáp án mong đợi, mức Bloom và 2 câu hỏi đào sâu. Kết quả là bản nháp để giảng viên duyệt, chưa tự động lưu vào ngân hàng.
- **Bảng trượt AI:** nút **Generate with AI** trên `Question/Create` (và **AI generation** trên trang ngân hàng) mở một bảng trượt nhỏ gọn. Chọn bộ sinh, bật/tắt việc dùng tài liệu làm ngữ cảnh, bấm **Use this question** để đổ kết quả vào `Content`, `ExpectedAnswer` và `Bloom level` của biểu mẫu đang mở.
- **AI exam room:** đang tạm tắt vì luồng phòng thi chưa đúng; mục menu được hiển thị dạng mờ và mọi đường dẫn cũ chuyển hướng về `Question/Create?slide=aiPanel`.
- **Đa ngôn ngữ:** toàn bộ giao diện và thông báo hỗ trợ English và Tiếng Việt.
- **Giao diện:** trang tổng quan và menu responsive cho desktop/mobile.

Các module kỳ thi/lịch thi, STT/TTS, phỏng vấn thích ứng, hỗ trợ chấm điểm, giám sát, báo cáo và quản trị nâng cao hiện là **roadmap**. Các phần giới thiệu trên dashboard không đồng nghĩa với chức năng đã hoàn thành.

## Công nghệ

| Thành phần | Công nghệ |
|---|---|
| Runtime | .NET 10, C# |
| Presentation | ASP.NET Core MVC, Razor, Bootstrap, JavaScript |
| Dữ liệu | EF Core 10, SQL Server, EF migrations |
| Xác thực | ASP.NET Core Identity, cookie, Google OAuth |
| Tích hợp | Gemini API, Ollama (`phi3:mini`), Gmail SMTP |
| Kiểm thử | xUnit, ASP.NET Core TestServer, EF InMemory và SQL Server thật |
| Đóng gói | Dockerfile nhiều giai đoạn, Docker Compose |

## Kiến trúc 3 Layer + DTO

![Sơ đồ kiến trúc hệ thống AIVES](docs/AIVES-3-Layer-Architecture.png)

```text
AIVES_System/
├── AIVES.WebMVC/    # Presentation: Controllers, Views, ViewModels, wwwroot, HTTP
├── AIVES.BLL/       # Business: service, validation, quy tắc và điều phối nghiệp vụ
├── AIVES.DAL/       # Data Access: EF Core, repository, Identity store, migrations
├── AIVES.DTO/       # Đối tượng truyền dữ liệu dùng chung
├── AIVES.Tests/     # Kiểm thử unit, HTTP, Identity và SQL integration
├── docs/            # Tài liệu kiến trúc, báo cáo và bằng chứng kiểm thử
├── scripts/         # Công cụ kiểm tra SMTP
├── AIVES_System.slnx
├── compose.yaml
└── DEPLOY.md
```

Luồng xử lý: **Người dùng → WebMVC → BLL → DAL → SQL Server**, kết quả đi ngược lại qua DTO. WebMVC tham chiếu BLL và DTO; BLL tham chiếu DAL và DTO; DAL là nơi duy nhất truy cập database. DTO không chứa truy vấn hay logic xử lý nghiệp vụ.

WebMVC xử lý form/HTTP và cấu hình MVC, cookie, Google OAuth. BLL kiểm tra dữ liệu, chính sách tài khoản, OTP và điều phối AI/email. DAL ánh xạ DTO với entity, thực hiện truy vấn/lưu trữ và chứa toàn bộ migrations. Ba layer là phân chia trách nhiệm trong mã nguồn, không yêu cầu ba máy chủ triển khai.

Xem [tài liệu kiến trúc](docs/AIVES-3-Layer-Architecture.md).

## Chạy bằng .NET

### Điều kiện

- .NET SDK 10.
- SQL Server có thể kết nối; cấu hình mặc định dùng SQL Server LocalDB trên Windows.
- Visual Studio hỗ trợ .NET 10 và solution `.slnx`, hoặc dùng CLI.

Mở `AIVES_System.slnx` tại thư mục gốc; chọn `AIVES.WebMVC` làm startup project nếu dùng Visual Studio.

```powershell
dotnet restore AIVES_System.slnx
dotnet build AIVES_System.slnx -c Release

# Chỉ cần thay connection nếu không dùng LocalDB mặc định.
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<SQL_SERVER_CONNECTION_STRING>" --project AIVES.WebMVC

dotnet run --project AIVES.WebMVC --launch-profile https
```

Profile HTTPS mở ứng dụng tại `https://localhost:7195` (HTTP: `http://localhost:5201`). Có thể chạy `dotnet dev-certs https --trust` trên máy phát triển nếu cần tin cậy chứng chỉ HTTPS local.

Ứng dụng áp dụng migrations khi khởi động; tài khoản database cần quyền phù hợp. Môi trường Development có seed dữ liệu mẫu. Không đặt connection string chứa mật khẩu vào Git.

### Cấu hình dịch vụ ngoài

Lưu các giá trị thật bằng User Secrets khi phát triển; dùng biến môi trường khi triển khai. Các giá trị `<...>` dưới đây là placeholder, phải thay trước khi chạy.

```powershell
# Tạo câu hỏi AI
dotnet user-secrets set "Gemini:ApiKey" "<GEMINI_API_KEY>" --project AIVES.WebMVC
dotnet user-secrets set "Gemini:Model" "<MODEL_AVAILABLE_TO_YOUR_ACCOUNT>" --project AIVES.WebMVC

# Fallback cục bộ bằng Ollama (tùy chọn)
dotnet user-secrets set "Ollama:Enabled" "true" --project AIVES.WebMVC
dotnet user-secrets set "Ollama:Model" "phi3:mini" --project AIVES.WebMVC
dotnet user-secrets set "Ollama:BaseUrl" "http://localhost:11434" --project AIVES.WebMVC
dotnet user-secrets set "Ollama:MaxContextCharacters" "6000" --project AIVES.WebMVC

# Gửi mã xác minh email
dotnet user-secrets set "GmailSmtp:Username" "<GMAIL_ADDRESS>" --project AIVES.WebMVC
dotnet user-secrets set "GmailSmtp:AppPassword" "<GMAIL_APP_PASSWORD>" --project AIVES.WebMVC

# Đăng nhập Google (tùy chọn)
dotnet user-secrets set "Authentication:Google:ClientId" "<GOOGLE_CLIENT_ID>" --project AIVES.WebMVC
dotnet user-secrets set "Authentication:Google:ClientSecret" "<GOOGLE_CLIENT_SECRET>" --project AIVES.WebMVC
```

SMTP mặc định là `smtp.gmail.com:587`. Google callback dùng đường dẫn `/signin-google`; đăng ký URI tương ứng với địa chỉ ứng dụng, ví dụ `https://localhost:7195/signin-google` khi dùng profile HTTPS.

Nếu chưa có Gemini key, giao diện AI báo chưa cấu hình. Nếu SMTP chưa cấu hình, đăng ký không thể gửi mã xác minh. Có thể cấu hình tài khoản phát triển qua `Development:TestAccount:Email`, `Password`, `DisplayName` trong User Secrets; tài khoản này chỉ được tạo khi chạy Development.

Model mặc định trong repository là `gemini-3.8-flash`; cần kiểm tra model và quyền truy cập thực tế của tài khoản trước khi sử dụng.

## Danh mục Môn học → Chủ đề → Tài liệu

Ngoài ngân hàng câu hỏi, hệ thống có một danh mục phục vụ việc tạo câu hỏi bằng AI:

- **Môn học (Subject)** → **Chủ đề (Topic)** → **Tài liệu (Material)**. Xóa môn học hoặc chủ đề sẽ xóa luôn cấp con (cascade). `Question.TopicId` là khóa ngoại nullable, xóa chủ đề chỉ gỡ liên kết chứ không xóa câu hỏi.
- `Material/Import` nhận tệp `.txt`, `.md`, `.csv`, `.json` tối đa 2 MB; tên tệp thành tiêu đề tài liệu và nội dung được lưu nguyên văn.
- `CatalogService.BuildRagContextAsync` chấm điểm tài liệu đang hoạt động theo các từ khóa của yêu cầu, chỉ giữ tối đa 6 tài liệu khớp nhất và cắt theo `Ollama:MaxContextCharacters`. Không có tài liệu khớp thì câu hỏi được tạo không kèm ngữ cảnh.

```powershell
# Áp dụng migration (đã có trong lúc khởi động, dùng khi cần chạy thủ công)
dotnet ef database update --project AIVES.DAL --startup-project AIVES.WebMVC
```

## Bộ sinh AI: Gemini và Ollama

`IQuestionGenerator` là interface trung lập provider; mỗi provider tự khai báo `Provider` và `IsConfigured`. `QuestionGeneratorRouter` chọn theo thứ tự ưu tiên **Gemini → Ollama**:

- Người dùng có thể chọn `Automatic`, cố định `Gemini` hoặc cố định `Ollama` trong bảng trượt **AI generation**.
- Nếu provider được yêu cầu chưa cấu hình, router tự chuyển sang provider còn lại thay vì báo lỗi.
- Không còn provider nào cấu hình thì trả về thông báo có kiểm soát, không lộ chi tiết nội bộ.
- Trang **System check** hiển thị trạng thái ghép của hai provider, ví dụ `Gemini has no API key, so the local Ollama model (phi3:mini) serves every request.`

Cần cài [Ollama](https://ollama.com) và `ollama pull phi3:mini` để chạy hoàn toàn cục bộ. Gemini và Ollama dùng chung một prompt và một bộ phân tích phản hồi nên trả về cùng cấu trúc.

Hai khác biệt thực tế giữa hai bộ sinh:

- **Số câu.** `phi3:mini` trả về một đối tượng JSON duy nhất bất kể prompt có yêu cầu mảng bao nhiêu phần tử, nên `OllamaQuestionGenerator` gọi **mỗi câu một lần** rồi nối kết quả. Cách này giữ đúng quy tắc "đúng số câu đã yêu cầu" mà không phải tin vào mô hình. Gemini trả mảng trong một lần gọi nên vẫn dùng một request.
- **Một đối tượng JSON cũng được chấp nhận** cho yêu cầu một câu, vì nhiều mô hình nhỏ bỏ qua chỉ dẫn "trả về mảng".

## Đa ngôn ngữ EN / VI

Giao diện hỗ trợ hai ngôn ngữ qua enum `AppLanguage` (`AIVES.DTO/AppLanguage.cs`). Mặc định là **English**; người dùng đổi ngôn ngữ bằng nút EN / VI trên thanh trên cùng.

- Ngôn ngữ được lưu bằng cookie `AIVES.Language`, và có thể ghi đè tạm thời bằng query string `?culture=en` hoặc `?culture=vi`.
- `AppLanguageCultureProvider` chuẩn hóa mọi giá trị qua enum trước khi áp dụng culture.
- Chuỗi hiển thị dùng `L10n.T("English source")`; khoá chính là văn bản tiếng Anh nên khoá thiếu bản dịch vẫn hiển thị tiếng Anh thay vì trống.
- Danh mục bản dịch nằm ở `AIVES.DTO/Localization/AppText.cs`. Thêm ngôn ngữ mới bằng cách bổ sung biến thể trong dictionary và một giá trị enum tương ứng.
- Prompt gửi cho Gemini cũng theo ngôn ngữ đang chọn, nên câu hỏi sinh ra khớp với giao diện.
- Thông báo lỗi của DataAnnotations vẫn dùng tiếng Anh vì thuộc tính attribute phải là hằng số lúc biên dịch; muốn dịch các thông báo này cần chuyển sang resx với `ErrorMessageResourceType`.

## Chạy bằng Docker Compose

Từ thư mục gốc:

```powershell
Copy-Item .env.example .env
# Điền cấu hình SQL và dịch vụ cần sử dụng trong .env.
docker compose up -d --build
docker compose ps
```

Ứng dụng mặc định ở `http://localhost:5201`. Compose chạy web cùng SQL Server và lưu database trong volume `aives-sql-data`. Không đưa `.env` vào Git. Cấu hình tài khoản demo và triển khai HTTPS/domain được mô tả trong [DEPLOY.md](DEPLOY.md).

## Kiểm thử

```powershell
# Kiểm thử nội bộ; không gọi Gemini thật.
dotnet test AIVES_System.slnx -c Release --filter "FullyQualifiedName!~LiveGeminiTests"
```

Để chạy đầy đủ các test HTTP/SQL, đặt biến môi trường `AIVES_TEST_SQL_CONNECTION` trỏ tới SQL Server kiểm thử trước khi chạy. Tài khoản SQL cần quyền tạo/xóa database. Test tạo database riêng với tên GUID và xóa chính database đó sau khi hoàn tất. Nếu thiếu biến này, các ca SQL/HTTP tương ứng sẽ được **skip**.

Test Gemini thật là opt-in: đặt `AIVES_TEST_GEMINI_API_KEY`, tùy chọn `AIVES_TEST_GEMINI_MODEL`, rồi chạy filter `FullyQualifiedName~LiveGeminiTests`. Ca này gọi nhà cung cấp thật và có thể phát sinh chi phí/quota.

Lần kiểm tra ngày **01/10/2026**: build Release 0 lỗi/0 cảnh báo; **112/112 test nội bộ pass, 0 skip** (test Gemini thật là opt-in) trên môi trường có SQL Server. Đây là kết quả tại thời điểm kiểm tra, không phải trạng thái CI tự cập nhật.

Xem [báo cáo kiểm thử và review](docs/Functional-Test-Report.md).

## Migration

Migrations nằm trong DAL; WebMVC là startup project:

```powershell
dotnet ef migrations has-pending-model-changes --project AIVES.DAL --startup-project AIVES.WebMVC
dotnet ef migrations add TenMigration --project AIVES.DAL --startup-project AIVES.WebMVC --output-dir Migrations
```

Các lệnh yêu cầu công cụ `dotnet-ef` tương thích EF Core 10. Database được migrate khi ứng dụng khởi động.

## Giới hạn hiện tại

- Kiểm tra tích hợp thật gần nhất: Gemini trả HTTP 503, Gmail SMTP từ chối xác thực với mã 534. Google OAuth chưa được kiểm thử đầy đủ qua consent/token exchange thật.
- Ollama đã được cài và `phi3:mini` đã kéo về; đã kiểm chứng thật qua bảng trượt AI: 2 câu trong khoảng 9 giây, và có grounding "Grounded in 1 material item" khi bật tài liệu làm ngữ cảnh.
- AI exam room đang tạm tắt cho tới khi luồng phòng thi được thiết kế lại.
- Câu hỏi AI tạo ra là bản nháp để giảng viên duyệt, chưa có bước lưu hàng loạt vào ngân hàng câu hỏi.
- Resend OTP còn rủi ro khi nhiều yêu cầu đồng thời; nếu SMTP lỗi sau khi thay mã, mã cũ đã mất hiệu lực. Chi tiết và hướng xử lý nằm trong báo cáo review.
- Các module roadmap chưa hoàn chỉnh.

Cần giải quyết các điểm trên và kiểm tra cấu hình dịch vụ ngoài trước khi triển khai production.
