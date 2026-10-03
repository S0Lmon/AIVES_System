# AIVES System

AIVES là ứng dụng web hỗ trợ chuẩn bị và quản lý câu hỏi cho thi vấn đáp. Project sử dụng ASP.NET Core MVC, SQL Server, Gemini hoặc Ollama để tạo bản nháp câu hỏi bằng tiếng Việt. Định hướng phát triển là hỗ trợ quy trình thi vấn đáp có AI, trong đó giảng viên duyệt câu hỏi và quyết định điểm.

Phiên bản hiện tại tập trung vào **ngân hàng câu hỏi, ngân hàng rubric dạng ma trận, danh mục môn học/chủ đề/tài liệu, tài khoản và tạo nội dung bằng AI**. Phỏng vấn bằng giọng nói, tổ chức kỳ thi và chấm điểm chưa được triển khai đầy đủ.

## Chức năng hiện có

- **Tài khoản:** đăng ký bằng email thuộc các tên miền được phép (mặc định `gmail.com` và `fpt.edu.vn`, cấu hình qua `Registration:AllowedEmailDomains`; so khớp chính xác phần sau `@`), gửi mã xác minh qua SMTP, xác minh email, gửi lại mã, đăng nhập bằng mật khẩu, đăng xuất và đăng nhập Google khi được cấu hình.
- **Phân quyền:** ba vai trò `Admin`, `Lecturer` (giảng viên), `Student` (sinh viên). Mọi tài khoản mới (mật khẩu hoặc Google) là `Student` và **không** vào được ngân hàng câu hỏi, rubric hay danh mục; chỉ `Lecturer` và `Admin` dùng được các trang này. Admin (đặt qua `AdminAccess:Emails`/`ADMIN_EMAIL`) nâng/hạ vai trò Giảng viên ↔ Sinh viên ở trang **Users** (`/Admin/Users`); thay đổi áp dụng cho phiên đang mở trong vòng một phút. Tài khoản tạo trước khi có phân quyền chưa có vai trò nên được đối xử như sinh viên cho tới khi admin gán vai trò. Tài khoản phát triển (`Development:TestAccount`) và tài khoản demo (`DemoAccount`) được gán `Lecturer`.
- **Ngân hàng câu hỏi:** bốn tab **Bank | Create | AI generation | Bulk generation**. Tab Bank thêm, xem danh sách/chi tiết, sửa, xóa và lọc theo mức Bloom; lưu ngữ cảnh, đáp án mong đợi, rubric, thứ tự hiển thị và trạng thái hoạt động. Rubric là tuỳ chọn.
- **Tạo hàng loạt bằng AI:** chọn một kế hoạch sinh (môn học, chủ đề, số câu, độ khó, mức Bloom), xem trước toàn bộ kết quả, chỉnh sửa câu nào giữ và bỏ câu nào, rồi mới lưu hàng loạt vào ngân hàng. Mức Bloom của kế hoạch được gán server-side từ tên do AI trả về.
- **Ngân hàng rubric:** ba tab **Bank | Create | AI generation**. Mỗi rubric là một ma trận do người dùng tự định nghĩa: 1–10 dòng tiêu chí và 2–6 cột mức đánh giá. Mỗi ô ghi mô tả riêng và điểm riêng. Mức `MaxPoints` của tiêu chí lấy bằng điểm cao nhất trong dòng, `TotalPoints` của rubric lấy bằng tổng các tiêu chí, nên người dùng không phải tự cộng.
- **Trình biên tập ma trận:** thêm/xoá/đổi tên dòng và cột trực tiếp; ma trận luôn hình chữ nhật. Trùng tên mức, ô mồ côi, ô trùng hoặc ma trận không đủ điều kiện đều bị từ chối kèm thông báo.
- **Sinh rubric bằng AI:** cùng cơ chế provider với câu hỏi (ưu tiên Gemini, tự chuyển sang Ollama), sinh ra một ma trận hoàn chỉnh để giảng viên duyệt rồi lưu.
- **Danh mục:** mức Bloom, môn học, chủ đề, tài liệu và rubric được truy xuất qua service/repository để chọn khi tạo câu hỏi.
- **Môn học / chủ đề / tài liệu:** CRUD đầy đủ cho ba cấp danh mục, nhập tài liệu từ tệp `.txt`, `.md`, `.csv`, `.json`. Tài liệu làm ngữ cảnh nền cho câu hỏi AI.
- **Gemini / Ollama:** tạo từ 1 đến 10 câu hỏi theo môn học, chủ đề, chuẩn đầu ra và độ khó, có thể lấy thêm tài liệu của chủ đề làm ngữ cảnh. Router ưu tiên Gemini và tự chuyển sang mô hình Ollama cục bộ khi Gemini chưa cấu hình. Mỗi câu có đáp án mong đợi, mức Bloom và 2 câu hỏi đào sâu. Kết quả là bản nháp để giảng viên duyệt, chưa tự động lưu vào ngân hàng.
- **Bảng trượt AI:** nút **Generate with AI** trên `Question/Create` mở một bảng trượt nhỏ gọn. Chọn bộ sinh, bật/tắt việc dùng tài liệu làm ngữ cảnh, bấm **Use this question** để đổ kết quả vào `Content`, `ExpectedAnswer` và `Bloom level` của biểu mẫu đang mở.
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

