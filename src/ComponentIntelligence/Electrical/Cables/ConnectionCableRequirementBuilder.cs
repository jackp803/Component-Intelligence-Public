using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Cables;

/// <summary>
/// Converts a topology connection analysis into a CableRequirement without inventing unknown
/// conductor mappings. The result may remain partial; MissingData on the analysis explains why.
/// </summary>
public sealed class ConnectionCableRequirementBuilder
{
    private readonly ConnectionEngineeringAnalyzer _analyzer = new();

    public CableRequirement Build(ElectricalProject project, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var analysis = _analyzer.Analyze(project, connectionId);
        var requirement = new CableRequirement
        {
            RequirementId = $"connection:{connectionId}",
            MinVoltageRating = RequiredVoltageRating(analysis),
            Shielding = analysis.Shielding,
            DragChain = analysis.DragChain,
            MinTwistedPairCount = analysis.TwistedPair == RequirementLevel.Required ? 1 : null
        };

        foreach (var standard in analysis.CommunicationStandards)
            requirement.CommunicationStandards.Add(standard);

        if (analysis.RequiredCoreCount is int coreCount and > 0 and <= 128)
        {
            for (var index = 1; index <= coreCount; index++)
            {
                requirement.Conductors.Add(new ConductorRequirement
                {
                    RequirementId = $"{connectionId}:core:{index}",
                    MinAreaMm2 = analysis.TerminationMinAreaMm2,
                    Layer = analysis.Layer,
                    Signal = analysis.Protocol
                });
            }
        }

        return requirement;
    }

    private static double? RequiredVoltageRating(ConnectionEngineeringAnalysis analysis)
    {
        if (analysis.MaxVoltage is double max) return max;
        if (analysis.NominalVoltage is double nominal) return nominal;
        if (analysis.MinVoltage is double min) return min;
        return null;
    }
}
