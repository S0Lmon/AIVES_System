# Kiểm tra và nâng cấp chức năng AIVES — 21/10/2026

## Phạm vi kiểm tra

Đối chiếu danh sách chức năng với mã nguồn Razor Pages, kiểm thử nghiệp vụ,
HTTP với SQL Server thật trong database tạm riêng, SignalR bằng client thật,
và nhận dạng lại câu đọc tiếng Việt bằng Whisper/Piper có sẵn trên máy.
Không sử dụng database nghiệp vụ để tạo kỳ thi hoặc tài khoản kiểm thử tự động.

| Nhóm chức năng | Bằng chứng kiểm tra trong project |
|---|---|
| Tài khoản, email được phép, OTP, Google | `AccountTests`, `GoogleAccountTests`, `FunctionalTests`, `SqlIntegrationTests` |
| Vai trò, đổi quyền, vô hiệu hóa tài khoản | `FunctionalTests`, `RazorRealtimeTests`, `OperationsTests` |
| Kỳ thi, lịch thi, trạng thái, phân bổ và chụp câu hỏi | `ExamServiceTests`, `CandidateStatusTests`, `ExamQuestionAllocatorTests`, `FunctionalTests` |
| Vấn đáp, hỏi xoáy, giới hạn, timeout, transcript | `InterviewServiceTests`, `FollowUpGeneratorTests`, `InterviewHubTests` |
| Nhận dạng, đọc câu hỏi, Gemini speech, thuật ngữ | `AudioTests`, `LiveTranscriberTests`, `SpeechModelTests`, `GeminiSpeechTests`, `OperationsTests` |
| Chấm điểm AI, giảng viên duyệt/chốt/mở lại, kết quả | `GradingTests`, `FunctionalTests` |
| Bản ghi, đồng ý, mã hóa, quyền phát, thời hạn lưu | `OperationsTests`, `FunctionalTests`, kiểm thử JavaScript bản ghi |
| Audit, phân công môn, cấu hình | `OperationsTests`, `FunctionalTests` |
| Báo cáo lớp, tín hiệu, xuất Excel | `GradingTests`, `FunctionalTests` |
| Ngân hàng câu hỏi, tạo hàng loạt, nhập dữ liệu | `QuestionTaxonomyTests`, `BulkPlanTests`, `FunctionalTests` |
| Rubric ma trận, chỉnh dòng/cột, sinh rubric | `RubricMatrixTests`, `FunctionalTests` |
| Danh mục, nhập tài liệu và RAG | `AiProviderTests`, `FunctionalTests` |
| Provider Gemini/Ollama, bảng trượt AI, đường dẫn cũ | `AiProviderTests`, `AiHttpClientRegistrationTests`, `FunctionalTests` |
| Ngôn ngữ, giao diện được render, ranh giới ba lớp | `LocalizationTests`, `FunctionalTests`, `LayerTests`, `RazorRealtimeTests` |

## Các lỗi đã sửa

1. **Chính sách đăng ký:** khi thiếu cấu hình, cho phép cả `gmail.com` và
   `fpt.edu.vn`; kiểm tra địa chỉ email hợp lệ và so khớp chính xác tên miền.
   Tài khoản Google mới cũng tuân theo chính sách này; tài khoản đã tồn tại
   vẫn được đăng nhập/liên kết khi chính sách thay đổi.
2. **Phiên HTTP:** kiểm tra security stamp mỗi request có xác thực. Khi admin
   khóa hoặc đổi vai trò, cookie cũ bị từ chối ngay ở request tiếp theo.
3. **Theo dõi vấn đáp:** sinh viên, kể cả người có tên trong kỳ thi, không được
   tham gia nhóm SignalR chứa câu hỏi/transcript của các thí sinh khác.
   Giảng viên phụ trách và admin tiếp tục theo dõi được.
4. **Giới hạn vấn đáp:** đóng lượt đang mở khi truy vấn trạng thái sau thời hạn
   trả lời; kết thúc phiên đã hết giờ trước khi khôi phục request AI bị gián đoạn;
   giữ nguyên câu trả lời đã lưu; ghi audit khi kết thúc do khôi phục.
