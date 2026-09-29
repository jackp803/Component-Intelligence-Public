using System.Globalization;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicAnchorLabel(SchematicPoint Position, int Rotation, bool OppositeCorner)
{
    public double Top(double measuredWidth, double measuredHeight) =>
        Position.Y - (OppositeCorner ? Rotation == 90 ? measuredWidth : measuredHeight : 0);
}

public static class SchematicSymbolPresentation
{
    public static (int Confirmed, int Total) ContactCoverage(SchematicSymbol symbol, SchematicSymbolOwner owner)
    {
        if (owner.Cable?.ArchivedCable is null)
            return (symbol.Anchors.Count(a => a.Confirmed), symbol.Anchors.Count);
        var pins = owner.Ports.SelectMany(p => p.Pins).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        return (symbol.Anchors.Where(a => a.Confirmed && pins.Contains(a.EndpointId))
            .Select(a => a.EndpointId).Distinct(StringComparer.Ordinal).Count(), pins.Count);
    }

    public static SchematicCadAsset? GeometryForOwner(SchematicSymbol symbol, SchematicSymbolOwner owner)
    {
        var asset = Geometry(symbol, owner.Reference);
        if (asset is null || owner.Cable?.ArchivedCable is not { } archived) return asset;
        var fields = archived.Template.TextBindings.ToDictionary(b => b.AttributeTag, b => b.Field, StringComparer.Ordinal);
        return asset with { Primitives = asset.Primitives.Select(p => p.Kind is "TEXT" or "MTEXT" &&
            p.AttributeTag is not null && fields.TryGetValue(p.AttributeTag, out var field)
                ? p with { Text = CableFieldValue(owner.Cable, field) } : p).ToArray() };
    }

    public static bool ShowReferenceForOwner(SchematicSymbol symbol, SchematicSymbolOwner owner) =>
        !HasCableField(symbol, owner, CableTextField.Reference) && ShowReferenceLabel(symbol);

    public static string CableCaption(SchematicSymbol symbol, SchematicSymbolOwner owner)
    {
        if (owner.Cable is not { } cable) return "";
        return string.Join("\n", new[] { CableTextField.LengthMm, CableTextField.Specification }
            .Where(f => !HasCableField(symbol, owner, f)).Select(f => CableFieldValue(cable, f)));
    }

    private static bool HasCableField(SchematicSymbol symbol, SchematicSymbolOwner owner, CableTextField field) =>
        owner.Cable?.ArchivedCable?.Template.TextBindings.Any(b => b.Field == field &&
            symbol.Geometry?.Primitives.Any(p => p.AttributeTag == b.AttributeTag && p.Kind is "TEXT" or "MTEXT") == true) == true;

    private static string CableFieldValue(CableInstance cable, CableTextField field) => field switch
    {
        CableTextField.Reference => cable.ReferenceDesignator ?? "Reference 待填",
        CableTextField.LengthMm => cable.ProvidedLengthMm is double length ? length.ToString("0.###", CultureInfo.InvariantCulture) + " mm" : "長度待填",
        CableTextField.Specification => cable.Specification ?? "規格待填",
        _ => throw new InvalidOperationException("Unknown cable text field.")
    };

    public static bool ShowReferenceLabel(SchematicSymbol symbol) =>
        symbol.Geometry?.Primitives.Any(p => p.AttributeTag == "TAG1" && p.Kind is "TEXT" or "MTEXT") != true;

    public static SchematicAnchorLabel AnchorLabel(SchematicSymbol symbol, SchematicAnchor anchor)
    {
        var point = SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId);
        var offset = symbol.Rotation switch
        {
            0 => new SchematicPoint(2, -3.8),
            90 => new SchematicPoint(3.8, 2),
            180 => new SchematicPoint(-2, 3.8),
            270 => new SchematicPoint(-3.8, -2),
            _ => throw new InvalidOperationException("Only orthogonal rotations are supported.")
        };
        // Keep the rotated text rectangle, but reverse its attachment instead of reading upside down.
        return new(new(point.X + offset.X, point.Y + offset.Y), symbol.Rotation % 180, symbol.Rotation >= 180);
    }

    public static bool ShowAnchorLabel(SchematicSymbol symbol, SchematicAnchor anchor) =>
        symbol.Geometry is null || !anchor.Confirmed;

    public static SchematicCadAsset? Geometry(SchematicSymbol symbol, string? reference) => symbol.Geometry is { } asset
        ? asset with { Primitives = asset.Primitives.Select(p => p.AttributeTag == "TAG1"
            ? p with { Text = string.IsNullOrWhiteSpace(reference) ? "?" : reference } : p).ToArray() } : null;
}
