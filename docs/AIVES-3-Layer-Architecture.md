# AIVES – Kiến trúc 3 lớp

```mermaid
flowchart TB
    User([Giảng viên / Người dùng])

    subgraph P["1. Presentation Layer – AIVES.WebMVC"]
        Views["Razor Views<br/>Home · Account · Question · AI Exam Room"]
        Controllers["Controllers<br/>AccountController<br/>QuestionController<br/>AiExamRoomController"]
        ViewModels["ViewModels<br/>Account · Question · Rubric · AI Generator"]
        Views <--> Controllers
        Controllers <--> ViewModels
    end

    subgraph B["2. Business Logic Layer – Services"]
        QuestionService["QuestionService<br/>Kiểm tra và xử lý nghiệp vụ câu hỏi"]
        RubricService["RubricService<br/>Xử lý rubric và tiêu chí chấm"]
        GeminiService["GeminiQuestionGenerator<br/>Sinh câu hỏi vấn đáp bằng AI"]
        EmailService["EmailVerificationService<br/>GmailSmtpEmailSender"]
    end

    subgraph D["3. Data Access Layer – Data / Repositories"]
        Repositories["Repository&lt;T&gt;<br/>QuestionRepository"]
        DbContext["ApplicationDbContext<br/>EF Core · ASP.NET Identity"]
        Entities["Entities<br/>Question · Rubric · BloomLevel<br/>ApplicationUser · VerificationCode"]
        Repositories --> DbContext
        DbContext <--> Entities
    end

    Sql[("SQL Server 2022<br/>Docker Volume")]
    Gemini[["Google Gemini API"]]
    Gmail[["Gmail SMTP"]]
    Google[["Google OAuth"]]

    User <--> Views
    Controllers --> QuestionService
    Controllers --> RubricService
    Controllers --> GeminiService
    Controllers --> EmailService
    QuestionService --> Repositories
    RubricService --> Repositories
    EmailService --> DbContext
    DbContext <--> Sql
    GeminiService <--> Gemini
    EmailService <--> Gmail
    Controllers <--> Google
```

## Luồng chính minh họa

1. Giảng viên nhập câu hỏi tại Razor View.
2. `QuestionController` nhận request và chuyển dữ liệu cho `QuestionService`.
3. `QuestionService` kiểm tra nội dung, Bloom level và rubric.
4. `QuestionRepository` thao tác qua `ApplicationDbContext`.
5. EF Core lưu câu hỏi vào SQL Server và kết quả được trả ngược lên giao diện.

## Trách nhiệm các lớp

| Lớp | Thành phần chính | Trách nhiệm |
|---|---|---|
| Presentation | Views, Controllers, ViewModels | Hiển thị UI, nhận request, binding và validation đầu vào |
| Business Logic | Question, Rubric, Gemini, Email services | Thực thi quy tắc nghiệp vụ và điều phối use case |
| Data Access | Repositories, ApplicationDbContext, Entities | Truy xuất dữ liệu bằng EF Core và ánh xạ SQL Server |

