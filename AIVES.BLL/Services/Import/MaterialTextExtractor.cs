using System.Text;
using AIVES.DTO.Localization;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace AIVES.BLL.Services.Import;

/// <summary>
/// Turns uploaded course material (textbook chapters, lecture slides) into plain text that the
/// question generator can retrieve from. Slides keep a "Slide n" heading and their speaker notes,
/// PDF pages a "Page n" heading, so retrieved passages still say where they came from.
/// </summary>
public static class MaterialTextExtractor
{
    public static readonly IReadOnlyList<string> Extensions = [".txt", ".md", ".markdown", ".csv", ".json", ".pdf", ".docx", ".pptx"];

    /// <summary>Upper bound on stored text, well above a typical course book chapter.</summary>
    public const int MaxCharacters = 400_000;

    public static async Task<string> ExtractAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!Extensions.Contains(extension))
            throw new ArgumentException(L10n.T("Supported formats: .txt, .md, .csv, .json, .pdf, .docx, .pptx"));

        // The Office and PDF readers need a seekable stream.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        string text;
        try
        {
            text = extension switch
            {
                ".pdf" => Pdf(buffer),
                ".docx" => Docx(buffer),
                ".pptx" => Pptx(buffer),
                _ => await new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true).ReadToEndAsync(cancellationToken)
            };
        }
        catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
        {
            throw new ArgumentException(L10n.T("The file could not be read. Check that it is not damaged or password protected."), ex);
        }

        text = Clean(text);
        return text.Length > MaxCharacters ? text[..MaxCharacters] : text;
    }

    private static string Pdf(Stream stream)
    {
        using var document = PdfDocument.Open(stream);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var pageText = ContentOrderTextExtractor.GetText(page);
            if (string.IsNullOrWhiteSpace(pageText))
                continue;
            builder.AppendLine($"## Page {page.Number}");
            builder.AppendLine(pageText.Trim());
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string Docx(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
            return string.Empty;
        var builder = new StringBuilder();
        foreach (var paragraph in body.Descendants<Word.Paragraph>())
        {
            var line = paragraph.InnerText.Trim();
            if (line.Length == 0)
                continue;
            var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
            builder.AppendLine(style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ? "## " + line : line);
        }
        return builder.ToString();
    }

    private static string Pptx(Stream stream)
    {
        using var document = PresentationDocument.Open(stream, false);
        var presentation = document.PresentationPart;
        var slideIds = presentation?.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ToList() ?? [];
        var builder = new StringBuilder();
        var number = 0;
        foreach (var slideId in slideIds)
        {
            number++;
            if (slideId.RelationshipId?.Value is not { } relationship || presentation!.GetPartById(relationship) is not SlidePart slide)
                continue;
            var lines = Paragraphs(slide.Slide).ToList();
            var notes = slide.NotesSlidePart?.NotesSlide is { } notesSlide ? Paragraphs(notesSlide).ToList() : [];
            if (lines.Count == 0 && notes.Count == 0)
                continue;
            builder.AppendLine($"## Slide {number}");
            foreach (var line in lines)
                builder.AppendLine(line);
            if (notes.Count > 0)
            {
                builder.AppendLine("Notes:");
                foreach (var line in notes)
                    builder.AppendLine(line);
            }
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static IEnumerable<string> Paragraphs(DocumentFormat.OpenXml.OpenXmlElement? root) =>
        root is null
            ? []
            : root.Descendants<Drawing.Paragraph>()
                .Select(paragraph => string.Concat(paragraph.Descendants<Drawing.Text>().Select(text => text.Text)).Trim())
                // Slide numbers and footers come through as bare digits.
                .Where(line => line.Length > 1 && !line.All(char.IsDigit));

    private static string Clean(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(line => line.TrimEnd());
        var builder = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            blank = line.Length == 0 ? blank + 1 : 0;
            if (blank <= 1)
                builder.AppendLine(line);
        }
        return builder.ToString().Trim();
    }
}
