using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Catalog;
using AIVES.BLL.Services.Import;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace AIVES.Tests;

/// <summary>FR-06: subjects → topics → material, and turning uploaded files into material text.</summary>
[Trait("Requirement", "FR-06")]
public sealed class CatalogTests
{
    private static CatalogService Catalog(out ApplicationDbContext db)
    {
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        return new CatalogService(new SubjectRepository(db), new TopicRepository(db), new MaterialRepository(db),
            Options.Create(new OllamaOptions()));
    }

    private static Task<string> Extract(byte[] bytes, string fileName) => MaterialTextExtractor.ExtractAsync(new MemoryStream(bytes), fileName);

    // TC-06-01: plain text formats are read as UTF-8 (with or without a BOM), Vietnamese included.
    [Theory]
    [InlineData("ghi-chu.txt")]
    [InlineData("bai-giang.md")]
    [InlineData("bang.csv")]
    [InlineData("du-lieu.json")]
    public async Task TextFormatsAreReadAsUtf8(string fileName)
    {
        const string content = "Chuẩn hoá dữ liệu: 1NF, 2NF và 3NF loại bỏ dư thừa.";
        var withBom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray();

        Assert.Equal(content, await Extract(Encoding.UTF8.GetBytes(content), fileName));
        Assert.Equal(content, await Extract(withBom, fileName));
    }

    // TC-06-01: PDF pages keep a "Page n" heading so retrieved passages say where they came from.
    [Fact]
    public async Task PdfPagesKeepTheirPageHeading()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Normalisation removes redundancy.", 12, new PdfPoint(50, 700), font);
        builder.AddPage(PageSize.A4); // a blank page is skipped, but still counts
        builder.AddPage(PageSize.A4).AddText("Indexes speed up lookups.", 12, new PdfPoint(50, 700), font);

        var text = await Extract(builder.Build(), "chapter.pdf");

