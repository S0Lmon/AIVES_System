# Hướng dẫn tự kiểm thử AIVES

Tài liệu này chỉ cách **tự chạy từng workflow** (GitHub Actions và trên máy) và **tự kiểm từng chức năng** FR-01 … FR-15. Mỗi chức năng có hai bước:

1. **Tự động**: một lệnh chạy toàn bộ test đã gắn với yêu cầu đó.
2. **Thủ công**: các bước bấm trên giao diện. Bảng chi tiết có cột KQ nằm trong `docs/Manual-Test-Script.md`; danh sách ca đầy đủ (TC-xx-yy) nằm trong `docs/Test-Cases-By-Requirement.md`.

---

## Phần A. Các workflow

### A1. Tổng quan

| Workflow | Tệp | Chạy khi nào | Kiểm tra gì |
|---|---|---|---|
| **Tests** | `.github/workflows/tests.yml` | Push lên `main`, mọi pull request, hoặc bấm *Run workflow* | Job `test`: build + toàn bộ test .NET với SQL Server tạm. Job `javascript`: test Node cho `wwwroot/js/interview.js`. Job `docker`: build Docker image của web (không push). |
| **Requirements** | `.github/workflows/requirements.yml` | Push lên `main`, mọi pull request, hoặc *Run workflow* (có thể chọn một FR) | Chạy test rồi in bảng Đạt / Lỗi / Bỏ qua **theo từng yêu cầu FR-xx**. |

Hai workflow chạy song song và độc lập. Một pull request chỉ nên merge khi cả hai cùng xanh.

### A2. Xem kết quả trên GitHub

1. Mở pull request → kéo xuống cuối trang, phần **Checks**. Mỗi job có dấu ✅ hoặc ❌; bấm **Details** để xem log.
2. Hoặc vào tab **Actions** → chọn workflow ở cột trái (**Tests** hoặc **Requirements**) → chọn lần chạy.
3. Trong một lần chạy:
   - **Summary**: bảng kết quả (Tests: số đạt/lỗi/bỏ qua và tên test lỗi; Requirements: bảng theo FR-xx).
   - **Artifacts** (cuối trang Summary): `test-results` (tệp `.trx`) của Tests; `requirements-report` (`requirements-report.md` + `.trx`) của Requirements. Tải về để lưu làm minh chứng.
4. Khi job đỏ: mở job → mở bước bị đỏ (thường là *Test* hoặc *Report by requirement*) → tìm dòng `Failed` hoặc mục **Test lỗi** trong Summary.

### A3. Chạy thủ công trên GitHub

**Tests**: Actions → **Tests** → **Run workflow** → chọn nhánh → **Run workflow**.

**Requirements** (chạy toàn bộ hoặc chỉ một yêu cầu):

1. Actions → **Requirements** → **Run workflow**.
2. Chọn nhánh. Ô *Only this requirement*: để trống để chạy tất cả, hoặc nhập ví dụ `FR-08`.
3. **Run workflow** → mở lần chạy mới → xem **Summary**.

> Nút *Run workflow* chỉ xuất hiện khi tệp workflow đã có trên nhánh `main`. Trước đó, workflow chỉ chạy qua pull request.

Bằng dòng lệnh (cần `gh` đã đăng nhập):

```powershell
gh workflow run tests.yml --ref main
gh workflow run requirements.yml --ref main -f requirement=FR-08
gh run list --limit 5                      # xem các lần chạy gần nhất
gh run watch                               # theo dõi lần chạy đang diễn ra
gh run download <run-id> -n requirements-report   # tải báo cáo về máy
```

### A4. Chạy y hệt trên máy

**Bước 1. SQL Server tạm cho test** (để các test HTTP/SQL không bị bỏ qua). Mật khẩu tự đặt, chỉ dùng cho container này:

```powershell
docker run -d --name aives-test-sql -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=<MatKhauTest_Aa1!>" -p 14339:1433 mcr.microsoft.com/mssql/server:2022-latest
$env:AIVES_TEST_SQL_CONNECTION = 'Server=127.0.0.1,14339;Database=AIVES_Test;User Id=sa;Password=<MatKhauTest_Aa1!>;TrustServerCertificate=True'
```

