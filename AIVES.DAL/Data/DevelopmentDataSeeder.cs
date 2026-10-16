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

        // ---- Seed 2 subjects, each with 3 topics, each topic with 12 questions ----
        var subjectDefinitions = new[]
        {
            (Name: "C# & .NET", Description: "C# language, .NET runtime, ASP.NET Core, and related technologies."),
            (Name: "Data Engineering", Description: "Databases, SQL, data modeling, ETL, and data platform fundamentals.")
        };

        var subjectEntities = new List<Subject>();
        foreach (var subjectDef in subjectDefinitions)
        {
            var subject = await dbContext.Subjects.SingleOrDefaultAsync(item => item.Name == subjectDef.Name, cancellationToken)
                ?? new Subject { Name = subjectDef.Name, Description = subjectDef.Description };
            subject.Description = subjectDef.Description;
            subject.ModifiedDate = DateTime.UtcNow;
            if (subject.Id == 0)
            {
                dbContext.Subjects.Add(subject);
            }
            subjectEntities.Add(subject);
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        var topicDefinitions = new Dictionary<string, (string Name, string Description)[]>
        {
            ["C# & .NET"] = new[]
            {
                ("Language fundamentals", "Types, operators, control flow, and core C# syntax."),
                ("Async programming", "Task, async/await, ValueTask, concurrency patterns."),
                ("Dependency injection", "Service lifetimes, registration, and resolving dependencies.")
            },
            ["Data Engineering"] = new[]
            {
                ("SQL fundamentals", "SELECT, joins, aggregation, and basic DDL/DML."),
                ("Database design", "Normalization, indexes, constraints, and schema design."),
                ("Data pipelines", "ETL/ELT, batch and streaming data movement.")
            }
        };

        var topicEntities = new List<Topic>();
        foreach (var subject in subjectEntities)
        {
            foreach (var (topicName, topicDesc) in topicDefinitions[subject.Name])
            {
                var topic = await dbContext.Topics.SingleOrDefaultAsync(item => item.Name == topicName && item.SubjectId == subject.Id, cancellationToken)
                    ?? new Topic { SubjectId = subject.Id, Name = topicName, Description = topicDesc };
                topic.Description = topicDesc;
                topic.ModifiedDate = DateTime.UtcNow;
                if (topic.Id == 0)
                {
                    dbContext.Topics.Add(topic);
                }
                topicEntities.Add(topic);
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        // Use existing bloom levels (1..6 seeded by migrations) and rubrics from above.
        var bloomLevelIds = new[] { 1, 2, 3, 4, 5, 6 };
        var rubricIds = rubrics.Values.Select(r => r.Id).ToList();
        var questionSeed = new Dictionary<string, (string Content, string ExpectedAnswer, int BloomLevelId, string RubricName)[]>
        {
            ["Language fundamentals"] = new[]
            {
                ("What is the difference between a struct and a class in C#?", "Structs are value types stored on the stack; classes are reference types on the heap. Structs are copied on assignment; classes copy the reference.", 1, "[DEMO] Kiến thức nền tảng"),
                ("Explain value types vs reference types.", "Value types hold data directly; reference types hold a reference to data. Default assignment copies value types but copies references for reference types.", 1, "[DEMO] Kiến thức nền tảng"),
                ("What does the `var` keyword do?", "It instructs the compiler to infer the type from the initialization expression; it is still strongly typed at compile time.", 1, "[DEMO] Kiến thức nền tảng"),
                ("What is a nullable reference type?", "A reference type annotated with `?` that may be null; the compiler warns about possible null dereferences.", 2, "[DEMO] Kiến thức nền tảng"),
                ("Explain extension methods.", "Static methods in static classes that appear as instance methods on the type they extend via `this` parameter.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What are records in C#?", "Immutable reference types with value-based equality, concise syntax, and `with` expressions for nondestructive mutation.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is pattern matching?", "Language feature to test whether a value has a certain shape and extract data from it, e.g. `is`, `switch` expressions.", 3, "[DEMO] Vận dụng và phân tích"),
                ("Explain `async`/`await`.", "Builds a state machine and yields the thread while awaiting I/O, improving scalability without blocking.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `Task.WhenAll`?", "Starts multiple tasks and returns a task that completes when all supplied tasks complete.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is the `using` statement?", "Ensures `IDisposable.Dispose` is called even if an exception occurs, via try/finally.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What are delegates?", "Type-safe function pointers that can reference methods and be passed as parameters.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is LINQ?", "Language Integrated Query for querying collections with a uniform syntax across in-memory and remote sources.", 3, "[DEMO] Vận dụng và phân tích")
            },
            ["Async programming"] = new[]
            {
                ("What problem does async/await solve?", "It avoids blocking threads during I/O waits, improving throughput and responsiveness.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is deadlock risk with `.Result`?", "Blocking on an async method can deadlock in synchronization contexts that require the same thread.", 3, "[DEMO] Vận dụng và phân tích"),
                ("Explain `ConfigureAwait(false)`.", "Tells the awaiter not to resume on the original context, avoiding deadlocks in library code.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `ValueTask`?", "A lighter-weight alternative to `Task` for synchronous-completing async methods, reducing allocations.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `CancellationToken`?", "A mechanism to cooperatively cancel long-running operations.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is `Task.Run`?", "Queues work to the thread pool for CPU-bound work; not for I/O-bound async.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is `IAsyncEnumerable`?", "Enables async streaming of results, awaiting each element as it arrives.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `Channel<T>`?", "A producer-consumer queue for async message passing between components.", 4, "[DEMO] Vận dụng và phân tích"),
                ("Explain `AsyncLocal`.", "Flows ambient data across async flows within a logical call context.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `TaskCompletionSource`?", "Lets you manually control the completion of a `Task`, useful for wrapping event-based APIs.", 4, "[DEMO] Thiết kế hệ thống"),
                ("What is `Task.Delay`?", "Asynchronously waits for a time interval without blocking a thread.", 1, "[DEMO] Kiến thức nền tảng"),
                ("What is `await foreach`?", "Consumes `IAsyncAsyncEnumerable` elements as they become available asynchronously.", 3, "[DEMO] Vận dụng và phân tích")
            },
            ["Dependency injection"] = new[]
            {
                ("What is dependency injection?", "A technique where a class receives its dependencies from an external source rather than creating them.", 1, "[DEMO] Kiến thức nền tảng"),
                ("What are the three service lifetimes?", "Transient (new per resolve), Scoped (new per request/scope), Singleton (one instance app-wide).", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is captive dependency?", "A longer-lived service holding a shorter-lived service, e.g. Singleton holding Scoped, causing stale/shared state.", 3, "[DEMO] Vận dụng và phân tích"),
                ("How does `AddScoped` work in ASP.NET Core?", "One instance per HTTP request scope; disposed at end of request.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is `IServiceProvider`?", "The root service locator used to resolve registered services.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is `IServiceScope`?", "Creates a scope for resolving Scoped services and disposes them when disposed.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `IServiceScopeFactory`?", "Factory used to create `IServiceScope` instances, often injected into background services.", 3, "[DEMO] Vận dụng và phân tích"),
                ("Can you inject Scoped into Singleton?", "Not directly without risks; use `IServiceScopeFactory` to create a scope inside the singleton.", 4, "[DEMO] Thiết kế hệ thống"),
                ("What is `KeyedService`?", "Registers multiple implementations of the same service distinguished by a key.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `IAsyncDisposable`?", "Disposes resources asynchronously, used with `await using`.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is `ActivatorUtilities`?", "Creates types with constructor injection manually, combining explicit and resolved parameters.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `IHttpContextAccessor`?", "Provides access to the current `HttpContext` outside of controllers, e.g. in services.", 2, "[DEMO] Kiến thức nền tảng")
            },
            ["SQL fundamentals"] = new[]
            {
                ("What is a primary key?", "A column or set of columns that uniquely identifies each row in a table.", 1, "[DEMO] Kiến thức nền tảng"),
                ("What is `INNER JOIN`?", "Returns rows that have matching values in both tables.", 1, "[DEMO] Kiến thức nền tảng"),
                ("What is `LEFT JOIN`?", "Returns all rows from the left table and matched rows from the right; nulls when no match.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is `GROUP BY`?", "Groups rows by one or more columns so aggregate functions can be applied per group.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is `HAVING`?", "Filters groups after `GROUP BY`, unlike `WHERE` which filters rows before grouping.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is a subquery?", "A query nested inside another query, used in `SELECT`, `FROM`, or `WHERE` clauses.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is a CTE?", "Common Table Expression: a named temporary result set referenced within a single statement.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `UNION` vs `UNION ALL`?", "`UNION` removes duplicates; `UNION ALL` keeps all rows and is faster.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is an index?", "A data structure that speeds up row lookup at the cost of storage and write performance.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `ACID`?", "Atomicity, Consistency, Isolation, Durability — guarantees for reliable transactions.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is a stored procedure?", "A precompiled set of SQL statements stored in the database and callable by name.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is a view?", "A saved query that acts like a virtual table, often used to simplify access or restrict columns.", 2, "[DEMO] Kiến thức nền tảng")
            },
            ["Database design"] = new[]
            {
                ("What is normalization?", "Organizing tables to reduce redundancy and improve data integrity by splitting into related tables.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is 1NF?", "Each table cell holds a single value, and there are no repeating groups.", 1, "[DEMO] Kiến thức nền tảng"),
                ("What is 2NF?", "1NF plus all non-key attributes depend on the whole primary key, not part of it.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is 3NF?", "2NF plus no transitive dependencies among non-key attributes.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is a foreign key?", "A column that references the primary key of another table, enforcing referential integrity.", 1, "[DEMO] Kiến thức nền tảng"),
                ("What is a composite key?", "A primary key made of two or more columns.", 1, "[DEMO] Kiến thức nền tảng"),
                ("What is a clustered index?", "Determines physical storage order of rows; only one per table.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is a nonclustered index?", "A separate structure pointing to data rows; multiple allowed per table.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is a covering index?", "An index containing all columns needed by a query, avoiding table lookups.", 4, "[DEMO] Thiết kế hệ thống"),
                ("What is denormalization?", "Intentionally adding redundancy to improve read performance, accepting some inconsistency risk.", 4, "[DEMO] Thiết kế hệ thống"),
                ("What is a many-to-many relationship?", "Resolved via a junction/associative table with foreign keys to both sides.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is `ON DELETE CASCADE`?", "Automatically deletes child rows when the referenced parent row is deleted.", 2, "[DEMO] Kiến thức nền tảng")
            },
            ["Data pipelines"] = new[]
            {
                ("What is ETL?", "Extract, Transform, Load: pull data, transform it, then load into a target system.", 1, "[DEMO] Kiến thức nền tảng"),
                ("What is ELT?", "Extract, Load, Transform: load raw data first, then transform inside the target system.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is batch processing?", "Processing data in large chunks at scheduled intervals.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is streaming?", "Processing data record-by-record as it arrives, with low latency.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is exactly-once processing?", "Guarantee that each record affects the output exactly once, even with retries.", 4, "[DEMO] Thiết kế hệ thống"),
                ("What is a data lake?", "A repository storing raw data in its native format until needed.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is a data warehouse?", "A system optimized for analytical queries over large historical datasets.", 2, "[DEMO] Kiến thức nền tảng"),
                ("What is CDC?", "Change Data Capture: tracking and propagating changes from source systems.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is data lineage?", "Tracking the origin, movement, and transformation of data through systems.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is idempotency in pipelines?", "Reprocessing the same input yields the same output without side effects.", 4, "[DEMO] Thiết kế hệ thống"),
                ("What is a dead letter queue?", "A queue holding records that could not be processed after retries, for later inspection.", 3, "[DEMO] Vận dụng và phân tích"),
                ("What is schema evolution?", "Handling changes to data structure over time without breaking downstream consumers.", 4, "[DEMO] Thiết kế hệ thống")
            }
        };

        var questionOrder = await dbContext.Questions.MaxAsync(q => (int?)q.DisplayOrder, cancellationToken) ?? 0;
        foreach (var topic in topicEntities)
        {
            var topicQuestions = questionSeed[topic.Name];
            foreach (var (content, expected, bloomId, rubricName) in topicQuestions)
            {
                dbContext.Questions.Add(new Question
                {
                    Content = content,
                    Context = $"[SEED] {topic.Subject.Name} / {topic.Name}",
                    ExpectedAnswer = expected,
                    BloomLevelId = bloomId,
                    RubricId = rubrics[rubricName].Id,
                    TopicId = topic.Id,
                    SubjectId = topic.SubjectId,
                    DisplayOrder = ++questionOrder,
                    IsActive = true,
                    ModifiedDate = DateTime.UtcNow
                });
            }
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
