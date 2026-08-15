using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Verification;

namespace ComponentIntelligence.Normalization;

public sealed class ComponentNormalizer : IComponentNormalizer
{
    private static readonly Regex VoltageRange = new(
        @"(?<min>\d+(?:[\.,]\d+)?)\s*(?:\.\.\.|…|-)\s*(?<max>\d+(?:[\.,]\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VoltageSingle = new(
        @"(?<value>\d+(?:[\.,]\d+)?)\s*V\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CurrentValue = new(
        @"(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>mA|µA|μA|uA|A)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex PowerValue = new(
        @"(?<value>\d+(?:[\.,]\d+)?)\s*(?<unit>mW|W)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public Task<ComponentIR> NormalizeAsync(
        RawComponentProfile raw,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(raw);

        var voltage = ParseVoltage(Value(raw, "power.operating_voltage"));
        var currentConsumption = ParseCurrentAmp(Value(raw, "power.current_consumption"));
        var maximumCurrent = ParseCurrentAmp(Value(raw, "power.maximum_current"));
        var powerConsumption = ParsePowerWatt(Value(raw, "power.power_consumption"));
        var pinCount = int.TryParse(Value(raw, "connector.pin_count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pins)
            ? pins
            : (int?)null;

        var productPage = raw.Assets.FirstOrDefault(asset => asset.Type == "product-page")?.Url ?? raw.Identity.OfficialProductUrl;
        var datasheet = raw.Assets.FirstOrDefault(asset => asset.Type == "datasheet")?.Url
            ?? raw.Documents
                .Where(document => document.Type.Contains("datasheet", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(document => SourceTrustPolicy.Score(document.SourceType))
                .Select(document => document.Url)
                .FirstOrDefault();

        var specifications = raw.Specifications
            .Where(spec => !string.IsNullOrWhiteSpace(spec.RawName) && !string.IsNullOrWhiteSpace(spec.RawValue))
            .Select(spec => new ComponentSpecification
            {
                Key = spec.ProposedKey,
                Name = spec.RawName,
                Section = spec.Section,
                Value = spec.RawValue,
                Status = spec.Status,
                Evidence = spec.Evidence
            })
            .GroupBy(spec => $"{spec.Section}\u001f{spec.Key}\u001f{spec.Name}\u001f{spec.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with
            {
                Evidence = group.SelectMany(item => item.Evidence).Distinct().ToArray()
            })
            .ToArray();

        var ir = new ComponentIR
        {
            Identity = new ComponentIrIdentity
            {
                ComponentId = CreateComponentId(raw.Identity.OfficialManufacturer, raw.Identity.OfficialModel),
                Manufacturer = raw.Identity.OfficialManufacturer,
                Model = raw.Identity.OfficialModel,
                Mpn = raw.Identity.Mpn
            },
            Classification = new ComponentClassification
            {
                Category = Value(raw, "classification.category"),
                Subcategory = Value(raw, "classification.subcategory")
            },
            Power = new ComponentPower
            {
                OperatingVoltage = voltage,
                CurrentConsumptionAmp = currentConsumption,
                MaximumCurrentAmp = maximumCurrent,
                PowerConsumptionWatt = powerConsumption
            },
            Io = new ComponentIo { OutputType = Value(raw, "io.output_type")?.Trim().ToUpperInvariant() },
            Connector = new ComponentConnector
            {
                Family = Value(raw, "connector.family")?.Trim().ToUpperInvariant(),
                Coding = Value(raw, "connector.coding")?.Trim().ToUpperInvariant(),
                Pins = pinCount
            },
            Ports = raw.Ports,
            Pins = raw.Pins,
            Specifications = specifications,
            Documents = raw.Documents,
            Assets = new ComponentAssets { ProductPageUrl = productPage, DatasheetUrl = datasheet },
            Readiness = new ComponentReadiness()
        };
        return Task.FromResult(ir);
    }

    private static string? Value(RawComponentProfile raw, string key) =>
        SourceTrustPolicy.BestSpecification(raw.Specifications, key)?.RawValue;

    private static NormalizedVoltage? ParseVoltage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var type = value.Contains("DC", StringComparison.OrdinalIgnoreCase)
            ? "DC"
            : value.Contains("AC", StringComparison.OrdinalIgnoreCase) ? "AC" : null;

        var range = VoltageRange.Match(value);
        if (range.Success &&
            TryDecimal(range.Groups["min"].Value, out var min) &&
            TryDecimal(range.Groups["max"].Value, out var max))
        {
            return new NormalizedVoltage { Min = min, Max = max, Unit = "V", Type = type };
        }

        var single = VoltageSingle.Match(value);
        if (single.Success && TryDecimal(single.Groups["value"].Value, out var nominal))
            return new NormalizedVoltage { Min = nominal, Max = nominal, Unit = "V", Type = type };

        return null;
    }

    private static decimal? ParseCurrentAmp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = CurrentValue.Match(value);
        if (!match.Success || !TryDecimal(match.Groups["value"].Value, out var number)) return null;
        var unit = match.Groups["unit"].Value;
        if (unit.Equals("mA", StringComparison.OrdinalIgnoreCase)) return number / 1000m;
        if (unit.Equals("µA", StringComparison.OrdinalIgnoreCase) ||
            unit.Equals("μA", StringComparison.OrdinalIgnoreCase) ||
            unit.Equals("uA", StringComparison.OrdinalIgnoreCase)) return number / 1_000_000m;
        return number;
    }

    private static decimal? ParsePowerWatt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = PowerValue.Match(value);
        if (!match.Success || !TryDecimal(match.Groups["value"].Value, out var number)) return null;
        return match.Groups["unit"].Value.Equals("mW", StringComparison.OrdinalIgnoreCase) ? number / 1000m : number;
    }

    private static bool TryDecimal(string value, out decimal result) =>
        decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static string CreateComponentId(string manufacturer, string model)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{manufacturer.Trim().ToUpperInvariant()}|{model.Trim().ToUpperInvariant()}"));
        return $"CMP-{Convert.ToHexString(bytes.AsSpan(0, 4))}";
    }
}
