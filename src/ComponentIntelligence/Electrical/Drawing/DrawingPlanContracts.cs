namespace ComponentIntelligence.Electrical.Drawing;

public enum DrawingPlanControlState { Auto, Manual, Locked }
public enum DrawingAlignment { Left, Right, Top, Bottom, HorizontalCenter, VerticalCenter }
public enum DrawingDistribution { Horizontal, Vertical }

public sealed record DrawingPlanDocument
{
    public const string V1 = "electrical-drawing-plan.v1";
    public string SchemaVersion { get; init; } = V1;
    public required string ProjectId { get; init; }
    public string? DrawingPlanHash { get; init; }
    public required string SourcePlanningInputHash { get; init; }
    public required string SourcePagePlanHash { get; init; }
    public IReadOnlyList<DrawingPlanPage> Pages { get; init; } = [];
    public IReadOnlyList<DrawingPlanGroup> Groups { get; init; } = [];
    public IReadOnlyList<DrawingPlacement> Placements { get; init; } = [];
    public IReadOnlyList<DrawingRoute> Routes { get; init; } = [];
    public IReadOnlyList<DrawingCrossPageRelation> CrossPageRelations { get; init; } = [];
    public IReadOnlyList<DrawingPlanIssue> Issues { get; init; } = [];
}

public sealed record DrawingPlanPage
{
    public required string PageId { get; init; }
    public required string Archetype { get; init; }
    public int Order { get; init; }
    public DrawingPlanControlState OrderState { get; init; }
    public required DrawingBounds Bounds { get; init; }
    public IReadOnlyList<string> GroupIds { get; init; } = [];
}

public sealed record DrawingPlanGroup
{
    public required string GroupId { get; init; }
    public required string PageId { get; init; }
    public DrawingPlanControlState State { get; init; }
    public required DrawingBounds Bounds { get; init; }
    public IReadOnlyList<string> RepresentationIds { get; init; } = [];
}

public sealed record DrawingBounds(long X, long Y, long Width, long Height);
public sealed record DrawingPoint(long X, long Y);

public sealed record DrawingPlacement
{
    public required string RepresentationId { get; init; }
    public required string PageId { get; init; }
    public required string GroupId { get; init; }
    public DrawingPlanControlState State { get; init; }
    public long X { get; init; }
    public long Y { get; init; }
    public long Width { get; init; }
    public long Height { get; init; }
    public int RotationDegrees { get; init; }
    public IReadOnlyList<int> AllowedRotations { get; init; } = [0];
}

public sealed record DrawingRoute
{
    public required string RouteId { get; init; }
    public required string ConnectionId { get; init; }
    public required string EndpointAId { get; init; }
    public required string EndpointBId { get; init; }
    public DrawingPlanControlState State { get; init; }
    public IReadOnlyList<DrawingPoint> Points { get; init; } = [];
}

public sealed record DrawingCrossPageRelation
{
    public required string RelationId { get; init; }
    public required string SourceRepresentationId { get; init; }
    public required string DestinationRepresentationId { get; init; }
    public required string RelationKind { get; init; }
    public required string EngineeringId { get; init; }
}

public sealed record DrawingPlanIssue
{
    public required string IssueId { get; init; }
    public DrawingPlanningIssueSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string TargetKind { get; init; }
    public required string TargetId { get; init; }
}

public interface IDrawingPlannerClient
{
    Task<DrawingPlanDocument> GenerateAsync(DrawingPlanningInput input, DrawingPlanDocument? priorPlan, CancellationToken cancellationToken);
}
