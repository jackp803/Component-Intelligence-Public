using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Cables;

public enum RequirementLevel
{
    Required,
    Preferred,
    Optional,
    Unknown
}

public enum CableSolutionType
{
    SingleCable,
    MultiCableCombination
}

public sealed record ConductorRequirement
{
    public required string RequirementId { get; init; }
    public double? MinAreaMm2 { get; init; }
    public int? MaxAwg { get; init; }
    public string? PairGroup { get; init; }
    public RequirementLevel PairLevel { get; init; } = RequirementLevel.Unknown;
    public ElectricalLayer Layer { get; init; } = ElectricalLayer.Unknown;
    public string? Signal { get; init; }
}

public sealed record CableRequirement
{
    public required string RequirementId { get; init; }
    public List<ConductorRequirement> Conductors { get; init; } = new();
    public double? MinVoltageRating { get; init; }
    public RequirementLevel Shielding { get; init; } = RequirementLevel.Unknown;
    public RequirementLevel DragChain { get; init; } = RequirementLevel.Unknown;
    public List<string> CommunicationStandards { get; init; } = new();
    public int? MinTwistedPairCount { get; init; }
    public int? MaxCableEntries { get; init; }
    public double? MaxCableOuterDiameterMm { get; init; }
}

public sealed record CableProductCandidate
{
    public required CableDefinition Definition { get; init; }
    public bool ApprovedMaterial { get; init; }
    public bool StandardProduct { get; init; } = true;
    public double? OuterDiameterMm { get; init; }
}

public sealed record CablePlanningPolicy
{
    public double ExactCoreCountBonus { get; init; } = 25;
    public double ApprovedMaterialBonus { get; init; } = 20;
    public double StandardProductBonus { get; init; } = 8;
    public double SingleCableBonus { get; init; } = 12;
    public double ExcessCorePenalty { get; init; } = 2;
    public double ExcessAreaPenaltyPerMm2 { get; init; } = 0.5;
    public double PreferredShieldBonus { get; init; } = 5;
    public double PreferredDragChainBonus { get; init; } = 5;
}

public sealed record ConductorAssignmentResult
{
    public required string RequirementId { get; init; }
    public required string CableDefinitionId { get; init; }
    public required string CoreId { get; init; }
    public required double NormalizedAreaMm2 { get; init; }
}

public sealed record CableSolutionCandidate
{
    public required CableSolutionType SolutionType { get; init; }
    public required IReadOnlyList<CableProductCandidate> Members { get; init; }
    public required IReadOnlyList<ConductorAssignmentResult> Assignments { get; init; }
    public required double Score { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
}

public sealed class CablePlanningEngine
{
    public IReadOnlyList<CableSolutionCandidate> FindSolutions(
        CableRequirement requirement,
        IEnumerable<CableProductCandidate> candidates,
        CablePlanningPolicy? policy = null,
        int maxResults = 20)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(candidates);
        if (maxResults <= 0) throw new ArgumentOutOfRangeException(nameof(maxResults));
        policy ??= new CablePlanningPolicy();

        var products = candidates.ToArray();
        var solutions = new List<CableSolutionCandidate>();

        foreach (var product in products)
        {
            var result = Evaluate(requirement, new[] { product }, policy);
            if (result is not null) solutions.Add(result);
        }

        if (requirement.MaxCableEntries is null or >= 2)
        {
            for (var first = 0; first < products.Length; first++)
            for (var second = first + 1; second < products.Length; second++)
            {
                var result = Evaluate(requirement, new[] { products[first], products[second] }, policy);
                if (result is not null) solutions.Add(result);
            }
        }

        return solutions
            .OrderByDescending(solution => solution.Score)
            .ThenBy(solution => solution.Members.Count)
            .ThenBy(solution => string.Join('|', solution.Members.Select(member => member.Definition.PartNumber ?? member.Definition.CableDefinitionId)), StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();
    }

    private static CableSolutionCandidate? Evaluate(
        CableRequirement requirement,
        IReadOnlyList<CableProductCandidate> members,
        CablePlanningPolicy policy)
    {
        if (requirement.MaxCableEntries is int maxEntries && members.Count > maxEntries) return null;

        if (requirement.MaxCableOuterDiameterMm is double maxOuter &&
            members.Any(member => member.OuterDiameterMm is not double outer || outer > maxOuter))
            return null;

        if (requirement.MinVoltageRating is double minVoltage &&
            members.Any(member => member.Definition.VoltageRating is not double rating || rating < minVoltage))
            return null;

        if (requirement.Shielding == RequirementLevel.Required && !members.Any(member => member.Definition.Shielded == true)) return null;
        if (requirement.DragChain == RequirementLevel.Required && members.Any(member => member.Definition.DragChainSuitable != true)) return null;

        foreach (var standard in requirement.CommunicationStandards.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!members.Any(member => SupportsProtocol(member, standard))) return null;
        }

