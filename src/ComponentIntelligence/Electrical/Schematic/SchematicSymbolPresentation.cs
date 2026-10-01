using System.Globalization;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public enum SchematicLabelSide { Left, Right, Top, Bottom }

public sealed record SchematicAnchorLabel(SchematicPoint Position, SchematicLabelSide Side)
{
    public int TextRotation => Side switch
    {
        SchematicLabelSide.Top => -90,
        SchematicLabelSide.Bottom => 90,
        _ => 0
    };

    public (double Left, double Top, double Right, double Bottom) Bounds(double width, double height) => Side switch
    {
        SchematicLabelSide.Left => (Position.X - width, Position.Y - height / 2, Position.X, Position.Y + height / 2),
        SchematicLabelSide.Right => (Position.X, Position.Y - height / 2, Position.X + width, Position.Y + height / 2),
        SchematicLabelSide.Top => (Position.X - height - 1, Position.Y - width, Position.X - 1, Position.Y),
        _ => (Position.X - height - 1, Position.Y, Position.X - 1, Position.Y + width)
    };
}

public static class SchematicSymbolPresentation
{
    public static string DisplayTitle(SchematicSymbol symbol, SchematicSymbolOwner owner) =>
        symbol.SectionCount > 1 ? $"{owner.DisplayName}  {symbol.SectionIndex}/{symbol.SectionCount}" : owner.DisplayName;

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

    public static SchematicCadAsset BodyGeometry(SchematicCadAsset asset) => asset with
    {
        Primitives = asset.Primitives.Where(p => p.Kind is not ("TEXT" or "MTEXT")).ToArray()
    };

    public static SchematicCadAsset UprightCadText(SchematicSymbol symbol, SchematicCadAsset asset) => asset with
    {
        Primitives = asset.Primitives.Where(p => p.Kind is "TEXT" or "MTEXT")
            .Select(p => p with { Start = WorldPoint(symbol, p.Start), Rotation = 0 }).ToArray()
    };

    public static SchematicPoint WorldPoint(SchematicSymbol symbol, SchematicPoint point)
    {
        var local = symbol.Rotation switch
        {
            0 => point,
            90 => new SchematicPoint(symbol.Height - point.Y, point.X),
            180 => new SchematicPoint(symbol.Width - point.X, symbol.Height - point.Y),
            270 => new SchematicPoint(point.Y, symbol.Width - point.X),
            _ => throw new InvalidOperationException("Only orthogonal rotations are supported.")
        };
        return new(symbol.Position.X + local.X, symbol.Position.Y + local.Y);
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

    public static string CableFieldValue(CableInstance cable, CableTextField field) => field switch
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
        var side = anchor.Direction switch
        {
            "Left" => SchematicLabelSide.Left,
            "Top" => SchematicLabelSide.Top,
            "Bottom" => SchematicLabelSide.Bottom,
            _ => SchematicLabelSide.Right
        };
        for (var i = 0; i < symbol.Rotation; i += 90) side = side switch
        {
            SchematicLabelSide.Left => SchematicLabelSide.Top,
            SchematicLabelSide.Top => SchematicLabelSide.Right,
            SchematicLabelSide.Right => SchematicLabelSide.Bottom,
            _ => SchematicLabelSide.Left
        };
        var offset = side switch
        {
            SchematicLabelSide.Left => new SchematicPoint(-2, 0),
            SchematicLabelSide.Right => new SchematicPoint(2, 0),
            SchematicLabelSide.Top => new SchematicPoint(0, -2),
            _ => new SchematicPoint(0, 2)
        };
        return new(new(point.X + offset.X, point.Y + offset.Y), side);
    }

    public static bool ShowAnchorLabel(SchematicSymbol symbol, SchematicAnchor anchor) =>
        symbol.Geometry is null || !anchor.Confirmed;

    public static SchematicCadAsset? Geometry(SchematicSymbol symbol, string? reference) => symbol.Geometry is { } asset
        ? asset with { Primitives = asset.Primitives.Select(p => p.AttributeTag == "TAG1"
            ? p with { Text = string.IsNullOrWhiteSpace(reference) ? "?" : reference } : p).ToArray() } : null;
}
