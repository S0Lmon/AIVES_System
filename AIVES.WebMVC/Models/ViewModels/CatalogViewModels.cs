using AIVES.DTO.Localization;

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

    /// <summary>
    /// The subject whose own page is open. Creating a subject redirects here so the lecturer lands
    /// on the new subject with topic creation already in front of them.
    /// </summary>
    public int? OpenSubjectId { get; set; }
    public SubjectNode? OpenSubject { get; set; }

    public IReadOnlyList<SubjectNode> Subjects { get; set; } = [];
    public IReadOnlyList<MaterialRow> Materials { get; set; } = [];
    public IReadOnlyList<SubjectOption> SubjectOptions { get; set; } = [];
    public IReadOnlyList<TopicOption> TopicOptions { get; set; } = [];
}

public sealed record SubjectOption(int Id, string Name);

public sealed record TopicOption(int Id, int SubjectId, string Name);

/// <summary>One choice in a custom dropdown. <paramref name="GroupId"/> carries the parent id so
/// a topic list can be filtered by the chosen subject.</summary>
public sealed record PickerOption(int Value, string Text, int? GroupId = null);

/// <summary>
/// Describes a custom dropdown. The control renders a real <c>select</c> and the script upgrades
/// its looks, so the form still posts a plain value when JavaScript is unavailable.
/// </summary>
public sealed class CatalogPickerModel
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Unique element id. Several forms on one page can post the same field name.</summary>
    public string? Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string EmptyLabel { get; set; } = string.Empty;
    public int? SelectedValue { get; set; }
    public bool Required { get; set; }
    public bool Grouped { get; set; }
    public IReadOnlyList<PickerOption> Options { get; set; } = [];
    /// <summary>Group headings, keyed by the <see cref="PickerOption.GroupId"/> they contain.</summary>
    public IReadOnlyList<PickerOption> Groups { get; set; } = [];
    /// <summary>Name of the control this one depends on. Null means it stands alone.</summary>
    public string? FilteredBy { get; set; }
}

/// <summary>Builds the subject and topic dropdowns so every screen picks them the same way.</summary>
public static class CatalogPickers
{
    public static CatalogPickerModel Subject(IReadOnlyList<SubjectOption> subjects, string name = "SubjectId",
        string? label = null, string? emptyLabel = null, int? selected = null, bool required = false,
        string? id = null) => new()
        {
            Name = name,
            Id = id,
            Label = label ?? L10n.T("Subject"),
            EmptyLabel = emptyLabel ?? L10n.T("All subjects"),
            SelectedValue = selected,
            Required = required,
            Options = subjects.Select(subject => new PickerOption(subject.Id, subject.Name)).ToList()
        };

    public static CatalogPickerModel Topic(IReadOnlyList<TopicOption> topics, string name = "TopicId",
        string? label = null, string? emptyLabel = null, int? selected = null, bool required = false,
        string? filteredBy = "SubjectId", string? id = null,
        IReadOnlyList<SubjectOption>? subjects = null) => new()
        {
            Name = name,
            Id = id,
            Label = label ?? L10n.T("Topic"),
            EmptyLabel = emptyLabel ?? L10n.T("All topics"),
            SelectedValue = selected,
            Required = required,
            Grouped = true,
            FilteredBy = filteredBy,
            Groups = (subjects ?? Array.Empty<SubjectOption>())
                .Select(subject => new PickerOption(subject.Id, subject.Name)).ToList(),
            Options = topics.Select(topic => new PickerOption(topic.Id, topic.Name, topic.SubjectId)).ToList()
        };
}

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