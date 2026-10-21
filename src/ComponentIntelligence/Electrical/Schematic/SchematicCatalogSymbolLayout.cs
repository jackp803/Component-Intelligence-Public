using System.Security.Cryptography;

namespace ComponentIntelligence.Electrical.Schematic;

// PNG bytes are captured from the decoded preview bitmap, never a remote URL.
public sealed record SchematicRasterAsset(byte[] Png, int PixelWidth, int PixelHeight)
{
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Png));
    public string File => "images/" + Sha256 + ".png";
}

public sealed record SchematicCatalogSymbolLayout(SchematicPoint ImageTopLeft, double ImageWidth,
    double ImageHeight, SchematicPoint LabelTopLeft, double LabelWidth)
{
    public static SchematicCatalogSymbolLayout Create(double width, double height, int pixelWidth, int pixelHeight, int rotation = 0)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 4 || height <= 14 || pixelWidth <= 0 || pixelHeight <= 0)
            throw new InvalidOperationException("Catalog image needs positive pixel dimensions and room for its label.");
        var quarterTurn = rotation % 180 != 0;
        var scale = Math.Min((width - 4) / (quarterTurn ? pixelHeight : pixelWidth),
            (height - 14) / (quarterTurn ? pixelWidth : pixelHeight));
        var w = pixelWidth * scale; var h = pixelHeight * scale;
        return new(new((width - w) / 2, 2 + (height - 14 - h) / 2), w, h, new(2, height - 10), width - 4);
    }
}