        var available = members
            .SelectMany(member => member.Definition.Cores.Select(core => new AvailableCore(member, core, WireSize.NormalizeAreaMm2(core))))
            .ToList();
        var assignments = new List<ConductorAssignmentResult>();
        var areaOversize = 0.0;

        if (!TryAssignRequiredPairGroups(requirement, available, assignments, ref areaOversize)) return null;

        var alreadyAssigned = assignments.Select(item => item.RequirementId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var conductor in requirement.Conductors
                     .Where(item => !alreadyAssigned.Contains(item.RequirementId))
                     .OrderByDescending(RequiredArea))
        {
            var requiredArea = RequiredArea(conductor);
            var match = available
                .Where(core => core.AreaMm2 + 1e-9 >= requiredArea)
                .OrderBy(core => core.AreaMm2)
                .ThenBy(core => core.Member.Definition.CableDefinitionId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (match is null) return null;

            assignments.Add(ToAssignment(conductor, match));
            areaOversize += Math.Max(0, match.AreaMm2 - requiredArea);
            available.Remove(match);
        }

        if (requirement.MinTwistedPairCount is int pairCount && CountUsablePairs(members) < pairCount) return null;

        // A combination is meaningful only when every physical cable member contributes at least
        // one assigned conductor. This prevents ranking a valid cable plus a completely unused extra cable.
        var usedMemberIds = assignments.Select(item => item.CableDefinitionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (members.Any(member => !usedMemberIds.Contains(member.Definition.CableDefinitionId))) return null;

        var totalCoreCount = members.Sum(member => member.Definition.Cores.Count > 0 ? member.Definition.Cores.Count : member.Definition.CoreCount);
        var excessCores = Math.Max(0, totalCoreCount - requirement.Conductors.Count);
        var score = 100.0;
        var reasons = new List<string> { "All REQUIRED cable constraints passed." };

        if (totalCoreCount == requirement.Conductors.Count)
        {
            score += policy.ExactCoreCountBonus;
            reasons.Add("Exact conductor/core count match.");
        }
        else if (excessCores > 0)
        {
            score -= excessCores * policy.ExcessCorePenalty;
            reasons.Add($"{excessCores} excess core(s) reduce ranking; overcapacity is not automatically better.");
        }

        score -= areaOversize * policy.ExcessAreaPenaltyPerMm2;
        if (areaOversize > 0) reasons.Add("Conductor oversize is accepted but ranked below a closer fit when otherwise equivalent.");

        if (members.All(member => member.ApprovedMaterial))
        {
            score += policy.ApprovedMaterialBonus;
            reasons.Add("All cable members are approved/existing materials.");
        }
        if (members.All(member => member.StandardProduct))
        {
            score += policy.StandardProductBonus;
            reasons.Add("Uses standard product(s).");
        }
        if (members.Count == 1)
        {
            score += policy.SingleCableBonus;
            reasons.Add("Single physical cable solution is simpler than a multi-cable assembly.");
        }
        if (requirement.Shielding == RequirementLevel.Preferred && members.Any(member => member.Definition.Shielded == true))
        {
            score += policy.PreferredShieldBonus;
            reasons.Add("Preferred shielding is satisfied.");
        }
        if (requirement.DragChain == RequirementLevel.Preferred && members.All(member => member.Definition.DragChainSuitable == true))
        {
            score += policy.PreferredDragChainBonus;
            reasons.Add("Preferred drag-chain suitability is satisfied.");
        }

        return new CableSolutionCandidate
        {
            SolutionType = members.Count == 1 ? CableSolutionType.SingleCable : CableSolutionType.MultiCableCombination,
            Members = members,
            Assignments = assignments,
            Score = Math.Round(score, 3),
            Reasons = reasons
        };
    }

    private static bool TryAssignRequiredPairGroups(
        CableRequirement requirement,
        List<AvailableCore> available,
        List<ConductorAssignmentResult> assignments,
        ref double totalOversize)
    {
        var logicalGroups = requirement.Conductors
            .Where(item => item.PairLevel == RequirementLevel.Required && !string.IsNullOrWhiteSpace(item.PairGroup))
            .GroupBy(item => item.PairGroup!, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var logicalGroup in logicalGroups)
        {
            var requirements = logicalGroup.OrderByDescending(RequiredArea).ToArray();
            PairAssignmentPlan? best = null;

            var physicalGroups = available
                .Where(core => !string.IsNullOrWhiteSpace(core.Core.PairGroup))
                .GroupBy(core => new
                {
                    Cable = core.Member.Definition.CableDefinitionId,
                    Pair = NormalizeToken(core.Core.PairGroup!)
                });

            foreach (var physicalGroup in physicalGroups)
            {
                var physicalCores = physicalGroup.ToList();
                if (physicalCores.Count < requirements.Length) continue;
                var member = physicalCores[0].Member;

                // When a logical pair is named after a requested communication standard (e.g. RS485),
                // the actual cable carrying that pair must itself declare that communication capability.
                var matchingStandard = requirement.CommunicationStandards.FirstOrDefault(standard => ProtocolEquals(standard, logicalGroup.Key));
                if (matchingStandard is not null && !SupportsProtocol(member, matchingStandard)) continue;

                // Required shielding must protect the communication pair itself, not merely exist on an unrelated power cable.
                if (requirement.Shielding == RequirementLevel.Required && member.Definition.Shielded != true) continue;

                var remaining = physicalCores.ToList();
                var proposed = new List<(ConductorRequirement Requirement, AvailableCore Core)>();
                var oversize = 0.0;
                var valid = true;
                foreach (var conductor in requirements)
                {
                    var requiredArea = RequiredArea(conductor);
                    var match = remaining
                        .Where(core => core.AreaMm2 + 1e-9 >= requiredArea)
                        .OrderBy(core => core.AreaMm2)
                        .FirstOrDefault();
                    if (match is null)
                    {
                        valid = false;
                        break;
                    }
                    proposed.Add((conductor, match));
                    oversize += Math.Max(0, match.AreaMm2 - requiredArea);
                    remaining.Remove(match);
                }

                if (!valid) continue;
                if (best is null || oversize < best.Oversize)
                    best = new PairAssignmentPlan(proposed, oversize);
            }

            if (best is null) return false;
            foreach (var proposed in best.Assignments)
            {
                assignments.Add(ToAssignment(proposed.Requirement, proposed.Core));
                available.Remove(proposed.Core);
            }
            totalOversize += best.Oversize;
        }

        return true;
    }

    private static ConductorAssignmentResult ToAssignment(ConductorRequirement conductor, AvailableCore core) => new()
    {
        RequirementId = conductor.RequirementId,
        CableDefinitionId = core.Member.Definition.CableDefinitionId,
        CoreId = core.Core.CoreId,
        NormalizedAreaMm2 = core.AreaMm2
    };

    private static double RequiredArea(ConductorRequirement conductor) =>
        conductor.MinAreaMm2 ?? (conductor.MaxAwg is int awg ? WireSize.AwgToAreaMm2(awg) : 0);

    private static int CountUsablePairs(IEnumerable<CableProductCandidate> members) => members
        .SelectMany(member => member.Definition.Cores
            .Where(core => !string.IsNullOrWhiteSpace(core.PairGroup))
            .Select(core => new { MemberId = member.Definition.CableDefinitionId, Pair = NormalizeToken(core.PairGroup!) }))
        .GroupBy(item => (item.MemberId, item.Pair))
        .Count(group => group.Count() >= 2);

    private static bool SupportsProtocol(CableProductCandidate member, string protocol) =>
        member.Definition.CommunicationCapabilities.Any(capability => ProtocolEquals(capability, protocol));

    private static bool ProtocolEquals(string first, string second) => NormalizeToken(first) == NormalizeToken(second);

    private static string NormalizeToken(string value) => value
        .Trim()
        .Replace("-", string.Empty)
        .Replace("_", string.Empty)
        .Replace(" ", string.Empty)
        .ToUpperInvariant();

    private sealed record AvailableCore(CableProductCandidate Member, CableCoreDefinition Core, double AreaMm2);
    private sealed record PairAssignmentPlan(IReadOnlyList<(ConductorRequirement Requirement, AvailableCore Core)> Assignments, double Oversize);
}

public static class WireSize
{
    public static double AwgToAreaMm2(int awg)
    {
        if (awg is < 0 or > 40) throw new ArgumentOutOfRangeException(nameof(awg));
        var diameterMm = 0.127 * Math.Pow(92, (36.0 - awg) / 39.0);
        return Math.PI * diameterMm * diameterMm / 4.0;
    }

    public static double NormalizeAreaMm2(CableCoreDefinition core)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (core.AreaMm2 is double area && area > 0) return area;
        if (core.Awg is int awg) return AwgToAreaMm2(awg);
        return 0;
    }
}
