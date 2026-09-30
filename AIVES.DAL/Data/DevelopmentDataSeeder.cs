using AIVES.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIVES.DAL.Data;

public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var rubricDefinitions = new[]
        {
            new RubricDefinition(
                "[DEMO] Kiến thức nền tảng",
                "Đánh giá độ chính xác, khả năng giải thích và ví dụ minh họa cho câu hỏi khái niệm.",
                [
                    new("Nêu đúng khái niệm", 4, "Trình bày chính xác định nghĩa và các thành phần cốt lõi."),
                    new("Giải thích rõ ràng", 3, "Diễn đạt mạch lạc, sử dụng thuật ngữ phù hợp."),
                    new("Ví dụ minh họa", 2, "Đưa ra ví dụ đúng và liên quan đến câu hỏi."),
                    new("Phản hồi câu hỏi phụ", 1, "Làm rõ được ý khi giảng viên hỏi sâu hơn.")
                ]),
            new RubricDefinition(
                "[DEMO] Vận dụng và phân tích",
                "Đánh giá khả năng lựa chọn giải pháp, lập luận kỹ thuật và phân tích đánh đổi.",
                [
                    new("Lựa chọn giải pháp", 3, "Đề xuất phương án phù hợp với bối cảnh đã cho."),
                    new("Lập luận kỹ thuật", 3, "Giải thích được vì sao lựa chọn đó hợp lý."),
                    new("Phân tích đánh đổi", 3, "Nhận diện ưu điểm, hạn chế và rủi ro của phương án."),
                    new("Trình bày", 1, "Câu trả lời có cấu trúc, súc tích và dễ theo dõi.")
                ]),
            new RubricDefinition(
                "[DEMO] Thiết kế hệ thống",
                "Đánh giá tư duy thiết kế, tính nhất quán và khả năng bảo vệ quyết định kiến trúc.",
                [
                    new("Mô hình hóa", 3, "Xác định đúng thành phần, quan hệ và trách nhiệm."),
                    new("Tính khả thi", 3, "Giải pháp có thể triển khai với công nghệ và nguồn lực đã nêu."),
                    new("Bảo mật và độ tin cậy", 2, "Xem xét lỗi, bảo mật, toàn vẹn và khả năng phục hồi."),
                    new("Bảo vệ quyết định", 2, "So sánh được với phương án thay thế và bảo vệ lựa chọn.")
                ])
        };

        var rubrics = new Dictionary<string, Rubric>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in rubricDefinitions)
        {
            var rubric = await dbContext.Rubrics
                .Include(item => item.Criteria)
                .SingleOrDefaultAsync(item => item.Name == definition.Name, cancellationToken);

            if (rubric is null)
            {
                rubric = new Rubric
                {
                    Name = definition.Name,
                    Description = definition.Description,
                    TotalPoints = definition.Criteria.Sum(item => item.MaxPoints),
                    Criteria = definition.Criteria.Select((item, index) => new RubricCriterion
                    {
                        Criterion = item.Name,
                        Description = item.Description,
                        MaxPoints = item.MaxPoints,
                        Order = index + 1
                    }).ToList()
                };
                dbContext.Rubrics.Add(rubric);
            }

            rubrics[definition.Name] = rubric;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var questionDefinitions = new[]
        {
            new QuestionDefinition("C# & .NET", "Hãy phân biệt value type và reference type trong C#. Điều gì xảy ra khi truyền từng loại vào một phương thức?", "Value type lưu trực tiếp giá trị và mặc định được sao chép khi gán/truyền tham số; reference type lưu tham chiếu đến đối tượng. Cần đề cập stack/heap một cách thận trọng, ref/out/in và tác động khi thay đổi trạng thái đối tượng.", 2, "[DEMO] Kiến thức nền tảng"),
            new QuestionDefinition("C# & .NET", "Giải thích cơ chế async/await trong C# và vì sao không nên dùng .Result trong ứng dụng web.", "async/await xây dựng state machine và giải phóng luồng trong lúc chờ I/O. .Result có thể chặn luồng, gây thread starvation hoặc deadlock trong một số synchronization context; nên await xuyên suốt.", 2, "[DEMO] Kiến thức nền tảng"),
            new QuestionDefinition("C# & .NET", "Trong tình huống nào bạn chọn interface thay vì abstract class? Hãy bảo vệ lựa chọn của mình.", "Interface phù hợp cho hợp đồng và đa kế thừa hành vi; abstract class phù hợp khi cần trạng thái hoặc triển khai dùng chung. Câu trả lời cần đề cập khả năng kiểm thử, coupling và evolution của API.", 4, "[DEMO] Vận dụng và phân tích"),
            new QuestionDefinition("C# & .NET", "Phân tích vòng đời Transient, Scoped và Singleton trong dependency injection của ASP.NET Core.", "Nêu đúng thời điểm tạo/hủy của ba vòng đời, scoped theo request web, rủi ro captive dependency khi singleton giữ scoped service, thread safety và cách chọn theo trạng thái của dịch vụ.", 4, "[DEMO] Vận dụng và phân tích"),
            new QuestionDefinition("EF Core", "Change Tracker của EF Core hoạt động như thế nào và khi nào nên dùng AsNoTracking?", "Change Tracker lưu trạng thái entity và phát hiện thay đổi để sinh lệnh cập nhật. AsNoTracking phù hợp truy vấn chỉ đọc, giảm bộ nhớ/CPU; không phù hợp khi entity cần được sửa rồi SaveChanges trực tiếp.", 2, "[DEMO] Kiến thức nền tảng"),
            new QuestionDefinition("EF Core", "So sánh eager loading, explicit loading và lazy loading. Bạn sẽ chọn cách nào cho trang danh sách câu hỏi?", "Phân biệt Include, tải tường minh và proxy lazy loading; nhận diện N+1. Với trang danh sách nên projection hoặc eager loading có kiểm soát, chỉ lấy trường cần thiết và phân trang.", 4, "[DEMO] Vận dụng và phân tích"),
            new QuestionDefinition("EF Core", "Hãy thiết kế transaction khi tạo một kỳ thi cùng danh sách thí sinh và bộ câu hỏi.", "Các ghi dữ liệu liên quan phải nguyên tử; dùng transaction của SaveChanges hoặc BeginTransaction khi có nhiều lần lưu. Cần xử lý rollback, concurrency, idempotency và không giữ transaction trong lúc gọi dịch vụ AI bên ngoài.", 3, "[DEMO] Thiết kế hệ thống"),
            new QuestionDefinition("EF Core", "Nếu hai giảng viên cùng sửa một rubric, bạn xử lý optimistic concurrency như thế nào?", "Thêm concurrency token/rowversion, kiểm tra DbUpdateConcurrencyException, tải lại dữ liệu, cho người dùng chọn ghi đè hoặc hợp nhất; không âm thầm mất cập nhật.", 4, "[DEMO] Thiết kế hệ thống"),
            new QuestionDefinition("SQL Server", "Clustered index và nonclustered index khác nhau thế nào?", "Clustered index quyết định thứ tự lưu trữ logic của dữ liệu và mỗi bảng chỉ có một; nonclustered có cấu trúc riêng chứa khóa và row locator. Cần liên hệ selectivity, covering index và chi phí ghi.", 2, "[DEMO] Kiến thức nền tảng"),
            new QuestionDefinition("SQL Server", "Một truy vấn lọc theo BloomLevelId và IsActive đang chậm. Bạn sẽ chẩn đoán và tối ưu ra sao?", "Đọc actual execution plan, kiểm tra scan/seek, cardinality và statistics; cân nhắc composite/covering index theo mẫu truy vấn, đo IO/time trước sau và tránh tối ưu khi chưa có bằng chứng.", 4, "[DEMO] Vận dụng và phân tích"),
            new QuestionDefinition("SQL Server", "Giải thích dirty read, non-repeatable read và phantom read. Isolation level nào phù hợp cho hệ thống thi?", "Mô tả đúng ba hiện tượng và các isolation level. Lựa chọn cần cân bằng tính nhất quán với khóa/độ đồng thời; có thể cân nhắc read committed snapshot và transaction chặt cho thao tác chốt điểm.", 4, "[DEMO] Vận dụng và phân tích"),
            new QuestionDefinition("AIVES", "Hãy đề xuất kiến trúc lưu transcript và điểm AI sao cho giảng viên vẫn là người quyết định điểm cuối cùng.", "Tách transcript bất biến, phiên bản đề xuất AI và điểm cuối cùng; lưu audit log người/thời điểm/lý do điều chỉnh, phân quyền, mã hóa dữ liệu nhạy cảm và cơ chế truy vết khi khiếu nại.", 4, "[DEMO] Thiết kế hệ thống")
        };

        var existingDemoQuestions = await dbContext.Questions
            .Where(question => question.Context.StartsWith("[DEMO]"))
            .OrderBy(question => question.Id)
            .ToListAsync(cancellationToken);

        var displayOrder = await dbContext.Questions.MaxAsync(question => (int?)question.DisplayOrder, cancellationToken) ?? 0;
        foreach (var definitionGroup in questionDefinitions.GroupBy(item => $"[DEMO] {item.Context}"))
        {
            var existingForContext = existingDemoQuestions
                .Where(question => question.Context == definitionGroup.Key)
                .ToList();
            var definitionsForContext = definitionGroup.ToList();

            if (existingForContext.Count > definitionsForContext.Count)
                dbContext.Questions.RemoveRange(existingForContext.Skip(definitionsForContext.Count));

            for (var index = 0; index < Math.Min(existingForContext.Count, definitionsForContext.Count); index++)
            {
                var existing = existingForContext[index];
                var definition = definitionsForContext[index];
                existing.Content = definition.Content;
                existing.ExpectedAnswer = definition.ExpectedAnswer;
                existing.BloomLevelId = definition.BloomLevelId;
                existing.RubricId = rubrics[definition.RubricName].Id;
                existing.IsActive = true;
                existing.ModifiedDate = DateTime.UtcNow;
            }

            foreach (var definition in definitionsForContext.Skip(existingForContext.Count))
            {
                dbContext.Questions.Add(new Question
                {
                    Content = definition.Content,
                    Context = definitionGroup.Key,
                    ExpectedAnswer = definition.ExpectedAnswer,
                    BloomLevelId = definition.BloomLevelId,
                    RubricId = rubrics[definition.RubricName].Id,
                    DisplayOrder = ++displayOrder,
                    IsActive = true
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record CriterionDefinition(string Name, int MaxPoints, string Description);
    private sealed record RubricDefinition(string Name, string Description, IReadOnlyList<CriterionDefinition> Criteria);
    private sealed record QuestionDefinition(string Context, string Content, string ExpectedAnswer, int BloomLevelId, string RubricName);
}
