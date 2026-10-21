namespace ComponentIntelligence.Electrical.Drawing;

public sealed record DrawingContinuationLabel(string RelationId, string PageId, string PeerPageId,
    string Text, DrawingPoint Anchor, DrawingPoint PeerAnchor);

public static class DrawingContinuationPresentation
{
    public static IReadOnlyList<DrawingContinuationLabel> Build(DrawingPlanningInput input, DrawingPlanDocument plan,
        IReadOnlyDictionary<string, string> representationLabels)
    {
        if (input.ProjectId != plan.ProjectId ||
            (input.PlanningInputHash != plan.SourcePlanningInputHash &&
             !DrawingPlanningInputHashCompatibility.IsPresentationMetadataOnlyUpgrade(input, plan.SourcePlanningInputHash)))
            return [];

        var pages = plan.Pages.ToDictionary(p => p.PageId, StringComparer.Ordinal);
        var routes = plan.Routes.ToDictionary(r => r.RouteId, StringComparer.Ordinal);
        var representations = input.Representations.ToDictionary(r => r.RepresentationId, StringComparer.Ordinal);
        var connections = input.Connections.ToDictionary(c => c.ConnectionId, StringComparer.Ordinal);
        var result = new List<DrawingContinuationLabel>();
        foreach (var relation in plan.CrossPageRelations.OrderBy(r => r.RelationId, StringComparer.Ordinal))
        {
            if (relation.RelationKind != "ElectricalConnectionContinuation" ||
                relation.SourcePageId is null || relation.DestinationPageId is null ||
                relation.SourceRouteId is null || relation.DestinationRouteId is null ||
                !pages.TryGetValue(relation.SourcePageId, out var sourcePage) ||
                !pages.TryGetValue(relation.DestinationPageId, out var destinationPage) ||
                !routes.TryGetValue(relation.SourceRouteId, out var sourceRoute) ||
                !routes.TryGetValue(relation.DestinationRouteId, out var destinationRoute) ||
                !representations.TryGetValue(relation.SourceRepresentationId, out var sourceRep) ||
                !representations.TryGetValue(relation.DestinationRepresentationId, out var destinationRep) ||
                !connections.TryGetValue(relation.EngineeringId, out var connection) ||
                sourceRoute.Points.Count < 2 || destinationRoute.Points.Count < 2 ||
                sourceRoute.ConnectionId != connection.ConnectionId || destinationRoute.ConnectionId != connection.ConnectionId)
                continue;

            var sourceEndpoint = FindEndpoint(sourceRep, connection);
            var destinationEndpoint = FindEndpoint(destinationRep, connection);
            var sourceAnchor = sourceRoute.Points[^1];
            var destinationAnchor = destinationRoute.Points[0];
            result.Add(new DrawingContinuationLabel(relation.RelationId, sourcePage.PageId, destinationPage.PageId,
                Caption(destinationPage.Order, representationLabels.GetValueOrDefault(destinationRep.RepresentationId) ?? destinationRep.DisplayLabel, destinationEndpoint),
                sourceAnchor, destinationAnchor));
            result.Add(new DrawingContinuationLabel(relation.RelationId, destinationPage.PageId, sourcePage.PageId,
                Caption(sourcePage.Order, representationLabels.GetValueOrDefault(sourceRep.RepresentationId) ?? sourceRep.DisplayLabel, sourceEndpoint),
                destinationAnchor, sourceAnchor));
        }
        return result;
    }

    private static string Caption(int peerOrder, string? peerRepresentation, string? peerEndpoint) =>
        $"未命名訊號 → P.{peerOrder + 1} / {peerRepresentation?.Replace('\n', ' ') ?? "元件未命名"} / {peerEndpoint ?? "接點未確認"}";

    private static string? FindEndpoint(DrawingRepresentationDecision representation, DrawingConnectionPlanningItem connection) =>
        representation.PortBindings.FirstOrDefault(b => b.EngineeringEndpointId == connection.FromEndpointId)?.DisplayLabel ??
        representation.PortBindings.FirstOrDefault(b => b.EngineeringEndpointId == connection.ToEndpointId)?.DisplayLabel;
}
