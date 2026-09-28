using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using netDxf;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicRasterExportTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWZkAAAAASUVORK5CYII=");

    [Fact]
    public void PackageContainsEveryReferencedPictureOnceWithContentHashes()
    {
        var service = new SchematicAuthoringService();
        var project = service.AddPage(new ElectricalProject { ProjectId = "TEST" }, "One");
        project = service.AddPage(project, "Two");
        project.Components.Add(new() { ComponentInstanceId = "component", ComponentDefinitionId = "CATALOG", TypeKey = "Device" });
        foreach (var page in project.Schematic!.Pages)
            project.Schematic.Symbols.Add(new() { SymbolId = page.PageId + "-symbol", ComponentInstanceId = "component", PageId = page.PageId });
        using var output = new MemoryStream();
        new SchematicDxfExporter().WritePackage(output, project, new Dictionary<string, SchematicRasterAsset> { ["CATALOG"] = new(Png, 1, 1) });
        output.Position = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        var image = Assert.Single(zip.Entries, e => e.FullName.StartsWith("images/"));
        using var data = new MemoryStream(); using (var input = image.Open()) input.CopyTo(data);
        Assert.Equal(Png, data.ToArray());
        Assert.Equal("images/" + Convert.ToHexString(SHA256.HashData(Png)) + ".png", image.FullName);
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        Assert.Equal("DRAFT_NOT_VERIFIED", manifest.RootElement.GetProperty("status").GetString());
        Assert.Single(manifest.RootElement.GetProperty("images").EnumerateArray());
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".dxf")))
        {
            using var dxfBytes = new MemoryStream(); using (var input = entry.Open()) input.CopyTo(dxfBytes);
            dxfBytes.Position = 0;
            Assert.Equal(image.FullName, Assert.Single(DxfDocument.Load(dxfBytes).Entities.Images).Definition.File);
        }
    }

    [Theory]
    [InlineData(0, 32, 38)]
    [InlineData(90, 32, 32)]
    [InlineData(180, 48, 32)]
    [InlineData(270, 38, 48)]
    public void CatalogPictureIsPortableAndKeepsExactRotatedPlacement(int rotation, double x, double y)
    {
        var service = new SchematicAuthoringService();
        var project = service.AddPage(new ElectricalProject { ProjectId = "TEST" }, "Picture");
        var page = project.Schematic!.Pages[0];
        project.Components.Add(new() { ComponentInstanceId = "component", ComponentDefinitionId = "CATALOG", TypeKey = "Device", DisplayName = "Device", ReferenceDesignator = "K1" });
        project.Schematic.Symbols.Add(new() { SymbolId = "symbol", ComponentInstanceId = "component", PageId = page.PageId,
            Position = new(20, 20), Width = 40, Height = 30, Rotation = rotation });
        var before = JsonSerializer.Serialize(project);
        var images = new Dictionary<string, SchematicRasterAsset> { ["CATALOG"] = new(Png, 1, 1) };
        var sheet = Assert.Single(new SchematicDxfExporter().Create(project, images));
        var packaged = Assert.Single(sheet.Images);
        Assert.Equal(Png, packaged.Png);
        Assert.Equal("images/" + packaged.Sha256 + ".png", packaged.File);
        using var stream = new MemoryStream(sheet.Dxf);
        var image = Assert.Single(DxfDocument.Load(stream).Entities.Images);
        Assert.Equal(packaged.File, image.Definition.File);
        Assert.Equal(x, image.Position.X, 8);
        Assert.Equal(page.Height - y, image.Position.Y, 8);
        Assert.Equal(16, image.Width, 8);
        Assert.Equal(16, image.Height, 8);
        Assert.Equal((360 - rotation) % 360, image.Rotation, 8);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.StartsWith("CATALOG_RASTER_IMAGE_NOT_EMBEDDED"));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void SharedCatalogLayoutFitsAspectRatioAndReservesLabelArea()
    {
        var layout = SchematicCatalogSymbolLayout.Create(40, 30, 200, 100);
        Assert.Equal(new SchematicPoint(4, 2), layout.ImageTopLeft);
        Assert.Equal(32, layout.ImageWidth);
        Assert.Equal(16, layout.ImageHeight);
        Assert.Equal(new SchematicPoint(2, 20), layout.LabelTopLeft);
        Assert.Equal(36, layout.LabelWidth);
    }
}
