using System.Text;
using ComponentIntelligence.Electrical.Cables;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    internal string BuildConnectionEngineeringSummary(string connectionId)
    {
        var analysis = new ConnectionEngineeringAnalyzer().Analyze(_project, connectionId);
        var text = new StringBuilder();
        text.AppendLine("Engineering Analysis｜線路工程分析");
        text.AppendLine($"Layer｜電氣層: {analysis.Layer}");
        text.AppendLine($"Protocol｜協定: {analysis.Protocol ?? "Unknown / 未知"}");
        text.AppendLine($"A: {analysis.FromEndpoint ?? "Unknown"}");
        text.AppendLine($"B: {analysis.ToEndpoint ?? "Unknown"}");
        text.AppendLine($"Connector A｜A端接頭: {analysis.ConnectorA ?? "Unknown / 未知"}");
        text.AppendLine($"Connector B｜B端接頭: {analysis.ConnectorB ?? "Unknown / 未知"}");
        text.AppendLine($"Voltage｜電壓: {FormatVoltage(analysis)}");
        text.AppendLine($"Required Current｜需求電流: {FormatAmp(analysis.RequiredCurrentAmp)}");
        text.AppendLine($"Source Capacity｜來源最大能力: {FormatAmp(analysis.SourceCapacityAmp)}");
        text.AppendLine($"Power｜功率: {FormatPower(analysis)}");
        text.AppendLine($"Required Cores｜需要芯數: {(analysis.RequiredCoreCount?.ToString() ?? "Unknown / 未知")}");
        text.AppendLine($"Selected Area｜目前線徑: {FormatArea(analysis.SelectedConductorAreaMm2)}");
        text.AppendLine($"Termination Range｜端接允許範圍: {FormatAreaRange(analysis.TerminationMinAreaMm2, analysis.TerminationMaxAreaMm2)}");
        text.AppendLine($"Provided Cable Length｜外部提供線長: {(analysis.ProvidedLengthMm is double length ? $"{length / 1000d:0.###} m" : "Unknown / 未知")}");
        text.AppendLine($"Length Source｜線長來源: {analysis.LengthSource}");
        text.AppendLine("Layout / Cable Route｜佈局／路徑: 僅供空間與圖面參考，不作為工程線長來源");
        text.AppendLine($"Twisted Pair｜雙絞: {FormatRequirement(analysis.TwistedPair)}");
        text.AppendLine($"Shielding｜屏蔽: {FormatRequirement(analysis.Shielding)}");
        text.AppendLine($"Drag Chain｜拖鏈: {FormatRequirement(analysis.DragChain)}");

        if (analysis.Warnings.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Warnings｜警告");
            foreach (var warning in analysis.Warnings) text.AppendLine($"⚠ {warning}");
        }

        if (analysis.MissingData.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Needs Data｜還需要補的資料");
            foreach (var item in analysis.MissingData) text.AppendLine($"• {item}");
        }
        else
        {
            text.AppendLine();
            text.AppendLine("✓ 目前沒有分析器可辨識的必要缺口。仍需通過 Cable Planning / Validation（線材規劃／驗證）。");
        }

        return text.ToString().TrimEnd();
    }

    private static string FormatVoltage(ConnectionEngineeringAnalysis analysis)
    {
        if (analysis.NominalVoltage is double nominal) return $"{nominal:0.###} V {analysis.VoltageType}";
        if (analysis.MinVoltage is double min && analysis.MaxVoltage is double max) return $"{min:0.###}...{max:0.###} V {analysis.VoltageType}";
        if (analysis.MinVoltage is double onlyMin) return $">= {onlyMin:0.###} V {analysis.VoltageType}";
        if (analysis.MaxVoltage is double onlyMax) return $"<= {onlyMax:0.###} V {analysis.VoltageType}";
        return "Unknown / 未知";
    }

    private static string FormatPower(ConnectionEngineeringAnalysis analysis)
    {
        if (analysis.PowerWatt is double watt) return $"{watt:0.###} W";
        if (analysis.MinPowerWatt is double min && analysis.MaxPowerWatt is double max) return $"{min:0.###}...{max:0.###} W";
        return "Unknown / 未知（缺電壓或負載電流）";
    }

    private static string FormatAmp(double? value) => value is double amp ? $"{amp:0.###} A" : "Unknown / 未知";
    private static string FormatArea(double? value) => value is double area ? $"{area:0.###} mm²" : "Unknown / 未知";

    private static string FormatAreaRange(double? min, double? max) => (min, max) switch
    {
        (double minimum, double maximum) => $"{minimum:0.###}...{maximum:0.###} mm²",
        (double minimum, null) => $">= {minimum:0.###} mm²",
        (null, double maximum) => $"<= {maximum:0.###} mm²",
        _ => "Unknown / 未知"
    };

    private static string FormatRequirement(RequirementLevel level) => level switch
    {
        RequirementLevel.Required => "Required / 必須",
        RequirementLevel.Preferred => "Preferred / 建議",
        RequirementLevel.Optional => "Optional / 可選",
        _ => "Unknown / 未知"
    };
}
