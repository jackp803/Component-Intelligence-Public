using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using Microsoft.Data.Sqlite;

internal static partial class Program
{
    private static void RunTextRegression()
    {
        var renderer = typeof(SchematicWorkspaceControl).GetMethod("CadCanvas", BindingFlags.Static | BindingFlags.NonPublic)!;
        var cases = 0;
        foreach (var kind in new[] { "TEXT", "MTEXT" })
        foreach (var pixels in new[] { 8.999999977500001e-6, .45, 9d })
        foreach (var rotation in new[] { 0d, 90d, 180d, 270d })
        foreach (var attachment in new[] { "TopLeft", "MiddleCenter", "BottomRight" })
        {
            var primitive = new SchematicCadPrimitive { Kind = kind, Start = new(40, 20), Text = "TINY CAD TEXT ABCDEFGHIJKLMNOP",
                TextHeight = pixels / 3, TextWidth = pixels * 8 / 3, Rotation = rotation,
                TextWidthFactor = 1.2, TextAttachment = attachment };
            var asset = new SchematicCadAsset { SourceSha256 = new('a', 64), Width = 100, Height = 50, Primitives = [primitive] };
            var before = JsonSerializer.Serialize(asset);
            var canvas = (Canvas)renderer.Invoke(null, [asset, null, 1d, null, true])!;
            canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
            var text = canvas.Children.OfType<TextBlock>().Single();
            Require(TextBlock.FontSizeProperty.IsValidValue(text.FontSize), "Invalid WPF FontSize assigned.");
            var matrix = text.RenderTransform.Value;
            var effectiveY = text.FontSize * Math.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22);
            var effectiveX = text.FontSize * Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
            Require(Math.Abs(effectiveY - pixels) <= pixels * 1e-8, "Small text was enlarged rather than rendered at its native size.");
            Require(Math.Abs(effectiveX - pixels * (kind == "TEXT" ? 1.2 : 1)) <= pixels * 1e-8, "CAD text width/rotation was scaled incorrectly.");
            if (kind == "MTEXT")
                Require(Math.Abs(text.Width / text.FontSize - 8) < 1e-8, "MTEXT wrapping width changed relative to the font size.");
            var offset = kind == "TEXT" ? primitive.TextAnchorOffset(text.DesiredSize.Width, text.DesiredSize.Height, text.BaselineOffset) :
                new SchematicPoint(attachment.EndsWith("Right") ? -text.DesiredSize.Width : attachment.EndsWith("Center") ? -text.DesiredSize.Width / 2 : 0,
                    attachment.StartsWith("Bottom") ? -text.DesiredSize.Height : attachment.StartsWith("Middle") ? -text.DesiredSize.Height / 2 : 0);
            var widthFactor = kind == "TEXT" ? 1.2 : 1;
            var displayScale = pixels / text.FontSize;
            var radians = rotation * Math.PI / 180;
            var x = offset.X * widthFactor * displayScale; var y = offset.Y * displayScale;
            var expected = new Point(x * Math.Cos(radians) - y * Math.Sin(radians), x * Math.Sin(radians) + y * Math.Cos(radians));
            var actual = text.RenderTransform.Transform(new Point());
            Require((actual - expected).Length < 1e-7, "Text alignment offset was not scaled before rotating.");
            Require(Canvas.GetLeft(text) == 120 && Canvas.GetTop(text) == 60, "Text anchor moved.");
            Require(before == JsonSerializer.Serialize(asset), "Renderer changed source CAD evidence.");
            cases++;
        }
        Console.WriteLine($"PASS: {cases} TEXT/MTEXT font-limit, exact-size, wrapping, rotation, alignment and native-evidence cases.");
    }

    private static void RunReadOnlyRegression(string path)
    {
        SQLitePCL.Batteries_V2.Init();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path,
            Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT SnapshotJson FROM ElectricalProjects";
        using var reader = command.ExecuteReader();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var projects = 0; var pages = 0; var cadFrames = 0; var fittedFrames = 0;
        while (reader.Read())
        {
            var project = JsonSerializer.Deserialize<ElectricalProject>(reader.GetString(0), options)!;
            var before = JsonSerializer.Serialize(project);
            var control = new SchematicWorkspaceControl(() => project, (_, _) => throw new InvalidOperationException("Read-only regression cannot commit."),
                () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
            control.RefreshWorkspace();
            foreach (var symbol in project.Schematic?.Symbols.Where(s => s.Geometry is not null) ?? [])
            {
                Set(control, "_pageId", symbol.PageId); Set(control, "_selectionId", symbol.SymbolId);
                control.RefreshWorkspace();
                var sheet = (Canvas)GetSheet(control); Layout(sheet);
                var frame = SelectionFrame(sheet);
                var rotated = symbol.Rotation % 180 != 0;
                if (frame.Width < (rotated ? symbol.Height : symbol.Width) - 2 ||
                    frame.Height < (rotated ? symbol.Width : symbol.Height) - 2) fittedFrames++;
                cadFrames++;
            }
            Require(before == JsonSerializer.Serialize(project), "Offscreen project loading changed project data.");
            projects++; pages += project.Schematic?.Pages.Count ?? 0;
        }
        Console.WriteLine($"PASS: {projects} saved project(s), {pages} page(s) loaded offscreen via read-only SQLite; no source mutation or native window.");
        Console.WriteLine($"PASS: {cadFrames} saved CAD selection frame(s), {fittedFrames} automatically reduced from native margins.");
    }
}
