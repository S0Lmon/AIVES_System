namespace AIVES.DTO;

public sealed record SubjectDto(int Id, string Name, string Description, int TopicCount, int MaterialCount);

public sealed record TopicDto(int Id, int SubjectId, string SubjectName, string Name, string Description, int MaterialCount);

public sealed record MaterialDto(int Id, int TopicId, string TopicName, string SubjectName, string Title,
    string Content, string? SourceFileName, MaterialSourceType SourceType, bool IsActive,
    DateTime CreatedDate, DateTime ModifiedDate, int ContentLength);

public sealed record SubjectInput(string Name, string Description);

public sealed record TopicInput(int SubjectId, string Name, string Description);

public sealed record MaterialInput(int TopicId, string Title, string Content, string? SourceFileName,
    MaterialSourceType SourceType, bool IsActive = true);

public enum MaterialSourceType
{
    Manual = 0,
    ImportedFile = 1
}

/// <summary>A single retrieved passage handed to the model as retrieval context.</summary>
public sealed record MaterialExcerpt(int MaterialId, string Title, string Content);

public enum AiProvider
{
    Gemini = 1,
    Ollama = 2
}