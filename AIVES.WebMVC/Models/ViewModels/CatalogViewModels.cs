namespace AIVES.WebMVC.Models.ViewModels;

public static class CatalogTabs
{
    public const string Subject = "subject";
    public const string Material = "material";

    public static string Normalize(string? tab) =>
        string.Equals(tab, Material, StringComparison.OrdinalIgnoreCase) ? Material : Subject;
}

public sealed class CatalogViewModel
{
    public string ActiveTab { get; set; } = CatalogTabs.Subject;

    public int SubjectCount { get; set; }
    public int TopicCount { get; set; }
    public int MaterialCount { get; set; }

    public string SubjectFilter { get; set; } = string.Empty;
    public string MaterialFilter { get; set; } = string.Empty;
    public int? FilterSubjectId { get; set; }
    public int? FilterTopicId { get; set; }

    public IReadOnlyList<SubjectNode> Subjects { get; set; } = [];
    public IReadOnlyList<MaterialRow> Materials { get; set; } = [];
    public IReadOnlyList<SubjectOption> SubjectOptions { get; set; } = [];
    public IReadOnlyList<TopicOption> TopicOptions { get; set; } = [];
}

public sealed record SubjectOption(int Id, string Name);

public sealed record TopicOption(int Id, int SubjectId, string Name);

public sealed class SubjectNode
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MaterialCount { get; set; }
    public IReadOnlyList<TopicNode> Topics { get; set; } = [];
}

public sealed class TopicNode
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MaterialCount { get; set; }
}

public sealed class MaterialRow
{
    public int Id { get; set; }
    public int TopicId { get; set; }
    public int SubjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public string TopicName { get; set; } = string.Empty;
    public string? SourceFileName { get; set; }
    public bool IsImported { get; set; }
    public bool IsActive { get; set; }
    public int ContentLength { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
}

public sealed class MaterialEditViewModel
{
    public int Id { get; set; }
    public int TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? SourceFileName { get; set; }
    public bool IsImportedFile { get; set; }
    public bool IsActive { get; set; } = true;
}