using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Validation;

public sealed record PreExportReviewItem
{
    public required string EndpointId { get; init; }
    public required string ComponentId { get; init; }
    public required string ComponentLabel { get; init; }
    public required string PortId { get; init; }
    public required string PinNumber { get; init; }
    public bool IsRequired { get; init; }
    public ResponsibilityScope ResponsibilityScope { get; init; }
    public EndpointDisposition Disposition { get; init; }
    public string? Reason { get; init; }
    public bool IsResolved { get; init; }
}

public sealed class PreExportReviewService
{
    public IReadOnlyList<PreExportReviewItem> BuildReview(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var connected = project.Connections
            .SelectMany(connection => new[] { connection.FromEndpointId, connection.ToEndpointId })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviews = project.EndpointReviews
            .GroupBy(review => review.EndpointId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var items = new List<PreExportReviewItem>();

        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        foreach (var pin in port.Pins)
        {
            if (pin.Status is PinStatus.Nc or PinStatus.Reserved) continue;
            if (connected.Contains(pin.PinId)) continue;

            reviews.TryGetValue(pin.PinId, out var review);
            var disposition = review?.Disposition ?? DefaultDisposition(component.ResponsibilityScope, pin);
            items.Add(new PreExportReviewItem
            {
                EndpointId = pin.PinId,
                ComponentId = component.ComponentInstanceId,
                ComponentLabel = component.ReferenceDesignator ?? component.EquipmentTag ?? component.ComponentInstanceId,
                PortId = port.PortId,
                PinNumber = pin.PinNumber,
                IsRequired = pin.IsRequired,
                ResponsibilityScope = component.ResponsibilityScope,
                Disposition = disposition,
                Reason = review?.Reason,
                IsResolved = IsResolved(disposition)
            });
        }

        return items
            .OrderByDescending(item => item.IsRequired)
            .ThenBy(item => item.ComponentLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.PortId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.PinNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ApplyDisposition(
        ElectricalProject project,
        string endpointId,
        EndpointDisposition disposition,
        string? reason,
        string? confirmedBy,
        DateTimeOffset? confirmedAt = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        if (disposition == EndpointDisposition.ReturnToEdit)
            disposition = EndpointDisposition.None;

        var review = project.EndpointReviews.LastOrDefault(item => string.Equals(item.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase));
        if (review is null)
        {
            review = new UnconnectedEndpointReview { EndpointId = endpointId };
            project.EndpointReviews.Add(review);
        }

        review.Disposition = disposition;
        review.Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        review.ConfirmedBy = string.IsNullOrWhiteSpace(confirmedBy) ? null : confirmedBy.Trim();
        review.ConfirmedAt = disposition == EndpointDisposition.None ? null : confirmedAt ?? DateTimeOffset.UtcNow;
    }

    public bool CanProceedToDrawing(ElectricalProject project, ValidationReport validation)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(validation);
        if (validation.DrawingReadiness == DrawingReadiness.Blocked) return false;
        return BuildReview(project).Where(item => item.IsRequired).All(item => item.IsResolved || item.Disposition == EndpointDisposition.Tbd);
    }

    private static EndpointDisposition DefaultDisposition(ResponsibilityScope scope, ComponentPin pin)
    {
        if (scope == ResponsibilityScope.OutOfScope) return EndpointDisposition.OutOfScope;
        if (scope == ResponsibilityScope.NotRequired || pin.Status == PinStatus.Optional) return EndpointDisposition.NotApplicable;
        return EndpointDisposition.None;
    }

    private static bool IsResolved(EndpointDisposition disposition) => disposition is
        EndpointDisposition.IntentionallyUnused or EndpointDisposition.OutOfScope or EndpointDisposition.NotApplicable or EndpointDisposition.Tbd;
}