### Tài khoản demo khi phát triển

Khi chưa cấu hình SMTP, có thể tạo sẵn hai tài khoản đã xác minh email để đăng nhập ngay: một giảng viên (user thường) và một admin. Tài khoản được tạo khi ứng dụng khởi động; mật khẩu tối thiểu 8 ký tự và có ít nhất một chữ số.

```powershell
# Giảng viên — chỉ được tạo khi chạy Development
dotnet user-secrets set "Development:TestAccount:Email" "lecturer.demo@gmail.com" --project AIVES.WebMVC
dotnet user-secrets set "Development:TestAccount:Password" "<LECTURER_PASSWORD>" --project AIVES.WebMVC
dotnet user-secrets set "Development:TestAccount:DisplayName" "Giảng viên Demo" --project AIVES.WebMVC

# Admin — tài khoản demo được gán quyền qua AdminAccess:Emails
dotnet user-secrets set "DemoAccount:Enabled" "true" --project AIVES.WebMVC
dotnet user-secrets set "DemoAccount:Email" "admin.demo@gmail.com" --project AIVES.WebMVC
dotnet user-secrets set "DemoAccount:Password" "<ADMIN_PASSWORD>" --project AIVES.WebMVC
dotnet user-secrets set "DemoAccount:DisplayName" "Admin Demo" --project AIVES.WebMVC
dotnet user-secrets set "AdminAccess:Emails:0" "admin.demo@gmail.com" --project AIVES.WebMVC
```

Khởi động lại ứng dụng rồi đăng nhập tại `/Account/Login`. Tài khoản giảng viên truy cập ngân hàng câu hỏi, AI và Profile; chỉ tài khoản admin vào được `/Admin`. Nếu đặt thêm `DemoAccount:ResetPasswordOnStartup` là `true`, mật khẩu admin sẽ được đặt lại theo cấu hình mỗi lần khởi động. Không commit mật khẩu thật vào Git.

## Danh mục Môn học → Chủ đề → Tài liệu

Ngoài ngân hàng câu hỏi, hệ thống có một danh mục phục vụ việc tạo câu hỏi bằng AI:

