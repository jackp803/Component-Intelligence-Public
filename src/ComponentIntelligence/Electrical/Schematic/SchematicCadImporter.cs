using System.Security.Cryptography;
using netDxf;
using netDxf.Entities;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicCadPrimitive
{
    public required string Kind { get; init; }
    public required SchematicPoint Start { get; init; }
    public SchematicPoint? End { get; init; }
    public double Radius { get; init; }
    public double StartAngle { get; init; }
    public double EndAngle { get; init; }
    public string? Text { get; init; }
    public double TextHeight { get; init; }
    public double Rotation { get; init; }
    public double TextWidth { get; init; }
    public string? TextAttachment { get; init; }
}

public sealed record SchematicCadContact(string Tag, string Value, SchematicPoint Position, string? SourcePinId = null);

public sealed record SchematicCadAsset
{
    public required string SourceSha256 { get; init; }
    public double MillimetresPerUnit { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public IReadOnlyList<SchematicCadPrimitive> Primitives { get; init; } = [];
    public IReadOnlyList<SchematicCadContact> ConnectionPoints { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public bool Complete => Diagnostics.Count == 0;
}

// Geometry-only import. Attribute tags never become engineering pin authority.
public sealed class SchematicCadImporter
{
    public SchematicCadAsset ReadDxf(string path, double millimetresPerUnit)
    {
        if (!double.IsFinite(millimetresPerUnit) || millimetresPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(millimetresPerUnit));
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        var doc = DxfDocument.Load(stream) ?? throw new InvalidDataException("DXF could not be read.");
        var raw = new List<SchematicCadPrimitive>(); var contacts = new List<SchematicCadContact>(); var diagnostics = new List<string>();
        var bounds = new List<SchematicPoint>();
        SchematicPoint Point(Vector3 p)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.Z) > 0.000001)
                throw new InvalidDataException("Only finite planar XY geometry is supported.");
            var point = new SchematicPoint(p.X, p.Y); bounds.Add(point); return point;
        }
        void Read(EntityObject e, int depth)
        {
            if (depth > 32) throw new InvalidDataException("Block nesting exceeds 32 levels.");
            if (!e.IsVisible || !e.Layer.IsVisible) return;
            if (e.Normal != Vector3.UnitZ) { diagnostics.Add($"Unsupported normal: {e.Type}"); return; }
            switch (e)
            {
                case Insert insert:
                    foreach (var attribute in insert.Attributes)
                    {
                        var p = Point(attribute.Position);
                        if (attribute.Tag.StartsWith("X", StringComparison.Ordinal) && attribute.Tag.Contains("TERM", StringComparison.Ordinal))
                            contacts.Add(new(attribute.Tag, attribute.Value ?? "", p));
                    }
                    foreach (var child in insert.Explode()) Read(child, depth + 1);
                    break;
                case Polyline2D polyline:
                    foreach (var child in polyline.Explode()) Read(child, depth + 1);
                    break;
                case Line line:
                    raw.Add(new() { Kind = "LINE", Start = Point(line.StartPoint), End = Point(line.EndPoint) });
                    break;
                case Circle circle:
                    raw.Add(new() { Kind = "CIRCLE", Start = Point(circle.Center), Radius = circle.Radius });
                    bounds.Add(new(circle.Center.X - circle.Radius, circle.Center.Y - circle.Radius));
                    bounds.Add(new(circle.Center.X + circle.Radius, circle.Center.Y + circle.Radius));
                    break;
                case Arc arc:
                    raw.Add(new() { Kind = "ARC", Start = Point(arc.Center), Radius = arc.Radius, StartAngle = arc.StartAngle, EndAngle = arc.EndAngle });
                    // Conservative circle bounds preserve a stable scale, including curved extrema.
                    bounds.Add(new(arc.Center.X - arc.Radius, arc.Center.Y - arc.Radius));
                    bounds.Add(new(arc.Center.X + arc.Radius, arc.Center.Y + arc.Radius));
                    break;
                case Text text:
                    raw.Add(new() { Kind = "TEXT", Start = Point(text.Position), Text = text.Value, TextHeight = text.Height, Rotation = text.Rotation });
                    if (text.Alignment != TextAlignment.BaselineLeft || text.IsBackward || text.IsUpsideDown || text.ObliqueAngle != 0 || text.WidthFactor != 1)
                        diagnostics.Add("Text formatting requires visual review.");
                    break;
                case MText text:
                    raw.Add(new() { Kind = "MTEXT", Start = Point(text.Position), Text = text.PlainText(),
                        TextHeight = text.Height, TextWidth = text.RectangleWidth,
                        TextAttachment = text.AttachmentPoint.ToString(), Rotation = text.Rotation });
                    diagnostics.Add("MText formatting and font metrics require visual review; plain text retained.");
                    break;
                default:
                    diagnostics.Add($"Unsupported entity: {e.Type}");
                    break;
            }
        }
        foreach (var entity in doc.Entities.All) Read(entity, 0);
        if (raw.Count == 0 || bounds.Count == 0) throw new InvalidDataException("No supported visible geometry in model space.");
        var minX = bounds.Min(p => p.X); var maxX = bounds.Max(p => p.X);
        var minY = bounds.Min(p => p.Y); var maxY = bounds.Max(p => p.Y);
        SchematicPoint Map(SchematicPoint p) => new((p.X - minX) * millimetresPerUnit, (maxY - p.Y) * millimetresPerUnit);
        return new()
        {
            SourceSha256 = Convert.ToHexString(SHA256.HashData(bytes)), MillimetresPerUnit = millimetresPerUnit,
            Width = Math.Max(0.1, (maxX - minX) * millimetresPerUnit), Height = Math.Max(0.1, (maxY - minY) * millimetresPerUnit),
            Primitives = raw.Select(p => p with { Start = Map(p.Start), End = p.End is null ? null : Map(p.End),
                Radius = p.Radius * millimetresPerUnit, TextHeight = p.TextHeight * millimetresPerUnit,
                TextWidth = p.TextWidth * millimetresPerUnit, Rotation = -p.Rotation }).ToArray(),
            ConnectionPoints = contacts.Select(c => c with { Position = Map(c.Position) }).ToArray(),
            Diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray()
        };
    }
}