        Assert.Contains("## Page 1", text);
        Assert.Contains("Normalisation removes redundancy.", text);
        Assert.DoesNotContain("## Page 2", text);
        Assert.Contains("## Page 3", text);
        Assert.True(text.IndexOf("## Page 1", StringComparison.Ordinal) < text.IndexOf("Indexes speed up", StringComparison.Ordinal));
    }

    // TC-06-01: slides keep a "Slide n" heading and their speaker notes; bare slide numbers are dropped.
    [Fact]
    public async Task SlidesKeepTheirSlideHeadingAndSpeakerNotes()
    {
        var text = await Extract(Presentation(
            (["Kiến trúc ba lớp", "Giao diện – Nghiệp vụ – Dữ liệu", "7"], "Nhấn mạnh vai trò của DTO."),
            (["Tiêm phụ thuộc"], null)), "slides.pptx");

        Assert.Contains("## Slide 1", text);
        Assert.Contains("Kiến trúc ba lớp", text);
        Assert.Contains("Notes:\nNhấn mạnh vai trò của DTO.", text.Replace("\r\n", "\n"));
        Assert.Contains("## Slide 2", text);
        Assert.Contains("Tiêm phụ thuộc", text);
        Assert.DoesNotContain("\n7\n", text.Replace("\r\n", "\n"));
    }

    // TC-06-03: only the listed formats are accepted, and damaged files give a readable error.
    [Theory]
    [InlineData("setup.exe")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public async Task UnsupportedFormatsAreRejected(string fileName)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Extract(Encoding.UTF8.GetBytes("MZ some binary content"), fileName));
        Assert.Contains(".pdf", error.Message);
    }

    [Fact]
    public async Task DamagedPdfIsRejectedWithAReadableError()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Extract(Encoding.ASCII.GetBytes("%PDF-1.7 not really a pdf"), "broken.pdf"));
        Assert.Contains("could not be read", error.Message);
    }

    // TC-06-02 (boundary on stored text): a huge text file is cut at MaterialTextExtractor.MaxCharacters.
    [Fact]
    public async Task VeryLongTextIsCutAtTheStoredLimit()
    {
        var text = await Extract(Encoding.UTF8.GetBytes(new string('a', MaterialTextExtractor.MaxCharacters + 5_000)), "long.txt");
        Assert.Equal(MaterialTextExtractor.MaxCharacters, text.Length);
    }

    [Fact]
    public async Task SubjectsTopicsAndMaterialAreCreatedTrimmedAndListedPerParent()
    {
        var catalog = Catalog(out _);
        var subject = await catalog.CreateSubjectAsync(new SubjectInput("  Cơ sở dữ liệu  ", null!));
        var topic = await catalog.CreateTopicAsync(new TopicInput(subject.Id, " Chuẩn hoá ", "  "));
        var other = await catalog.CreateTopicAsync(new TopicInput(subject.Id, "Chỉ mục", ""));
        await catalog.CreateMaterialAsync(new MaterialInput(topic.Id, " Bài giảng 1 ", "  Dạng chuẩn 1NF, 2NF, 3NF.  ", "bai1.txt", MaterialSourceType.ImportedFile));

        Assert.Equal("Cơ sở dữ liệu", subject.Name);
        Assert.Equal(string.Empty, subject.Description);
        Assert.Equal("Chuẩn hoá", topic.Name);
        Assert.Equal(2, (await catalog.GetTopicsAsync(subject.Id)).Count);
        var material = Assert.Single(await catalog.GetMaterialsAsync(topic.Id));
        Assert.Equal("Bài giảng 1", material.Title);
        Assert.Equal("Dạng chuẩn 1NF, 2NF, 3NF.", material.Content);
        Assert.Empty(await catalog.GetMaterialsAsync(other.Id));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("   ")]
    public async Task SubjectAndTopicNamesMustBeAtLeastTwoCharacters(string name)
    {
        var catalog = Catalog(out var db);
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.CreateSubjectAsync(new SubjectInput(name, "")));
        var subject = await catalog.CreateSubjectAsync(new SubjectInput("Mạng máy tính", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.CreateTopicAsync(new TopicInput(subject.Id, name, "")));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.CreateSubjectAsync(new SubjectInput(new string('x', 201), "")));
        Assert.Single(db.Subjects);
        Assert.Empty(db.Topics);
    }

    [Fact]
    public async Task MaterialNeedsATitleAndAtLeastTenCharactersOfContent()
    {
        var catalog = Catalog(out var db);
        var subject = await catalog.CreateSubjectAsync(new SubjectInput("Hệ điều hành", ""));
        var topic = await catalog.CreateTopicAsync(new TopicInput(subject.Id, "Tiến trình", ""));

        await Assert.ThrowsAsync<ArgumentException>(() => catalog.CreateMaterialAsync(new MaterialInput(topic.Id, "X", "Nội dung đủ dài để lưu.", null, MaterialSourceType.Manual)));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.CreateMaterialAsync(new MaterialInput(topic.Id, "Ghi chú", "ngắn", null, MaterialSourceType.Manual)));
        Assert.Empty(db.Materials);
        await catalog.CreateMaterialAsync(new MaterialInput(topic.Id, "Ghi chú", "Đúng mười ký", null, MaterialSourceType.Manual));
        Assert.Single(db.Materials);
    }

    /// <summary>A minimal .pptx: each slide has text lines and optional speaker notes.</summary>
    private static byte[] Presentation(params (string[] Lines, string? Notes)[] slides)
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            var slideIds = new P.SlideIdList();
            uint id = 256;
            foreach (var (lines, notes) in slides)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(TextShape(lines))));
                if (notes is not null)
                {
                    var notesPart = slidePart.AddNewPart<NotesSlidePart>();
                    notesPart.NotesSlide = new P.NotesSlide(new P.CommonSlideData(new P.ShapeTree(TextShape([notes]))));
                }
                slideIds.Append(new P.SlideId { Id = id++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
            }
            presentationPart.Presentation = new P.Presentation(slideIds);
        }
        return stream.ToArray();
    }

    private static P.Shape TextShape(IEnumerable<string> lines) =>
        new(new P.TextBody([new A.BodyProperties(), .. lines.Select(line => new A.Paragraph(new A.Run(new A.Text(line))))]));
}