- **Môn học (Subject)** → **Chủ đề (Topic)** → **Tài liệu (Material)**. Xóa môn học hoặc chủ đề sẽ xóa luôn cấp con (cascade). `Question.TopicId` là khóa ngoại nullable, xóa chủ đề chỉ gỡ liên kết chứ không xóa câu hỏi.
- `Material/Import` nhận tệp `.txt`, `.md`, `.csv`, `.json` tối đa 2 MB; tên tệp thành tiêu đề tài liệu và nội dung được lưu nguyên văn.
- `CatalogService.BuildRagContextAsync` chấm điểm tài liệu đang hoạt động theo các từ khóa của yêu cầu, chỉ giữ tối đa 6 tài liệu khớp nhất và cắt theo `Ollama:MaxContextCharacters`. Không có tài liệu khớp thì câu hỏi được tạo không kèm ngữ cảnh.

```powershell
# Áp dụng migration (đã có trong lúc khởi động, dùng khi cần chạy thủ công)
dotnet ef database update --project AIVES.DAL --startup-project AIVES.WebMVC
```

## Rubric dạng ma trận

Mỗi rubric là một ma trận chấm điểm do giảng viên tự định nghĩa, lưu bằng ba bảng:

```text
Rubric ──< RubricLevel        (cột: các mức đánh giá, ví dụ Below / Approaching / Meets / Exceeds)
   │
   └────< RubricCriterion ──< RubricCriterionLevel   (ô: mô tả + điểm của một tiêu chí tại một mức)
```

- Migration `20261001111335_AddRubricMatrix` tạo bảng và **tự đồi bộ rubric cũ**: mỗi rubric được thêm bốn mức `Below / Approaching / Meets / Exceeds` và một ô cho mỗi tiêu chí. Điểm cột chia đều theo thang 1/4 → 4/4, nên thang luôn tăng dần. Descriptor của ô để trống để giảng viên điền sau.
- Giới hạn do service kiểm tra: 1–10 dòng tiêu chí, 2–6 cột mức. Tên mức phải khác nhau, mọi ô phải đủ cặp (tiêu chí × mức) và không trùng.
- **Điểm suy ra, không nhập tay:** `RubricCriterion.MaxPoints` = điểm cao nhất trong dòng, `Rubric.TotalPoints` = tổng các tiêu chí. Sửa một ô là hai giá trị này tự đổi theo.
- Sửa rubric thay toàn bộ ma trận qua `RubricRepository.UpdateAsync`; dòng/cột bị bỏ đi sẽ bị xoá theo cascade. Xoá rubric xoá luôn cột, tiêu chí và ô.
- Tab **AI generation** của `Rubric` sinh ma trận qua cùng router Gemini → Ollama; kết quả phải là hình chữ nhật đầy đủ thì mới cho lưu.
- Cùng một `_RubricMatrixEditor.cshtml` được dùng cho cả Create và Edit, nên hai màn hình không lệch nhau về hành vi.

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
- **Rubric sinh theo cột.** Với rubric, `OllamaRubricGenerator` gọi **mỗi mức một lần** để lấy đủ một hàng của ma trận, cùng lý do trên. Ma trận dở dang hoặc không hình chữ nhật bị `RubricGeneration` từ chối trước khi tới tầng lưu trữ.

`IRubricGenerator` và `RubricGeneratorRouter` lặp lại đúng mẫu của câu hỏi, nên hai loại nội dung dùng chung một quy tắc chọn provider và một câu chữ thông báo khi không provider nào dùng được.

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

Ứng dụng mặc định ở `https://localhost` (chứng chỉ nội bộ của Caddy). Compose chạy Caddy làm reverse proxy HTTPS trước 2 instance web, cùng SQL Server lưu database trong volume `aives-sql-data`. Migrations chạy một lần trong container `migrate`; web dùng login SQL `aives_app` chỉ có quyền đọc/ghi dữ liệu. Không đưa `.env` vào Git. Kiến trúc, scale, tài khoản demo và tên miền được mô tả trong [DEPLOY.md](DEPLOY.md).