Mỗi lần chạy test tạo cơ sở dữ liệu riêng rồi xoá đi; không đụng tới database ứng dụng. Xong việc: `docker rm -f aives-test-sql`.

**Bước 2. Job `test`**:

```powershell
dotnet build AIVES_System.slnx -c Release
dotnet test AIVES.Tests -c Release --no-build
```

Không đặt `AIVES_TEST_SQL_CONNECTION` thì khoảng 50 test cần SQL bị **bỏ qua** (không phải lỗi).

**Bước 3. Job `javascript`**:

```powershell
node --test scripts/tests/interview-recording.test.cjs
```

**Bước 4. Job `docker`**:

```powershell
docker build -f AIVES.WebRazor/Dockerfile -t aives-web:local .
```

**Bước 5. Workflow Requirements** (bảng theo yêu cầu):

```powershell
dotnet test AIVES.Tests -c Release --no-build --logger "trx;LogFileName=r.trx" --results-directory TestResults
python scripts/requirements_report.py TestResults/r.trx
# Chỉ một yêu cầu:
dotnet test AIVES.Tests -c Release --no-build --filter "Requirement=FR-08" --logger "trx;LogFileName=fr08.trx" --results-directory TestResults
python scripts/requirements_report.py TestResults/fr08.trx --only FR-08
```

Trên Windows, nếu chữ tiếng Việt hiện lỗi trong terminal: `$env:PYTHONIOENCODING='utf-8'`.

### A5. Thêm test mới cho đúng yêu cầu

Gắn nhãn lên lớp hoặc phương thức test (gắn được nhiều nhãn):

```csharp
[Trait("Requirement", "FR-12")]
[Fact]
public async Task TenTestMoTaHanhVi() { ... }
```

Test chưa gắn nhãn vẫn chạy, nhưng workflow Requirements sẽ cảnh báo *Test chưa gắn yêu cầu*. Test cần SQL Server dùng `[SqlFact]` / `[SqlTheory]`.

---

## Phần B. Chuẩn bị để tự kiểm thủ công

1. Chạy ứng dụng: `.\scripts\start-dev.ps1` (SQL Server + VieNeu-TTS + app), hoặc `dotnet run --project AIVES.WebRazor --launch-profile http` → `http://localhost:5201`.
2. Tài khoản (User Secrets, xem README mục *Tài khoản demo khi phát triển*):

| Vai trò | Dùng để | Cách có |
|---|---|---|
| Giảng viên A (GV) | Câu hỏi, rubric, kỳ thi, chấm điểm | `Development:TestAccount:*` |
| Admin (AD) | Quản trị, nhật ký hệ thống | `DemoAccount:*` + `AdminAccess:Emails:0` |
| Sinh viên S1, S2 | Thi vấn đáp, xem kết quả | Đăng ký (cần SMTP) |
| Giảng viên B (GV2) | Kiểm tra quyền giữa hai giảng viên | Đăng ký rồi AD nâng vai trò |

3. Cần thêm: `Gemini:ApiKey` cho phần AI; Chrome/Edge và micro cho phần vấn đáp. Dùng **hai profile trình duyệt** (hoặc một cửa sổ ẩn danh) để đăng nhập hai vai trò cùng lúc.
4. Đánh dấu kết quả vào cột **KQ** của `docs/Manual-Test-Script.md`.

---

## Phần C. Từng chức năng

Lệnh tự động ở mỗi mục giả định đã build (`dotnet build -c Release`) và đã đặt `AIVES_TEST_SQL_CONNECTION` (mục A4). Từ FR-02 trở đi chỉ ghi phần lọc; lệnh đầy đủ là `dotnet test AIVES.Tests -c Release --no-build --filter Requirement=FR-xx`. Trên GitHub: Actions → **Requirements** → *Run workflow* → nhập `FR-xx`.

### FR-01 Tài khoản: đăng ký, xác minh email, đăng nhập

