namespace ComponentIntelligence.Electrical.Drawing;

public sealed partial class DrawingPlanEditService
{
    // This proposal is never persisted: the shared planner must rebuild routes
    // successfully before the controller commits it as one edit.
    public DrawingPlanDocument PreparePageTransfer(DrawingPlanDocument plan, IReadOnlyList<string> ids, string targetPageId)
    {
        var selected = SelectPlacements(plan, ids);
        var page = plan.Pages.SingleOrDefault(p => p.PageId == targetPageId)
            ?? throw new InvalidOperationException("找不到目標頁面。");
        if (selected.All(p => p.PageId == targetPageId)) return plan;
        var moved = selected.Where(p => p.PageId != targetPageId).Select(p => p.RepresentationId).ToHashSet(StringComparer.Ordinal);
        var endpoints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in moved)
        {
            if (!_endpointBindings.TryGetValue(id, out var bound))
                throw new InvalidOperationException("缺少端點 binding，無法安全搬頁。");
            endpoints.UnionWith(bound);
        }
        var affected = plan.Routes.Where(r => endpoints.Contains(r.EndpointAId) || endpoints.Contains(r.EndpointBId))
            .Select(r => r.ConnectionId).ToHashSet(StringComparer.Ordinal);
        if (plan.Routes.Any(r => affected.Contains(r.ConnectionId) && r.State != DrawingPlanControlState.Auto))
            throw new InvalidOperationException("搬頁會改變手動或鎖定路線的頁面範圍；請先明確將相關路線設為 Auto，或取消搬頁。原圖面未變更。");
        var groupId = plan.Groups.FirstOrDefault(g => g.PageId == targetPageId)?.GroupId ?? $"MANUAL:{targetPageId}";
        var placements = plan.Placements.Select(p => moved.Contains(p.RepresentationId)
            ? p with { PageId=targetPageId, GroupId=groupId, State=DrawingPlanControlState.Manual } : p).ToArray();
        var groups = plan.Groups.ToList();
        if (groups.All(g => g.GroupId != groupId)) groups.Add(new DrawingPlanGroup
        { GroupId=groupId, PageId=targetPageId, State=DrawingPlanControlState.Manual, Bounds=page.Bounds });
        groups = groups.Select(g => g with { RepresentationIds=placements.Where(p => p.GroupId == g.GroupId).Select(p => p.RepresentationId).ToArray() }).ToList();
        return DrawingPlanJson.Rehash(plan with
        {
            Placements=placements, Groups=groups,
            Pages=plan.Pages.Select(p => p with { GroupIds=groups.Where(g => g.PageId == p.PageId).Select(g => g.GroupId).ToArray() }).ToArray(),
            Routes=plan.Routes.Where(r => !affected.Contains(r.ConnectionId)).ToArray(),
            CrossPageRelations=plan.CrossPageRelations.Where(r => !moved.Contains(r.SourceRepresentationId) && !moved.Contains(r.DestinationRepresentationId)).ToArray()
        });
    }
}