5. **Đợi AI:** không phát câu mới khi AI trả kết quả sau giờ kết thúc phiên;
   bắt đầu đồng hồ câu tiếp theo sau khi AI xử lý xong; áp dụng timeout cả với
   provider không tuân theo cancellation token.
6. **Dữ liệu đầu vào:** từ chối kiểu trả lời ngoài enum; API trả 400 cho input
   không hợp lệ thay vì lỗi 500.
7. **Bản ghi:** server chỉ nhận bản ghi sau khi lượt đã có câu trả lời. Trình duyệt
   dừng thu khi nộp, đợi server xác nhận lưu rồi tải lên; giữ bản ghi khi cần thử
   nộp lại, tách buffer từng câu, hiển thị lỗi nếu tải bản ghi thất bại.
8. **Camera:** URL phòng Whisper tự chuyển thí sinh có yêu cầu camera sang phòng
   trình duyệt đã hỗ trợ MediaRecorder, thay vì hiển thị trang không có nút bắt đầu.
9. **Kiểm thử:** bỏ `Temporary skip` khỏi 18 bài kiểm tra audio, live transcription
   và InterviewHub; bổ sung hồi quy cho các lỗi trên và 3 bài JavaScript về thứ tự
   tải bản ghi, thử lại và hai câu liên tiếp.
10. **Dependency:** nâng các package Microsoft đang ở `10.0.12` lên `10.0.13`,
    ghi đè PKCS cũ do SqlClient kéo vào, đặt runtime Docker ở `10.0.13`.
    Tham khảo thông báo Microsoft:
    [SignalR](https://github.com/advisories/GHSA-334f-vvp4-7g6p),
    [PKCS](https://github.com/advisories/GHSA-846f-f764-76mx),
    [XML](https://github.com/advisories/GHSA-r955-8q5j-3h83).

## Chạy lại kiểm thử

Đặt `AIVES_TEST_SQL_CONNECTION` tới SQL Server có quyền tạo/xóa database **test**.
Các fixture tự tạo database với tên `AIVES_FunctionalTest_*` / `AIVES_LayerTest_*`
và dọn riêng chúng khi kết thúc. Không đặt connection tới database nghiệp vụ.
Đặt `AIVES_SPEECH_MODELS` tới thư mục model đã tải.

```powershell
dotnet test AIVES.Tests/AIVES.Tests.csproj
node --test scripts/tests/interview-recording.test.cjs
node --check AIVES.WebRazor/wwwroot/js/interview.js
```

`LiveGeminiTests` cần `AIVES_TEST_GEMINI_API_KEY` và tùy chọn
`AIVES_TEST_GEMINI_MODEL`; đây là request thật, có dùng hạn mức của provider.

Kết quả trên máy ngày 21/10/2026:

- Lượt đầy đủ với SQL Server và model giọng nói: **339 đạt, 0 lỗi, 1 bỏ qua**
  (Gemini thật cần bật riêng).
- Lượt Gemini thật với cấu hình server: **1 đạt, 0 lỗi**, sinh một câu hỏi
  hợp lệ có hai câu hỏi đào sâu. Tổng cộng cả 340 bài .NET đều đã chạy đạt.
- JavaScript bản ghi: **3 đạt, 0 lỗi**; kiểm tra cú pháp JavaScript đạt.

## Giới hạn bằng chứng

- Kiểm thử tự động không thay thế kiểm tra micro/camera vật lý và chất lượng
  nhận dạng từng giọng nói trên mọi trình duyệt/thiết bị.
- SMTP và Google OAuth cần tài khoản ngoài để kiểm tra đầu cuối. Bộ HTTP tích hợp
  dùng mail sender giả để kiểm tra luồng OTP mà không gửi email thật.
- Ollama được kiểm tra qua provider giả; chưa có bằng chứng model Ollama thật trên
  máy triển khai này.
- Vô hiệu hóa cookie áp dụng ở request HTTP tiếp theo; thay đổi này chưa bổ sung
  cơ chế chủ động đóng mọi WebSocket đang kết nối khi admin đổi quyền.
- Không có migration mới; bản nâng cấp không đổi schema hoặc dữ liệu chấm điểm.
