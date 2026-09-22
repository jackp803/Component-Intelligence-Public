namespace ComponentIntelligence.Electrical.Drawing;

/// <summary>Generated presentation continuations only; never a new physical or approved asset identity.</summary>
public static class DrawingPreviewCatalog
{
    public static IReadOnlyList<DrawingRepresentationDecision> Build(DrawingPlanningInput input)
    {
        var result = input.Representations.ToList();
        foreach (var item in input.HeavyDutyConnectors)
        {
            if (item.RepresentationIds.Count != 1) continue;
            var original = input.Representations.SingleOrDefault(r => r.RepresentationId == item.RepresentationIds[0]);
            if (original is null || !string.IsNullOrEmpty(original.AssetPath)) continue;
            if (item.RowsPerPage <= 0) throw new ArgumentException("Heavy Duty row capacity must be positive.");
            var contacts = item.ContactIds.Order(StringComparer.Ordinal).ToArray();
            if (contacts.Length <= item.RowsPerPage) continue;
            result.Remove(original);
            var chunks = contacts.Chunk(item.RowsPerPage).ToArray();
            for (var index = 0; index < chunks.Length; index++)
                result.Add(original with
                {
                    RepresentationId = index == 0 ? original.RepresentationId : $"{original.RepresentationId}:CONTINUATION:{index + 1:000}",
                    PortBindings = original.PortBindings.Where(b => chunks[index].Contains(b.EngineeringEndpointId, StringComparer.Ordinal)
                        || (index == 0 && !contacts.Contains(b.EngineeringEndpointId, StringComparer.Ordinal))).ToArray()
                });
        }
        return result.OrderBy(r => r.RepresentationId, StringComparer.Ordinal).ToArray();
    }
}
