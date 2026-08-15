using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Knowledge;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Verification;
using Xunit;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class OcrReviewTests
{
    [Fact]
    public void OcrCandidateParser_PreservesPinRowsAsInferredCandidates()
    {
        var parser = new OcrCandidateParser();
        var candidates = parser.Parse(
            "Operating voltage: 18...30 V DC\nPin 1 L+ 24V\nPin 3 L- 0V\nPin 4 C/Q IO-Link",
            new Uri("file:///datasheet.png"),
            2,
            "ABC123",
            ComponentSourceType.User);

        Assert.Contains(candidates, item => item.RawName == "1" && item.RawValue!.Contains("L+", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(candidates, item => item.RawName == "4" && item.RawValue!.Contains("C/Q", StringComparison.OrdinalIgnoreCase));
        Assert.All(candidates, item => Assert.Equal(VerificationStatus.Inferred, item.Status));
        Assert.All(candidates.SelectMany(item => item.Evidence), evidence => Assert.Equal(ExtractionMethod.OcrText, evidence.ExtractionMethod));
    }

    [Fact]
    public void PinoutExtractor_DoesNotPromoteOcrOnlyPinCandidates()
    {
        var parser = new OcrCandidateParser();
        var candidates = parser.Parse(
            "Pin 1 L+ 24V\nPin 3 L- 0V\nPin 4 C/Q IO-Link",
            new Uri("file:///pinout.png"),
            1,
            "HASH",
            ComponentSourceType.ManufacturerDatasheet);

        var pins = new PinoutExtractor().Extract(candidates);

        Assert.Empty(pins);
    }

    [Fact]
    public void PinoutExtractor_AllowsExplicitlyUserConfirmedOcrPinCandidate()
    {
        var evidence = new Evidence
        {
            SourceType = ComponentSourceType.User,
            SourceUrl = new Uri("file:///pinout.png"),
            DocumentUrl = new Uri("file:///pinout.png"),
            DocumentHashSha256 = "HASH",
            PageNumber = 1,
            ExtractionMethod = ExtractionMethod.OcrText,
            RawValue = "L+ 24V",
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.UserConfirmed
        };
        var candidate = new RawSpecification
        {
            RawName = "1",
            Section = "OCR pinout candidate / page 1",
            RawValue = "L+ 24V",
            Status = VerificationStatus.UserConfirmed,
            Evidence = [evidence]
        };

        var pins = new PinoutExtractor().Extract([candidate]);

        var pin = Assert.Single(pins);
        Assert.Equal("1", pin.PinNumber);
        Assert.Equal("L+ 24V", pin.Function);
    }

    [Fact]
    public void SourceTrustPolicy_DiscountsOcrAgainstNativeManufacturerEvidence()
    {
        var nativeProductPage = new Evidence
        {
            SourceType = ComponentSourceType.ManufacturerProductPage,
            SourceUrl = new Uri("https://manufacturer.example/product"),
            ExtractionMethod = ExtractionMethod.StructuredJson,
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };
        var ocrDatasheet = nativeProductPage with
        {
            SourceType = ComponentSourceType.ManufacturerDatasheet,
            SourceUrl = new Uri("https://manufacturer.example/datasheet.pdf"),
            DocumentUrl = new Uri("https://manufacturer.example/datasheet.pdf"),
            ExtractionMethod = ExtractionMethod.OcrText,
            VerificationStatus = VerificationStatus.Inferred
        };

        Assert.True(SourceTrustPolicy.Score(nativeProductPage) > SourceTrustPolicy.Score(ocrDatasheet));
        Assert.True(SourceTrustPolicy.Score(ocrDatasheet) > SourceTrustPolicy.Score(ComponentSourceType.User));
    }

    [Fact]
    public async Task OcrReviewQueue_PersistsCandidatesWithoutCreatingComponentIr()
    {
        var root = Path.Combine(Path.GetTempPath(), $"component-intelligence-ocr-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var db = Path.Combine(root, "test.db");
        var image = Path.Combine(root, "scan.png");
        await File.WriteAllBytesAsync(image, [1, 2, 3, 4]);

        try
        {
            var service = new OcrReviewQueueService(db, new FakeOcr());
            var result = await service.AnalyzeAsync("ROW-1", "IFM", "TA2115", image);

            Assert.True(result.Attempted);
            Assert.True(result.EngineAvailable);
            Assert.Equal(1, result.RecognizedPages);
            Assert.True(result.CandidateCount >= 3);

            var pending = await service.GetPendingAsync("IFM", "TA2115");
            Assert.Equal(result.CandidateCount, pending.Count);
            Assert.Contains(pending, item => item.RawName == "1" && item.RawValue!.Contains("L+", StringComparison.OrdinalIgnoreCase));

            var component = await new SqliteComponentIrRepository(db).FindByIdentityAsync("IFM", "TA2115");
            Assert.Null(component);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class FakeOcr : IOcrTextExtractor
    {
        public string EngineName => "fake-ocr";
        public bool IsAvailable => true;

        public Task<OcrTextResult> ExtractAsync(
            ReadOnlyMemory<byte> imageBytes,
            string extension,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OcrTextResult(
                true,
                true,
                "Operating voltage: 18...30 V DC\nPin 1 L+ 24V\nPin 3 L- 0V\nPin 4 C/Q IO-Link",
                EngineName));
    }
}