- **Tự động**: `dotnet test AIVES.Tests -c Release --no-build --filter Requirement=FR-01`
- **Thủ công** (Manual-Test-Script mục 1):
  1. Đăng ký bằng `x@yahoo.com` và `x@gmail.com.evil.com` → bị từ chối; `x@gmail.com`, `x@fpt.edu.vn` → được nhận.
  2. Mật khẩu 7 ký tự, 8 ký tự không có số → báo lỗi; `Abcdef12` → đạt.
  3. Nhập OTP sai 5 lần → bị chặn; bấm *Gửi lại* ngay → bị chặn; chờ rồi gửi lại → mã cũ hết hiệu lực.
  4. Đăng nhập khi chưa xác minh → không vào được. Sai mật khẩu 5 lần → khoá tạm.
  5. Đăng nhập với `/Account/Login?ReturnUrl=https://evil.com` → vẫn ở lại trang nội bộ.
  6. Đăng xuất rồi bấm Back → không xem lại được trang nội bộ.

### FR-02 Phân quyền Admin / Lecturer / Student

- **Tự động**: `--filter Requirement=FR-02`
- **Thủ công** (mục 2):
  1. Chưa đăng nhập, mở `/Question`, `/Rubric`, `/Exam`, `/Catalog`, `/Grading`, `/Reports`, `/Admin` → về trang đăng nhập.
  2. S1 mở các trang trên → *Access denied*. GV mở `/Admin/Users` → *Access denied*.
  3. AD nâng S1 thành Lecturer → trong ≤ 1 phút S1 vào được `/Question` (không cần đăng nhập lại). Hạ lại → mất quyền.

### FR-03 Ngân hàng câu hỏi (`/Question`)

- **Tự động**: `--filter Requirement=FR-03`
- **Thủ công** (mục 4):
  1. Tạo câu hỏi chỉ có nội dung (bỏ trống chủ đề, đáp án, rubric) → lưu được.
  2. Nội dung 3 ký tự → bị từ chối. Lọc theo Bloom → đúng.
  3. Sửa câu hỏi → ngày tạo giữ nguyên. Xoá → biến mất; mở `/Question/Details/9999` → 404.
  4. Mở sửa cùng một câu ở hai cửa sổ → mỗi bên thấy tên người kia; bên kia lưu/xoá thì có cảnh báo.
  5. Nhập `<script>alert(1)</script>` vào nội dung → hiện thành chữ, không chạy.

### FR-04 Sinh câu hỏi bằng AI và RAG

- **Tự động**: `--filter Requirement=FR-04` (test Gemini thật bị bỏ qua nếu chưa đặt khoá; đó là chủ đích)
- **Thủ công** (mục 4, phần AI):
  1. Tab **AI**: chọn môn + chủ đề có tài liệu, 1 câu → có đáp án, Bloom, 2 câu xoáy và dòng *Grounded in … material*.
  2. Xin 0 hoặc 11 câu → bị chặn, không gọi AI.
  3. Tab **Bulk**: 10 câu, bỏ chọn 3, sửa 1, lưu → đúng 7 câu được lưu.
  4. Import `.csv`/`.xlsx`/`.json` (mẫu tải ở trang) → vào màn hình duyệt; tệp thiếu cột hoặc Bloom = 9 → báo lỗi theo dòng.
  5. Bỏ `Gemini:ApiKey` (bật Ollama nếu có) → tự chuyển sang Ollama, hoặc báo "chưa cấu hình" có kiểm soát.

### FR-05 Rubric ma trận (`/Rubric`)

- **Tự động**: `--filter Requirement=FR-05`
- **Thủ công** (mục 5):
  1. Tạo 3 tiêu chí × 4 mức, điền điểm → tổng điểm tự cập nhật khi đổi ô.
  2. 0 dòng, 11 dòng, 1 cột, 7 cột, tên mức trùng → bị từ chối kèm thông báo.
  3. Thêm/xoá/đổi tên dòng, cột → ma trận luôn đủ ô. Xoá rubric → biến mất hoàn toàn.

### FR-06 Danh mục Môn học → Chủ đề → Tài liệu (`/Catalog`)

