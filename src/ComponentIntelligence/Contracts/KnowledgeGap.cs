namespace ComponentIntelligence.Contracts;

public enum KnowledgeGapPriority
{
    Required,
    Recommended
}

/// <summary>
/// A field that is genuinely absent or unusable in central knowledge and should be completed from
/// a human-selected engineering document. The desktop application only reports the gap; it does not
/// search the web, download PDFs, infer a replacement value, or write the value back to Notion.
/// </summary>
public sealed record KnowledgeGap
{
    public required string Key { get; init; }
    public required string ChineseName { get; init; }
    public required string EnglishName { get; init; }
    public required string ChineseReason { get; init; }
    public required string EnglishReason { get; init; }
    public KnowledgeGapPriority Priority { get; init; } = KnowledgeGapPriority.Required;
    public string? PdfHintChinese { get; init; }
    public string? PdfHintEnglish { get; init; }
}
