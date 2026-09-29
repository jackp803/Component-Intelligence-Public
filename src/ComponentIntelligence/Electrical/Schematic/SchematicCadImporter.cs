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
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DisplayText
    {
        get
        {
            if (Text is null || Kind != "TEXT") return Text;
            // Decode once, retaining source text and unsupported formatting verbatim.
            var display = new System.Text.StringBuilder();
            for (var i = 0; i < Text.Length; i++)
            {
                if (i + 2 < Text.Length && Text[i] == '%' && Text[i + 1] == '%')
                {
                    var symbol = char.ToLowerInvariant(Text[i + 2]) switch
                    {
                        'p' => '\u00b1', 'd' => '\u00b0', 'c' => '\u2300', '%' => '%', _ => '\0'
                    };
                    if (symbol != '\0') { display.Append(symbol); i += 2; continue; }
                }
                display.Append(Text[i]);
            }
            return display.ToString();
        }
    }
    public string? AttributeTag { get; init; }
    public double TextHeight { get; init; }
    public double Rotation { get; init; }
    public double TextWidth { get; init; }
    public double TextWidthFactor { get; init; } = 1;
    public string? TextAttachment { get; init; }
    public IReadOnlyList<IReadOnlyList<SchematicPoint>> Contours { get; init; } = [];

    public SchematicPoint TextAnchorOffset(double width, double height, double baseline)
    {
        var alignment = TextAttachment ?? "BaselineLeft";
        var x = alignment.EndsWith("Right", StringComparison.Ordinal) ? -width :
            alignment.EndsWith("Center", StringComparison.Ordinal) || alignment == "Middle" ? -width / 2 : 0;
        var y = alignment.StartsWith("Top", StringComparison.Ordinal) ? 0 :
            alignment.StartsWith("Middle", StringComparison.Ordinal) ? -height / 2 :
            alignment.StartsWith("Bottom", StringComparison.Ordinal) ? -height : -baseline;
        return new(x, y);
    }

    // TEXT stores a baseline anchor; rotate its nominal top-left around that anchor.
    public SchematicPoint PreviewTextOrigin()
    {
        if (Kind != "TEXT") return Start;
        var angle = Rotation * Math.PI / 180;
        return new(Start.X + TextHeight * Math.Sin(angle), Start.Y - TextHeight * Math.Cos(angle));
    }
}

