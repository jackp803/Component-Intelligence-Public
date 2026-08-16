using System.Text.RegularExpressions;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Verification;

/// <summary>
/// Reports fields that are genuinely absent from central Component IR knowledge.
/// This policy is intentionally deterministic: it never searches the web and never invents values.
/// A human can use the resulting checklist to locate an official PDF, then use GPT to update the
/// central Google Drive archive.
/// </summary>
public static class KnowledgeCompletenessPolicy
{
    private static readonly Regex DimensionsWHD = new(
        @"\b\d+(?:[.,]\d+)?\s*[x×X]\s*\d+(?:[.,]\d+)?\s*[x×X]\s*\d+(?:[.,]\d+)?\s*mm\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<KnowledgeGap> Assess(ComponentIR component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var gaps = new List<KnowledgeGap>();

        if (Blank(component.Classification.Category))
            gaps.Add(Gap("classification.category", "元件類別", "Component category",
                "中央庫尚未提供可用的元件類別。", "The central library does not contain a usable component category.",
                "請從原廠 Datasheet 的產品名稱、型式或 General data 找元件類別。",
                "Use the official datasheet product type or General data section."));

        if (component.Power.OperatingVoltage is null)
            gaps.Add(Gap("power.operating_voltage", "工作電壓", "Operating voltage",
                "缺少工作電壓，無法可靠判斷 Power Domain（電源域）。", "Operating voltage is missing, so the power domain cannot be determined reliably.",
                "找 Supply voltage / Operating voltage / Rated voltage。",
                "Find Supply voltage / Operating voltage / Rated voltage."));

        if (component.Ports.Count == 0)
        {
            gaps.Add(Gap("ports", "Port 定義", "Port definitions",
                "沒有任何可用 Port，無法建立電氣拓樸。", "No usable ports are defined, so electrical topology cannot be built.",
                "找 Connection、Wiring、Pin assignment、Connector 或 I/O 章節。",
                "Find Connection, Wiring, Pin assignment, Connector, or I/O sections."));
        }
        else
        {
            AddAggregatePortGap(gaps, component.Ports.Where(port => Blank(port.PortType) && Blank(port.PortRole)), "ports.port_role",
                "Port 角色", "Port role", "部分 Port 缺少 PortRole（例如 Power Input、Digital Output、Communication）。");
            AddAggregatePortGap(gaps, component.Ports.Where(port => Blank(port.SignalType)), "ports.signal_type",
                "訊號類型", "Signal type", "部分 Port 缺少 Signal Type（訊號類型）。");
            AddAggregatePortGap(gaps, component.Ports.Where(port => Blank(port.Direction)), "ports.direction",
                "訊號方向", "Signal direction", "部分 Port 缺少 Input / Output / Bidirectional / Mixed / Passive 方向。");

            var voltagePorts = component.Ports.Where(NeedsVoltageDomain).Where(port => Blank(port.VoltageDomain));
            AddAggregatePortGap(gaps, voltagePorts, "ports.voltage_domain",
                "Port 電壓域", "Port voltage domain", "部分電源或 I/O Port 缺少 Voltage Domain（電壓域）。");

            var communicationPorts = component.Ports.Where(IsCommunicationPort).ToArray();
            if (communicationPorts.Length > 0 && communicationPorts.All(port => Blank(port.Protocol)))
                gaps.Add(Gap("ports.protocol", "通訊協定", "Communication protocol",
                    "已辨識出通訊 Port，但沒有可用 Protocol（通訊協定）。", "Communication ports exist but no usable protocol is recorded.",
                    "找 Interface / Communication / Protocol 章節，例如 RS-485、EtherCAT、Ethernet。",
                    "Find the Interface / Communication / Protocol section, e.g. RS-485, EtherCAT, Ethernet."));
        }

        var hasPhysicalConnector = component.Pins.Count > 0 ||
                                   component.Connector.Pins is > 0 ||
                                   component.Ports.Any(port => !Blank(port.ConnectorFamily));
        if (hasPhysicalConnector && Blank(component.Connector.Family) && component.Ports.All(port => Blank(port.ConnectorFamily)))
            gaps.Add(Gap("connector.family", "接頭系列", "Connector family",
                "已有腳位或實體連接資訊，但缺少 Connector Family（接頭系列）。", "Pin/physical connection data exists but connector family is missing.",
                "找 Connector / Connection 章節，例如 M12、RJ45、terminal block。",
                "Find the Connector / Connection section, e.g. M12, RJ45, terminal block."));

        var rootM12MissingCoding = !Blank(component.Connector.Family) &&
                                   component.Connector.Family!.Contains("M12", StringComparison.OrdinalIgnoreCase) &&
                                   Blank(component.Connector.Coding);
        var m12PortsMissingCoding = component.Ports.Where(port =>
            !Blank(port.ConnectorFamily) &&
            port.ConnectorFamily!.Contains("M12", StringComparison.OrdinalIgnoreCase) &&
            Blank(port.ConnectorCoding)).ToArray();
        if (rootM12MissingCoding || m12PortsMissingCoding.Length > 0)
        {
            var affected = m12PortsMissingCoding.Select(port => port.PortId).ToArray();
            gaps.Add(Gap("connector.coding", "M12 Coding", "M12 coding",
                affected.Length == 0
                    ? "M12 接頭缺少 Coding（例如 A-code / D-code）。"
                    : $"M12 Port 缺少 Coding：{string.Join(", ", affected)}。",
                affected.Length == 0
                    ? "An M12 connector is present but its coding is missing."
                    : $"M12 coding is missing for port(s): {string.Join(", ", affected)}.",
                "找 Connector designation / Coding / Connection drawing。",
                "Find Connector designation / Coding / Connection drawing."));
        }

        AddPerPortPinCoverageGaps(gaps, component);

        var connectorFamilies = component.Ports.Select(port => port.ConnectorFamily)
            .Append(component.Connector.Family)
            .Where(value => !Blank(value))
            .Select(value => value!)
            .ToArray();
        var expectedRootPins = component.Connector.Pins ?? 0;
        var anyExpectedPortPins = component.Ports.Any(port => port.PinCount is > 0);
        if ((expectedRootPins > 0 || anyExpectedPortPins || connectorFamilies.Length > 0) && component.Pins.Count == 0)
            gaps.Add(Gap("pins", "Pin assignment（腳位定義）", "Pin assignment",
                "存在實體接頭但沒有可用 Pin assignment。", "A physical connector exists but no usable pin assignment is available.",
                "找 Pin assignment / Wiring diagram / Connection diagram。",
                "Find Pin assignment / Wiring diagram / Connection diagram."));
        else if (component.Pins.Count > 0)
        {
            var functionMissing = component.Pins
                .Where(pin => !PinMayOmitFunction(pin))
                .Where(pin => Blank(pin.Function) && Blank(pin.PinRole))
                .Select(pin => $"{pin.PortId ?? "?"}:{pin.PinNumber}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (functionMissing.Length > 0)
                gaps.Add(Gap("pins.function", "Pin Function（腳位功能）", "Pin function",
                    $"Pin {string.Join(", ", functionMissing)} 缺少可信的功能定義。",
                    $"Pin {string.Join(", ", functionMissing)} is missing a credible function assignment.",
                    "從原廠 Pin assignment 表逐腳確認；若實體 Pin 存在但標準接法未使用，明確標成 Unused，不可直接刪除。",
                    "Verify each pin from the official pin-assignment table. If a physical pin exists but is unused in the standard connection, mark it Unused instead of deleting it."));

            var ownerMissing = component.Pins.Where(pin => Blank(pin.PortId)).Select(pin => pin.PinNumber).Distinct().ToArray();
            if (component.Ports.Count > 1 && ownerMissing.Length > 0)
                gaps.Add(Gap("pins.port_id", "Pin 所屬 Port", "Pin parent port",
                    $"Pin {string.Join(", ", ownerMissing)} 尚未對應到實際 Port。",
                    $"Pin {string.Join(", ", ownerMissing)} is not assigned to a physical/logical port.",
                    "用原廠 connector/pin 圖確認每個 Pin 屬於哪個 Port。",
                    "Use the official connector/pin drawing to assign each pin to its port."));
        }

        var hasTypedOutput = component.Ports.Any(NeedsOutputType);
        if (hasTypedOutput && Blank(component.Io.OutputType))
            gaps.Add(Gap("io.output_type", "輸出型態", "Output type",
                "已有需要電氣輸出型態的 Output Port，但缺少 PNP / NPN / relay / push-pull 等資訊。",
                "An output port that requires an electrical output type exists, but PNP/NPN/relay/push-pull information is missing.",
                "找 Output function / Switching output / Electrical output。",
                "Find Output function / Switching output / Electrical output."));

        if (!HasUsableDimensions(component))
            gaps.Add(Gap("dimensions", "外形尺寸 W × H × D", "Dimensions W × H × D",
                "缺少可直接用於 Physical Layout（實體配置）的三軸 mm 尺寸。",
                "A usable three-axis millimetre dimension is missing for physical layout.",
                "找 Dimensions / Mechanical data / Dimension drawing，記錄 W × H × D mm。",
                "Find Dimensions / Mechanical data / Dimension drawing and record W × H × D mm.",
                KnowledgeGapPriority.Recommended));

        if (component.Assets.DatasheetUrl is null && !component.Documents.Any(document =>
                document.Type.Contains("datasheet", StringComparison.OrdinalIgnoreCase)))
            gaps.Add(Gap("assets.datasheet", "原廠 Datasheet 來源", "Official datasheet source",
                "中央庫尚未保存可追溯的 Datasheet 來源。", "The central library does not contain a traceable datasheet source.",
                "人工找到原廠 PDF 後，把 URL / PDF 與缺欄位清單交給 GPT 更新 Google Drive 歸檔。",
                "After a human finds the official PDF, give the URL/PDF and missing-field checklist to GPT for the Google Drive archive workflow.",
                KnowledgeGapPriority.Recommended));

        if (component.Assets.ImageUrl is null)
            gaps.Add(Gap("assets.image", "元件圖片", "Component image",
                "中央庫尚未提供元件圖片。", "The central library does not contain a component image.",
                "可從原廠產品頁或 Datasheet 取得並存入 Documents/Manufacturer/Model；這不影響接線，但有助於 UI 辨識。",
                "Use an official product page or datasheet image and store it under Documents/Manufacturer/Model. This is not required for wiring but helps UI recognition.",
                KnowledgeGapPriority.Recommended));

        return gaps;
    }

    public static IReadOnlyList<KnowledgeGap> ForMissingCentralRecord() =>
    [
        Gap("central.component", "中央元件資料", "Central component record",
            "中央庫沒有這個料號，軟體不會自行上網搜尋。", "The component is absent from central knowledge; the application will not search the web.",
            "請人工找原廠 PDF，交給 GPT 依 Components / Ports / Pins 規格建立或更新中央歸檔。",
            "Find the official PDF manually and give it to GPT to create/update the Components / Ports / Pins central archive.")
    ];

    [Obsolete("Use ForMissingCentralRecord. Notion is no longer the desktop central archive.")]
    public static IReadOnlyList<KnowledgeGap> ForMissingNotionRecord() => ForMissingCentralRecord();

    private static void AddPerPortPinCoverageGaps(ICollection<KnowledgeGap> gaps, ComponentIR component)
    {
        foreach (var port in component.Ports.Where(port => port.PinCount is > 0))
        {
            var expected = port.PinCount!.Value;
            var actual = component.Pins.Count(pin => string.Equals(pin.PortId, port.PortId, StringComparison.OrdinalIgnoreCase));
            if (actual == expected) continue;

            gaps.Add(Gap(
                $"pins.coverage.{port.PortId}",
                $"{port.PortId} Pin 完整度",
                $"{port.PortId} pin completeness",
                $"Port {port.PortId} 宣告 {expected} Pin，但中央庫目前只有 {actual} 筆 Pin。實體存在的 Pin 即使 NC / Unused / Unknown 也不能省略。",
                $"Port {port.PortId} declares {expected} pins but the central library contains {actual}. Physical pins must remain present even when NC, Unused, or Unknown.",
                "確認 Connector PinCount，並建立 1..N 全部實體 Pin。",
                "Confirm the connector pin count and create every physical pin 1..N."));
        }
    }

    private static bool PinMayOmitFunction(ComponentPin pin)
    {
        var status = pin.PinStatus?.Trim();
        return status is not null &&
               (status.Equals("NC", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Unused", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Reserved", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("NotApplicable", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddAggregatePortGap(
        ICollection<KnowledgeGap> gaps,
        IEnumerable<ComponentPort> source,
        string key,
        string chineseName,
        string englishName,
        string chineseReason)
    {
        var ids = source.Select(port => port.PortId).Where(value => !Blank(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0) return;
        gaps.Add(Gap(key, chineseName, englishName,
            $"{chineseReason} 缺少：{string.Join(", ", ids)}。",
            $"Missing for port(s): {string.Join(", ", ids)}.",
            "請查看原廠 Connection / Wiring / Interface 表格。",
            "Check the official Connection / Wiring / Interface table."));
    }

    private static bool NeedsVoltageDomain(ComponentPort port)
    {
        var text = $"{port.PortType} {port.PortRole} {port.SignalType}";
        return ContainsAny(text, "power", "supply", "digital", "analog", "di", "do", "ai", "ao");
    }

    private static bool IsCommunicationPort(ComponentPort port)
    {
        var text = $"{port.PortType} {port.PortRole} {port.SignalType} {port.Protocol}";
        return ContainsAny(text, "communication", "comm", "ethernet", "ethercat", "rs-485", "rs485", "serial", "can", "profinet", "modbus", "io-link", "iolink");
    }

    private static bool NeedsOutputType(ComponentPort port)
    {
        if (!string.Equals(port.Direction, "Output", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(port.Direction, "Out", StringComparison.OrdinalIgnoreCase)) return false;
        var text = $"{port.PortType} {port.PortRole} {port.SignalType}";
        return ContainsAny(text, "digital", "do", "switch", "transistor", "relay", "analog", "ao", "pnp", "npn");
    }

    private static bool HasUsableDimensions(ComponentIR component) =>
        component.Specifications.Any(specification =>
            specification.Status is VerificationStatus.Verified or VerificationStatus.UserConfirmed or VerificationStatus.SingleSource &&
            (string.Equals(specification.Key, "dimensions", StringComparison.OrdinalIgnoreCase) ||
             specification.Name.Contains("dimension", StringComparison.OrdinalIgnoreCase) ||
             specification.Name.Contains("尺寸", StringComparison.OrdinalIgnoreCase)) &&
            !Blank(specification.Value) &&
            DimensionsWHD.IsMatch(specification.Value!));

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static KnowledgeGap Gap(
        string key,
        string chineseName,
        string englishName,
        string chineseReason,
        string englishReason,
        string pdfHintChinese,
        string pdfHintEnglish,
        KnowledgeGapPriority priority = KnowledgeGapPriority.Required) =>
        new()
        {
            Key = key,
            ChineseName = chineseName,
            EnglishName = englishName,
            ChineseReason = chineseReason,
            EnglishReason = englishReason,
            PdfHintChinese = pdfHintChinese,
            PdfHintEnglish = pdfHintEnglish,
            Priority = priority
        };
}
