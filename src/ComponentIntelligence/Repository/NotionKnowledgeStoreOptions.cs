namespace ComponentIntelligence.Repository;

/// <summary>
/// Configuration for the user's central Notion electrical-material knowledge base.
/// No secret is committed: the token is read from COMPONENT_INTELLIGENCE_NOTION_TOKEN.
/// Data-source IDs may be overridden when the database is cloned or moved to another workspace.
/// </summary>
public sealed record NotionKnowledgeStoreOptions
{
    public const string ApiVersion = "2026-03-11";

    public string? Token { get; init; }
    public string ComponentsDataSourceId { get; init; } = "968e2dad-0581-49b9-831f-f22fed36e145";
    public string DocumentsDataSourceId { get; init; } = "57030154-c404-4916-9848-f9580f576ac2";
    public string PortsDataSourceId { get; init; } = "f73bc721-1459-4dab-967f-a79d35775d00";
    public string PinsDataSourceId { get; init; } = "95ca405a-0144-4f50-a511-c80a7d4bcc6b";
    public string SpecificationsDataSourceId { get; init; } = "9d4d02cc-372c-4115-84d6-e335d78b7fea";
    public Uri ApiBaseAddress { get; init; } = new("https://api.notion.com/v1/");

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Token) &&
        !string.IsNullOrWhiteSpace(ComponentsDataSourceId);

    public static NotionKnowledgeStoreOptions FromEnvironment() => new()
    {
        Token = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_NOTION_TOKEN"),
        ComponentsDataSourceId = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_NOTION_COMPONENTS_DS")
            ?? "968e2dad-0581-49b9-831f-f22fed36e145",
        DocumentsDataSourceId = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_NOTION_DOCUMENTS_DS")
            ?? "57030154-c404-4916-9848-f9580f576ac2",
        PortsDataSourceId = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_NOTION_PORTS_DS")
            ?? "f73bc721-1459-4dab-967f-a79d35775d00",
        PinsDataSourceId = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_NOTION_PINS_DS")
            ?? "95ca405a-0144-4f50-a511-c80a7d4bcc6b",
        SpecificationsDataSourceId = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_NOTION_SPECIFICATIONS_DS")
            ?? "9d4d02cc-372c-4115-84d6-e335d78b7fea"
    };
}
