using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject SetWireSpecifications(ElectricalProject project, IReadOnlyCollection<string> wireIds,
        int? awg = null, double? areaMm2 = null, bool suggest = false, string? referenceWireId = null)
    {
        if (wireIds.Count == 0) throw new InvalidOperationException("請先選取導線。");
        if ((awg.HasValue ? 1 : 0) + (areaMm2.HasValue ? 1 : 0) + (suggest ? 1 : 0) + (referenceWireId is null ? 0 : 1) != 1)
            throw new InvalidOperationException("請選擇一種線徑設定方式。");
        if (referenceWireId is not null)
        {
            var source = RequireWire(project, referenceWireId);
            var spec = SchematicWirePresentation.Resolve(project, source);
            if (source.SizingProposal is not null || source.AdoptedSizingProposal is not null || spec.Awg is null && spec.AreaMm2 is null)
                throw new InvalidOperationException("來源線徑尚未核對，不能當成已確認規格套用。");
            awg = spec.AwgIsApproximate ? null : spec.Awg; areaMm2 = awg is null ? spec.AreaMm2 : null;
        }
        // Each returned edit is isolated; the caller commits once only after every selected conductor succeeds.
        var next = project;
        foreach (var id in wireIds.Distinct(StringComparer.Ordinal))
        {
            RequireWire(next, id);
            next = suggest ? UseAutomaticWireSize(next, id) : awg is int gauge ? SetWireAwg(next, id, gauge) : SetWireArea(next, id, areaMm2!.Value);
        }
        return next;
    }
}
