using System.Globalization;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicWireSizingPresentation
{
    public static bool CanAccept(ElectricalProject project, SchematicWire wire) =>
        wire.SizingProposal?.AreaMm2 is > 0 and var area && double.IsFinite(area) &&
        SchematicWireLegendPresentation.IsOrdinary(project, wire) && !SchematicWireIdentification.Segments(project.Schematic!, wire).Any(w => w.Locked);

    public static string Summary(ElectricalProject project, SchematicWire wire)
    {
        var spec = SchematicWirePresentation.Resolve(project, wire);
        var state = wire.SizingProposal is not null ? wire.SizingProposal.Basis == "PRODUCT-SENSOR-DRAFT-V1" ? "草稿預設" : "自動建議" :
            wire.AdoptedSizingProposal is not null ? "已採用建議" : wire.ManualSpecification ? "手動設定" : "目前規格";
        return state + "：" + (spec.Awg is int gauge ? (spec.AwgIsApproximate ? "約 AWG " : "AWG ") + gauge : "AWG 未設定") +
            (spec.AreaMm2 is double area ? " / " + area.ToString("0.###", CultureInfo.InvariantCulture) + " mm²" : "");
    }

    public static string Basis(ElectricalProject project, SchematicWire wire)
    {
        var proposal = wire.SizingProposal ?? wire.AdoptedSizingProposal;
        var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == wire.ConnectionId);
        return string.Join("\n\n", new[] {
            proposal is null ? "目前沒有自動建議依據；可使用廠商規格或手動設定。" :
                "依據：" + proposal.Basis + "\n" + (proposal.LoadCurrentAmp is double current ? $"已知負載小計：{current:0.###} A" : "負載未知；此值僅為草稿預設。"),
            "實際線長：" + (connection?.ProvidedLengthMm is double length && connection.LengthSource != CableLengthSource.Unknown ? $"{length:0.###} mm" : "未提供（不採用圖上線長）") +
                "\n允許壓降：" + (connection?.MaxVoltageDropPercent is double limit ? $"{limit:0.###}%" : "未提供"),
            "尚待核對：" + (proposal?.Review ?? "負載、接頭允許截面積、實際線長／壓降、溫度、束線、絕緣及過電流保護。"),
            "採用建議僅表示選用此規格，不代表安全核准。" });
    }
}