public sealed record SchematicCadContact(string Tag, string Value, SchematicPoint Position, string? SourcePinId = null, string? Direction = null);

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
        SchematicDxfHatchClosure.Restore(doc, bytes);
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
                case Hatch hatch when hatch.Pattern.Name == "SOLID" && hatch.Pattern.Style.ToString() == "Normal":
                    var loops = new List<IReadOnlyList<SchematicPoint>>();
                    foreach (var path in hatch.BoundaryPaths)
                    {
                        if (path.Edges.Count == 1 && path.Edges[0] is HatchBoundaryPath.Polyline edge &&
                            edge.IsClosed && edge.Vertexes.Length >= 3 && edge.Vertexes.All(v => v.Z == 0))
                        {
                            loops.Add(edge.Vertexes.Select(v => Point(new Vector3(v.X, v.Y, hatch.Elevation))).ToArray());
                            continue;
                        }
                        var lines = path.Edges.OfType<HatchBoundaryPath.Line>().ToArray();
                        if (lines.Length != path.Edges.Count || lines.Length < 3 ||
                            lines.Where((line, i) => (line.End - lines[(i + 1) % lines.Length].Start).Modulus() > 0.000001).Any())
                        { loops.Clear(); break; }
                        loops.Add(lines.Select(line => Point(new Vector3(line.Start.X, line.Start.Y, hatch.Elevation))).ToArray());
                    }
                    if (loops.Count == 0) diagnostics.Add("Unsupported Hatch boundary; no partial fill imported.");
                    else raw.Add(new() { Kind = "SOLID_HATCH", Start = loops[0][0], Contours = loops });
                    break;
                case Insert insert:
                    foreach (var attribute in insert.Attributes)
                    {
                        if (attribute.Tag.StartsWith("X", StringComparison.Ordinal) && attribute.Tag.Contains("TERM", StringComparison.Ordinal))
                            contacts.Add(new(attribute.Tag, attribute.Value ?? "", Point(attribute.Position)));
                        // Explode discards attribute identity. Preserve tagged text before flattening.
                        if (attribute.IsVisible && attribute.Layer.IsVisible && !attribute.Flags.HasFlag(AttributeFlags.Hidden) && !string.IsNullOrEmpty(attribute.Value))
                        {
                            raw.Add(new() { Kind = "TEXT", Start = Point(attribute.Position), Text = attribute.Value,
                                AttributeTag = attribute.Tag, TextHeight = attribute.Height, Rotation = attribute.Rotation,
                                TextAttachment = attribute.Alignment.ToString(), TextWidthFactor = attribute.WidthFactor });
                            if (attribute.Alignment != TextAlignment.BaselineLeft || attribute.WidthFactor != 1 || attribute.ObliqueAngle != 0)
                                diagnostics.Add("Attribute text formatting requires visual review.");
                        }
                        attribute.IsVisible = false;
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
                    raw.Add(new() { Kind = "TEXT", Start = Point(text.Position), Text = text.Value, TextHeight = text.Height, Rotation = text.Rotation,
                        TextAttachment = text.Alignment.ToString(), TextWidthFactor = text.WidthFactor });
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
        // Standalone ACADE symbols store contacts as model-space ATTDEFs, not INSERT attributes.
        foreach (var definition in doc.Blocks[netDxf.Blocks.Block.DefaultModelSpaceName].AttributeDefinitions.Values)
        {
            if (definition.Tag.StartsWith("X", StringComparison.Ordinal) && definition.Tag.Contains("TERM", StringComparison.Ordinal))
                contacts.Add(new(definition.Tag, definition.Value ?? "", Point(definition.Position), Direction: ContactDirection(definition.Tag)));
            if (!definition.IsVisible || !definition.Layer.IsVisible || definition.Flags.HasFlag(AttributeFlags.Hidden) || string.IsNullOrEmpty(definition.Value)) continue;
            raw.Add(new() { Kind = "TEXT", Start = Point(definition.Position), Text = definition.Value, AttributeTag = definition.Tag,
                TextHeight = definition.Height, Rotation = definition.Rotation,
                TextAttachment = definition.Alignment.ToString(), TextWidthFactor = definition.WidthFactor });
            if (definition.Alignment != TextAlignment.BaselineLeft || definition.WidthFactor != 1 || definition.ObliqueAngle != 0)
                diagnostics.Add("Attribute text formatting requires visual review.");
        }
        if (raw.Count == 0 || bounds.Count == 0) throw new InvalidDataException("No supported visible geometry in model space.");
        var minX = bounds.Min(p => p.X); var maxX = bounds.Max(p => p.X);
        var minY = bounds.Min(p => p.Y); var maxY = bounds.Max(p => p.Y);
        SchematicPoint Map(SchematicPoint p) => new((p.X - minX) * millimetresPerUnit, (maxY - p.Y) * millimetresPerUnit);
        return new()
        {
            SourceSha256 = Convert.ToHexString(SHA256.HashData(bytes)), MillimetresPerUnit = millimetresPerUnit,
            Width = Math.Max(0.1, (maxX - minX) * millimetresPerUnit), Height = Math.Max(0.1, (maxY - minY) * millimetresPerUnit),
            Primitives = raw.Select(p => p with { Start = Map(p.Start), End = p.End is null ? null : Map(p.End),
                Contours = p.Contours.Select(loop => (IReadOnlyList<SchematicPoint>)loop.Select(Map).ToArray()).ToArray(),
                Radius = p.Radius * millimetresPerUnit, TextHeight = p.TextHeight * millimetresPerUnit,
                TextWidth = p.TextWidth * millimetresPerUnit, Rotation = -p.Rotation }).ToArray(),
            ConnectionPoints = contacts.Select(c => c with { Position = Map(c.Position) }).ToArray(),
            Diagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    // ACADE connection-attribute orientation, not a catalog endpoint identity rule.
    private static string? ContactDirection(string tag) => tag.Length > 2 && tag[2..].StartsWith("TERM", StringComparison.Ordinal)
        ? tag[..2] switch { "X1" => "Right", "X2" => "Top", "X4" => "Left", "X8" => "Bottom", _ => null } : null;
}
