using ComponentIntelligence.Contracts;
namespace ComponentIntelligence.Verification;
public interface IVerificationEngine
{
    Task<VerificationSummary> VerifyAsync(ComponentIR component, RawComponentProfile raw, CancellationToken cancellationToken = default);
}
public sealed record VerificationSummary(VerificationStatus Status, decimal Completeness, string Confidence, ComponentReadiness Readiness, IReadOnlyList<string> Issues);
