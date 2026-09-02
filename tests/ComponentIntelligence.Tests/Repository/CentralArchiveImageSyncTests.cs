using ClosedXML.Excel;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Repository;

public sealed class CentralArchiveImageSyncTests
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task RelativeImagePath_ResolvesOnlyProductImage()
    {
        var directory = CreateDirectory();
        try
        {
            var imageRelative = "Documents/ACME/MODEL-1/product.png";
            var drawingRelative = "Documents/ACME/MODEL-1/drawing.png";
            WriteAsset(directory, imageRelative, ValidPng);
            WriteAsset(directory, drawingRelative, [1, 2, 3]);
            var workbookPath = Path.Combine(directory, "Component_Intelligence_Database.xlsx");
            CreateWorkbook(workbookPath, imageRelative, drawingRelative);

            var component = Assert.Single(await new WorkbookComponentKnowledgeStore(workbookPath).ListAsync());

            Assert.NotNull(component.Assets.ImageUrl);
            Assert.True(component.Assets.ImageUrl!.IsFile);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(directory, imageRelative.Replace('/', Path.DirectorySeparatorChar))),
                component.Assets.ImageUrl.LocalPath);
            Assert.NotEqual(
                Path.GetFullPath(Path.Combine(directory, drawingRelative.Replace('/', Path.DirectorySeparatorChar))),
                component.Assets.ImageUrl.LocalPath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MissingImagePath_FallsBackSafelyAndNeverUsesDrawingPath()
    {
        var directory = CreateDirectory();
        try
        {
            var missingImage = "Documents/ACME/MODEL-1/missing.png";
            var drawingRelative = "Documents/ACME/MODEL-1/drawing.png";
            WriteAsset(directory, drawingRelative, ValidPng);
            var workbookPath = Path.Combine(directory, "Component_Intelligence_Database.xlsx");
            CreateWorkbook(workbookPath, missingImage, drawingRelative);

            var component = Assert.Single(await new WorkbookComponentKnowledgeStore(workbookPath).ListAsync());

            Assert.Null(component.Assets.ImageUrl);
            Assert.Contains(component.Specifications, specification =>
                specification.Key == "drawing_path" && specification.Value == drawingRelative);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CentralSync_CachesImageAndPersistsReadableLocalUri()
    {
        var directory = CreateDirectory();
        try
        {
            var imageRelative = "Documents/ACME/MODEL-1/product.png";
            var sourcePath = WriteAsset(directory, imageRelative, ValidPng);
            var workbookPath = Path.Combine(directory, "Component_Intelligence_Database.xlsx");
            CreateWorkbook(workbookPath, imageRelative, string.Empty);
            var archive = await new WorkbookComponentKnowledgeStore(workbookPath).ListAsync();
            var cacheRoot = Path.Combine(directory, "local-image-cache");

            var synchronized = await new CentralArchiveImageSynchronizer(
                    new ComponentImageFileCache(cacheRoot))
                .SynchronizeAsync(archive);
            var cached = Assert.Single(synchronized);
            var cachedPath = cached.Assets.ImageUrl?.LocalPath;

            Assert.NotNull(cachedPath);
            Assert.True(File.Exists(cachedPath));
            Assert.StartsWith(Path.GetFullPath(cacheRoot), Path.GetFullPath(cachedPath!), StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(Path.GetFullPath(sourcePath), Path.GetFullPath(cachedPath!));
            Assert.Equal(ValidPng, await File.ReadAllBytesAsync(cachedPath!));

            var sqlite = new SqliteComponentIrRepository(Path.Combine(directory, "components.db"));
            await sqlite.SaveAsync(cached);
            var reloaded = await sqlite.FindByIdentityAsync("ACME", "MODEL-1");
            Assert.NotNull(reloaded?.Assets.ImageUrl);
            Assert.True(File.Exists(reloaded!.Assets.ImageUrl!.LocalPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ci-central-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string WriteAsset(string root, string relativePath, byte[] contents)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
        return path;
    }

    private static void CreateWorkbook(string path, string imagePath, string drawingPath)
    {
        using var workbook = new XLWorkbook();
        var components = workbook.AddWorksheet("Components");
        WriteRow(components, 1,
            "ComponentID", "Manufacturer", "Model", "Category", "ImagePath", "DrawingPath",
            "TopologyStatus", "LayoutStatus");
        WriteRow(components, 2,
            "ACME-MODEL-1", "ACME", "MODEL-1", "Controller", imagePath, drawingPath,
            "Ready", "Ready");

        var ports = workbook.AddWorksheet("Ports");
        WriteRow(ports, 1, "PortID", "ComponentID", "PortName");
        var pins = workbook.AddWorksheet("Pins");
        WriteRow(pins, 1, "PinID", "PortID", "PinNumber");
        workbook.SaveAs(path);
    }

    private static void WriteRow(IXLWorksheet sheet, int row, params string[] values)
    {
        for (var column = 0; column < values.Length; column++)
            sheet.Cell(row, column + 1).Value = values[column];
    }
}