- **Tự động**: `--filter Requirement=FR-06` (22 test, gồm nhập tệp qua trang và xoá dây chuyền trên SQL Server)
- **Thủ công** (mục 3):
  1. Tạo môn → trang môn mở ra với form thêm chủ đề → thêm chủ đề.
  2. Tab **Material** → *Import material*: thử `.pdf`, `.docx`, `.pptx`, `.txt`. Mở tài liệu: PDF có tiêu đề `## Page n`, PowerPoint có `## Slide n` và phần *Notes*.
  3. Nhập tệp `.exe` → *Supported formats…*; tệp > 20 MB → *The file is larger than 20 MB.*; tệp rỗng → *Choose a file to import.*
  4. Xoá chủ đề có câu hỏi → tài liệu mất, câu hỏi còn (chỉ mất chủ đề).
  5. Xoá môn → chủ đề và tài liệu mất theo; câu hỏi còn, không còn gắn môn.

### FR-07 Kỳ thi & lịch thi (`/Exam`, `/MyExams`)

- **Tự động**: `--filter Requirement=FR-07`
- **Thủ công** (mục 7):
  1. GV tạo kỳ thi: 3 email, 10 phút/thí sinh → khung giờ nối tiếp, mỗi người một bộ câu riêng.
  2. Email trùng, giờ bắt đầu trong quá khứ, danh sách rỗng → bị từ chối.
  3. Sửa câu gốc trong ngân hàng sau khi giao → câu trong kỳ thi không đổi.
  4. GV2 mở kỳ thi của GV → không thấy; AD thấy tất cả. Sau giờ bắt đầu → không sửa/xoá được.
  5. S1 mở `/MyExams` → thấy khung giờ, không thấy câu hỏi.

### FR-08 Phỏng vấn AI bằng giọng nói

- **Tự động**: `--filter Requirement=FR-08`, cộng thêm `node --test scripts/tests/interview-recording.test.cjs` (ghi âm, tải lên, thử lại phía trình duyệt)
- **Thủ công** (mục 8; dùng Chrome/Edge và micro):
  1. Kỳ thi có ghi âm: bấm bắt đầu khi chưa tích đồng ý → không bắt đầu được.
  2. Đến giờ, S1 bắt đầu → câu hỏi được đọc to (VieNeu, hoặc Piper/giọng trình duyệt khi VieNeu tắt).
  3. Trả lời đủ ý → sang câu kế. Trả lời mơ hồ → đúng một câu hỏi xoáy.
  4. Để hết giờ → tự nộp, đánh dấu *Quá giờ*. Sửa chữ bằng tay → ghi nhận *Typed*.
  5. Từ chối micro hoặc dùng Firefox → vẫn gõ phím được.
  6. GV mở trang giám sát → thấy câu hỏi, câu trả lời, câu xoáy theo thời gian thực.
  7. S2 mở URL phòng thi của S1 → 403/404. Thi xong → không vào làm lại được.

### FR-09 Chấm điểm có AI hỗ trợ (`/Grading`)

- **Tự động**: `--filter Requirement=FR-09`
- **Thủ công** (mục 9):
  1. Sau buổi thi, chờ worker chấm → mỗi câu có điểm AI đề xuất; câu không trả lời được 0 điểm.
  2. Nhập điểm vượt thang → bị từ chối. *Lưu nháp* → S1 chưa thấy kết quả.
  3. *Chốt điểm* → S1 thấy kết quả /10 ở `/MyExams`. Mở lại, sửa, chốt lại → S1 thấy điểm mới.
  4. S2 mở kết quả của S1 → 403/404.

### FR-10 Ghi âm/ghi hình & bảo mật

- **Tự động**: `--filter Requirement=FR-10`
- **Thủ công** (mục 8–9):
  1. Kỳ thi *ghi âm*: mỗi câu trả lời và cả buổi thi có bản ghi trong trang chấm.
  2. Kỳ thi *không ghi*: không có bản ghi, không hỏi đồng ý.
  3. Mở tệp trong thư mục recordings bằng trình phát nhạc → không phát được (đã mã hoá).
  4. GV2 hoặc S1 phát bản ghi → bị từ chối; GV của kỳ thi và AD phát được.

### FR-11 Nhật ký kiểm tra (`/Grading/Audit/{id}`, `/Admin/Audit`)