Khi chạy nhiều instance ngoài Docker, đặt `Database:MigrateOnStartup=false` cho các instance web và chạy migrations một lần bằng `dotnet AIVES.WebMVC.dll --migrate-only`. Endpoint `GET /health` kiểm tra kết nối database. Nên đặt `DataProtection:CertificatePath`/`DataProtection:CertificatePassword` (file PFX) để khóa cookie lưu trong database được mã hóa, và `ReverseProxy:TerminatesHttps=true` khi reverse proxy đã lo HTTPS. Trước khi deploy bằng Docker, chạy `./scripts/preflight.sh` để kiểm tra `.env`.

## Kiểm thử

```powershell
# Kiểm thử nội bộ; không gọi Gemini thật.
dotnet test AIVES_System.slnx -c Release --filter "FullyQualifiedName!~LiveGeminiTests"
```

Để chạy đầy đủ các test HTTP/SQL, đặt biến môi trường `AIVES_TEST_SQL_CONNECTION` trỏ tới SQL Server kiểm thử trước khi chạy. Tài khoản SQL cần quyền tạo/xóa database. Test tạo database riêng với tên GUID và xóa chính database đó sau khi hoàn tất. Nếu thiếu biến này, các ca SQL/HTTP tương ứng sẽ được **skip**.

Test Gemini thật là opt-in: đặt `AIVES_TEST_GEMINI_API_KEY`, tùy chọn `AIVES_TEST_GEMINI_MODEL`, rồi chạy filter `FullyQualifiedName~LiveGeminiTests`. Ca này gọi nhà cung cấp thật và có thể phát sinh chi phí/quota.

Lần kiểm tra ngày **01/10/2026**: build Release 0 lỗi/0 cảnh báo; **125/125 test nội bộ pass, 0 skip** (test Gemini thật là opt-in) trên môi trường có SQL Server. Đây là kết quả tại thời điểm kiểm tra, không phải trạng thái CI tự cập nhật.

Xem [báo cáo kiểm thử và review](docs/Functional-Test-Report.md).

## Migration

Migrations nằm trong DAL; WebMVC là startup project:

```powershell
dotnet ef migrations has-pending-model-changes --project AIVES.DAL --startup-project AIVES.WebMVC
dotnet ef migrations add TenMigration --project AIVES.DAL --startup-project AIVES.WebMVC --output-dir Migrations
```

Các lệnh yêu cầu công cụ `dotnet-ef` tương thích EF Core 10. Database được migrate khi ứng dụng khởi động.

## Giới hạn hiện tại

- Kiểm tra tích hợp thật gần nhất (03/10/2026): Gemini sinh câu hỏi thành công qua giao diện, có grounding theo tài liệu. Request Gemini trước đây bị gửi nhầm tới địa chỉ Ollama do hai HttpClient trùng tên (đã sửa). Gemini đôi khi trả 503 khi model quá tải; app tự thử lại tối đa 2 lần, và nếu một model quá tải kéo dài thì đổi `GEMINI_MODEL` (ví dụ `gemini-3.5-flash`). Gmail SMTP từ chối xác thực với mã 534 khi dùng mật khẩu thường thay vì App Password. Google OAuth chưa được kiểm thử đầy đủ qua consent/token exchange thật.
- Ollama đã được cài và `phi3:mini` đã kéo về; đã kiểm chứng thật qua bảng trượt AI: 2 câu trong khoảng 9 giây, và có grounding "Grounded in 1 material item" khi bật tài liệu làm ngữ cảnh.
- AI exam room đang tạm tắt cho tới khi luồng phòng thi được thiết kế lại.
- Câu hỏi AI tạo ra là bản nháp để giảng viên duyệt, chưa có bước lưu hàng loạt vào ngân hàng câu hỏi.
- Resend OTP còn rủi ro khi nhiều yêu cầu đồng thời; nếu SMTP lỗi sau khi thay mã, mã cũ đã mất hiệu lực. Chi tiết và hướng xử lý nằm trong báo cáo review.
- Các module roadmap chưa hoàn chỉnh.

Cần giải quyết các điểm trên và kiểm tra cấu hình dịch vụ ngoài trước khi triển khai production.
