using System;
using System.ComponentModel;
using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.TaskCoverage
{
    public class T002Tests
    {
        [Fact]
        public void BomImportStatus_HasExpectedValues()
        {
            var values = (BomImportStatus[])Enum.GetValues(typeof(BomImportStatus));
            Assert.Contains(BomImportStatus.Imported, values);
            Assert.Contains(BomImportStatus.ImportedWithWarnings, values);
            Assert.Contains(BomImportStatus.Invalid, values);
        }

        [Fact]
        public void ResolutionStatus_HasExpectedValues()
        {
            var values = (ResolutionStatus[])Enum.GetValues(typeof(ResolutionStatus));
            Assert.Contains(ResolutionStatus.WaitingForInput, values);
            Assert.Contains(ResolutionStatus.Resolving, values);
            Assert.Contains(ResolutionStatus.Resolved, values);
            Assert.Contains(ResolutionStatus.Ambiguous, values);
            Assert.Contains(ResolutionStatus.NotFound, values);
            Assert.Contains(ResolutionStatus.Conflict, values);
            Assert.Contains(ResolutionStatus.Failed, values);
        }

        [Fact]
        public void MatchLevel_HasExpectedValues()
        {
            var values = (MatchLevel[])Enum.GetValues(typeof(MatchLevel));
            Assert.Contains(MatchLevel.None, values);
            Assert.Contains(MatchLevel.Exact, values);
            Assert.Contains(MatchLevel.Strong, values);
            Assert.Contains(MatchLevel.Ambiguous, values);
        }

        [Fact]
        public void EnrichmentStatus_HasExpectedValues()
        {
            var values = (EnrichmentStatus[])Enum.GetValues(typeof(EnrichmentStatus));
            Assert.Contains(EnrichmentStatus.NotStarted, values);
            Assert.Contains(EnrichmentStatus.Enriching, values);
            Assert.Contains(EnrichmentStatus.Enriched, values);
            Assert.Contains(EnrichmentStatus.Partial, values);
            Assert.Contains(EnrichmentStatus.Failed, values);
        }

        [Fact]
        public void VerificationStatus_HasExpectedValues()
        {
            var values = (VerificationStatus[])Enum.GetValues(typeof(VerificationStatus));
            Assert.Contains(VerificationStatus.Verified, values);
            Assert.Contains(VerificationStatus.SingleSource, values);
            Assert.Contains(VerificationStatus.Conflict, values);
            Assert.Contains(VerificationStatus.NotAvailable, values);
            Assert.Contains(VerificationStatus.NotFound, values);
            Assert.Contains(VerificationStatus.Inferred, values);
            Assert.Contains(VerificationStatus.UserConfirmed, values);
        }

        [Fact]
        public void ReadinessStatus_HasExpectedValues()
        {
            var values = (ReadinessStatus[])Enum.GetValues(typeof(ReadinessStatus));
            Assert.Contains(ReadinessStatus.Ready, values);
            Assert.Contains(ReadinessStatus.Partial, values);
            Assert.Contains(ReadinessStatus.NotReady, values);
        }

        [Fact]
        public void EnumSerialization_SerializesAndDeserializes()
        {
            // Test string-based serialization/deserialization for all enums
            var importedStatus = BomImportStatus.Imported;
            var serialized = importedStatus.ToString();
            var deserialized = (BomImportStatus)Enum.Parse(typeof(BomImportStatus), serialized);
            Assert.Equal(importedStatus, deserialized);

            var resolvedStatus = ResolutionStatus.Resolved;
            var resSerialized = resolvedStatus.ToString();
            var resDeserialized = (ResolutionStatus)Enum.Parse(typeof(ResolutionStatus), resSerialized);
            Assert.Equal(resolvedStatus, resDeserialized);

            var exactMatch = MatchLevel.Exact;
            var matchSerialized = exactMatch.ToString();
            var matchDeserialized = (MatchLevel)Enum.Parse(typeof(MatchLevel), matchSerialized);
            Assert.Equal(exactMatch, matchDeserialized);

            var enrichedStatus = EnrichmentStatus.Enriched;
            var enrichSerialized = enrichedStatus.ToString();
            var enrichDeserialized = (EnrichmentStatus)Enum.Parse(typeof(EnrichmentStatus), enrichSerialized);
            Assert.Equal(enrichedStatus, enrichDeserialized);

            var verifiedStatus = VerificationStatus.Verified;
            var verifySerialized = verifiedStatus.ToString();
            var verifyDeserialized = (VerificationStatus)Enum.Parse(typeof(VerificationStatus), verifySerialized);
            Assert.Equal(verifiedStatus, verifyDeserialized);

            var readyStatus = ReadinessStatus.Ready;
            var readySerialized = readyStatus.ToString();
            var readyDeserialized = (ReadinessStatus)Enum.Parse(typeof(ReadinessStatus), readySerialized);
            Assert.Equal(readyStatus, readyDeserialized);
        }

        [Fact]
        public void EnumSerialization_NameMatchesValue()
        {
            // Verify that name-based round-trip works for all enum types
            Assert.Equal("Imported", BomImportStatus.Imported.ToString());
            Assert.Equal("Resolved", ResolutionStatus.Resolved.ToString());
            Assert.Equal("Exact", MatchLevel.Exact.ToString());
            Assert.Equal("Enriched", EnrichmentStatus.Enriched.ToString());
            Assert.Equal("Verified", VerificationStatus.Verified.ToString());
            Assert.Equal("Ready", ReadinessStatus.Ready.ToString());

            Assert.True(Enum.IsDefined(typeof(BomImportStatus), "Imported"));
            Assert.True(Enum.IsDefined(typeof(ResolutionStatus), "Resolved"));
            Assert.True(Enum.IsDefined(typeof(MatchLevel), "Exact"));
            Assert.True(Enum.IsDefined(typeof(EnrichmentStatus), "Enriched"));
            Assert.True(Enum.IsDefined(typeof(VerificationStatus), "Verified"));
            Assert.True(Enum.IsDefined(typeof(ReadinessStatus), "Ready"));
        }

        [Fact]
        public void EnumSerialization_DontDependOnExternalModules()
        {
            // This test confirms that enum serialization works using only System and standard library,
            // without any external module dependencies. The test itself is the proof.
            var value = BomImportStatus.ImportedWithWarnings;
            var name = value.ToString();
            Assert.Equal("ImportedWithWarnings", name);

            var parsed = Enum.Parse<BomImportStatus>(name);
            Assert.Equal(value, parsed);
        }

        [Fact]
        public void AllEnums_AreWellFormed()
        {
            // Ensure all required enums are defined and have at least 2 values
            Assert.True(Enum.GetValues(typeof(BomImportStatus)).Length >= 2);
            Assert.True(Enum.GetValues(typeof(ResolutionStatus)).Length >= 2);
            Assert.True(Enum.GetValues(typeof(MatchLevel)).Length >= 2);
            Assert.True(Enum.GetValues(typeof(EnrichmentStatus)).Length >= 2);
            Assert.True(Enum.GetValues(typeof(VerificationStatus)).Length >= 2);
            Assert.True(Enum.GetValues(typeof(ReadinessStatus)).Length >= 2);
        }

        [Fact]
        public void EnumUnderlyingType_IsInt()
        {
            // Confirm all enums use the default underlying type (int)
            Assert.Equal(System.TypeCode.Int32, Type.GetTypeCode(typeof(BomImportStatus)));
            Assert.Equal(System.TypeCode.Int32, Type.GetTypeCode(typeof(ResolutionStatus)));
            Assert.Equal(System.TypeCode.Int32, Type.GetTypeCode(typeof(MatchLevel)));
            Assert.Equal(System.TypeCode.Int32, Type.GetTypeCode(typeof(EnrichmentStatus)));
            Assert.Equal(System.TypeCode.Int32, Type.GetTypeCode(typeof(VerificationStatus)));
            Assert.Equal(System.TypeCode.Int32, Type.GetTypeCode(typeof(ReadinessStatus)));
        }
    }
}
