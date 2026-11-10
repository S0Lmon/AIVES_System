# Kịch bản demo AIVES — 15 phút

Mục tiêu: cho thấy trọn quy trình *câu hỏi → kỳ thi → vấn đáp AI → chấm → báo cáo* và phân quyền. Chi tiết đầy đủ: [Manual-Test-Script.md](Manual-Test-Script.md).

## Chuẩn bị trước (không tính vào 15 phút)

- `dotnet run --project AIVES.WebRazor --launch-profile http` → `http://localhost:5201`; model giọng nói đã tải (`scripts/download-speech-models.ps1`).
- Gemini key đã cấu hình; Chrome/Edge có micro.
- 3 tài khoản đã đăng nhập sẵn ở 3 profile/cửa sổ: **Giảng viên** (`demo.aives@gmail.com`), **Admin**, **Sinh viên S1**. Ngân hàng đã có ≥ 6 câu hỏi `active` cho một môn.
- Dự phòng: một kỳ thi đã chấm xong để mở báo cáo nếu AI chậm.

## Dòng thời gian

| Phút | Vai trò | Thao tác | Điểm cần cho thấy | Ca liên quan |
|---|---|---|---|---|
| 0:00–1:00 | — | Mở `/Question` khi chưa đăng nhập → bị chuyển về Login. Đăng nhập Sinh viên, mở `/Question` → AccessDenied | Xác thực + phân quyền | 1.1, 2.1 |
| 1:00–3:00 | Giảng viên | **AI generation**: sinh 2 câu có tài liệu làm ngữ cảnh → *Use this question* → lưu. Mở rubric ma trận, gắn vào câu | AI + RAG, đáp án mong đợi, câu hỏi đào sâu, rubric | 4.6, 4.8, 5.1 |
| 3:00–4:00 | Giảng viên | **Import** tệp mẫu `.csv`, bỏ chọn 1 dòng, sửa 1 dòng, lưu | Nhập hàng loạt có duyệt | 4.10 |
| 4:00–5:30 | Giảng viên | `/Exam/Create`: bắt đầu sau 2 phút, 3 câu chính, 1 câu xoáy, ghi âm, thêm email S1 và S2 → mở Details | Bộ câu riêng từng thí sinh, lịch nối tiếp | 7.1, 7.3 |
| 5:30–6:00 | Sinh viên | `/MyExams`: thấy khung giờ, không thấy câu hỏi | Che câu hỏi trước giờ thi | 8.1 |
| 6:00–10:00 | Sinh viên + GV | S1 *Bắt đầu vấn đáp* → tích đồng ý ghi âm. Câu 1: trả lời đủ ý. Câu 2: cố tình trả lời mơ hồ → AI hỏi xoáy. GV mở `/Interviews/Monitor` ở cửa sổ bên | Giọng nói → chữ gần thời gian thực, hỏi xoáy thích ứng, giám sát real-time | 8.3–8.6, 8.14 |
| 10:00–12:00 | Giảng viên | `/Grading` → bài của S1: transcript, nghe lại bản ghi, điểm AI đề xuất + tín hiệu phụ; sửa 1 điểm → **Chốt điểm** | AI đề xuất, người quyết định (human-in-the-loop) | 9.1, 9.2, 9.5, 9.6 |
| 12:00–13:00 | Sinh viên | `/MyExams/Result` thấy điểm; thử mở kết quả của người khác → 403/404 | Chỉ thấy sau khi chốt | 10.4, 10.5 |
| 13:00–14:00 | Giảng viên | `/Reports`: phân bố điểm, câu khó nhất, độ trễ AI → **Xuất Excel** | Báo cáo + bảng điểm | 10.1–10.3 |
| 14:00–15:00 | Admin | `/Admin/Users` nâng/hạ vai trò, khoá tài khoản; `/Admin/Audit` cho thấy toàn bộ thao tác vừa làm | Quản trị + nhật ký kiểm tra | 2.4, 2.6, 11.5 |

## Phương án dự phòng

| Sự cố | Xử lý |
|---|---|
| Gemini chậm/lỗi (503, 3 request/phút gói miễn phí) | Dùng câu hỏi đã tạo sẵn; ở vấn đáp AI tự chuyển câu, nói rõ đây là hành vi thiết kế (không treo buổi thi) |
| Micro/nhận dạng giọng không chạy | Gõ phím trả lời (được ghi nhận là *Typed*) |
| Worker chấm chưa xong | Mở kỳ thi đã chấm sẵn; hoặc bấm yêu cầu AI chấm lại |
| Hết giờ thi trước khi demo vấn đáp | Tạo kỳ thi mới với giờ bắt đầu = hiện tại + 2 phút ở bước 4:00 |

## Câu nói gợi ý cho phần khó

- Hỏi xoáy: "AI chỉ hỏi thêm khi câu trả lời mơ hồ hoặc thiếu ý; quá giờ AI thì hệ thống bỏ qua, không làm treo buổi thi."
- Chấm điểm: "AI chỉ *đề xuất*; điểm cuối do giảng viên chốt, mọi thay đổi có log."
