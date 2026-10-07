using System.Globalization;
using netDxf;
using netDxf.Entities;

namespace ComponentIntelligence.Electrical.Schematic;

internal static class SchematicDxfHatchClosure
{
    // netDxf 3.0.1 ignores HATCH polyline group 73 while reading. Restore only
    // explicit ASCII DXF flags, by entity handle and boundary path position,
    // before INSERT.Explode transforms the paths. Never infer closure by distance.
    public static void Restore(DxfDocument document, byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false));
        var flags = new Dictionary<string, List<bool?>>(StringComparer.OrdinalIgnoreCase);
        string? handle = null;
        var hatch = false;
        var polyline = false;
        var paths = new List<bool?>();
        void Finish()
        {
            if (hatch && handle is not null && paths.Count > 0)
                flags.Add(handle, paths.ToList());
        }
        while (reader.ReadLine() is { } codeLine && reader.ReadLine() is { } valueLine)
        {
            // Binary DXF is still handled by netDxf, without this ASCII repair.
            if (!int.TryParse(codeLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)) return;
            var value = valueLine.Trim();
            if (code == 0)
            {
                Finish();
                hatch = value == "HATCH";
                handle = null;
                paths.Clear();
                polyline = false;
            }
            else if (hatch)
            {
                if (code == 5) handle = value;
                else if (code == 92)
                {
                    polyline = int.TryParse(value, out var pathType) && (pathType & 2) != 0;
                    paths.Add(null);
                }
                else if (code == 73 && polyline && paths.Count > 0)
                {
                    paths[^1] = value switch { "0" => false, "1" => true, _ => null };
                    polyline = false;
                }
            }
        }
        Finish();
        foreach (var entity in document.Blocks.SelectMany(block => block.Entities).OfType<Hatch>())
        {
            if (!flags.TryGetValue(entity.Handle, out var source) || source.Count != entity.BoundaryPaths.Count) continue;
            for (var i = 0; i < source.Count; i++)
                if (source[i] is { } closed && entity.BoundaryPaths[i].Edges.Count == 1 &&
                    entity.BoundaryPaths[i].Edges[0] is HatchBoundaryPath.Polyline edge)
                {
                    edge.IsClosed = closed;
                    // Polyline.Clone also omits IsClosed in netDxf 3.0.1.
                    // Linear edges survive nested block cloning/transforms.
                    if (edge.Vertexes.All(v => v.Z == 0))
                    {
                        var lines = edge.Explode();
                        entity.BoundaryPaths[i] = new HatchBoundaryPath(lines);
                    }
                }
        }
    }
}
