using System.Text;

namespace ComponentIntelligence.Resolution;

public sealed record NormalizedModel(string Raw, string Canonical, string SearchKey);

public static class ModelNormalizer
{
    public static NormalizedModel? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var raw = value;
        var canonical = value.Trim().Normalize(NormalizationForm.FormKC);
        var searchKey = canonical.ToUpperInvariant();
        return new NormalizedModel(raw, canonical, searchKey);
    }
}