- **Tự động**: `--filter Requirement=FR-11` (13 test)
- **Thủ công** (mục 9–11):
  1. Tạo/sửa/xoá một kỳ thi, cho S1 thi (có đồng ý ghi âm), đổi điểm, chốt/mở lại, phát bản ghi, xuất Excel.
  2. Mở `/Grading/Audit/{id}` → mỗi hành động trên là một dòng; dòng đổi điểm ghi *cũ → mới (AI x)*.
  3. AD đổi vai trò, phân công môn, lưu `/Admin/Settings` → mở `/Admin/Audit` thấy đủ; lọc theo loại hành động hoạt động.
  4. GV mở `/Admin/Audit` → *Access denied*.

### FR-12 Báo cáo & xuất bảng điểm

- **Tự động**: `--filter Requirement=FR-12` (12 test)
- **Thủ công** (mục 10):
  1. `/Reports` → chọn kỳ thi → có số đã thi/đã chốt, TB, trung vị, min, max, tỷ lệ đạt (≥ 5), phân bố điểm, câu khó nhất, theo Bloom, độ trễ AI.
  2. Đối chiếu bằng tay với điểm đã chốt: chỉ điểm **đã chốt** được tính; bản nháp không tính.
  3. Kỳ thi chưa có bài nào chốt → trang mở bình thường, các ô để trống.
  4. **Xuất Excel**: có tên trường, môn/chủ đề, kỳ thi, ngày (giờ VN), STT, MSSV (tách từ email FPT), họ tên, email, điểm từng câu, tổng /10, ghi chú (*Did not sit*, *Viva not finished*, *Grade not confirmed*), hai dòng chữ ký.
  5. S1 mở `/Reports` hoặc URL xuất → *Access denied*; GV2 → 404.

### FR-13 Quản trị (`/Admin`)

- **Tự động**: `--filter Requirement=FR-13`
- **Thủ công** (mục 11):
  1. `/Admin/Users`: lọc, đổi vai trò → có hiệu lực ≤ 1 phút.
  2. Vô hiệu hoá một tài khoản đang đăng nhập → bị đăng xuất ngay; kích hoạt lại → đăng nhập được.
  3. `/Admin/Subjects`: gán môn cho GV → chỉ GV đó tạo được kỳ thi cho môn này.
  4. `/Admin/Settings`: đổi ngôn ngữ, tốc độ đọc, giọng (nghe thử), thời hạn lưu → kỳ thi mới áp dụng.
  5. AD tự hạ hoặc khoá chính mình → bị từ chối.

### FR-14 Thuật ngữ STT (`/Glossary`)

- **Tự động**: `--filter Requirement=FR-14`
- **Thủ công** (mục 6):
  1. Thêm `SQL` với cách đọc `ét kiu eo`.
  2. Trong buổi thi, nói "ét kiu eo" → bản ghi chữ được sửa thành `SQL`, bản gốc vẫn giữ.

### FR-15 Đa ngôn ngữ & Giao diện

- **Tự động**: `--filter Requirement=FR-15`
- **Thủ công** (mục 12):
  1. Bấm EN ↔ VI hoặc thêm `?culture=vi` → toàn bộ giao diện đổi, nhớ qua cookie. `?culture=xx` → về mặc định, không lỗi.
  2. Thu cửa sổ về 390 × 844 (DevTools → thiết bị di động) → menu và trang chính không vỡ.
  3. Hai cửa sổ GV: thêm/sửa/xoá câu hỏi ở một bên → bên kia tự cập nhật kèm thông báo; nhiều tab của một người tính là 1 người online.
  4. Mở `/health` → `Healthy`; tắt SQL Server → báo lỗi.

---

## Phần D. Lưu kết quả

- **Tự động**: tải artifact `requirements-report` của lần chạy trên GitHub, hoặc lưu đầu ra của `scripts/requirements_report.py`.
- **Thủ công**: điền cột KQ trong `docs/Manual-Test-Script.md`; ảnh chụp lỗi để trong `docs/test-evidence/`.
- **Báo cáo PDF**: `docs/test-evidence/AIVES-Bao-cao-kiem-thu-2026-11-11.pdf` (kết quả theo yêu cầu, chi tiết FR-06, FR-11, FR-12).
