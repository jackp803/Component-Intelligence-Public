using System.Threading;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Extraction;

/// <summary>
/// Async-scoped target identity for document extraction. ComponentIntelligencePipeline establishes the
/// scope before enrichment so every source adapter uses the same identity gate without duplicating rules.
/// </summary>
public static class DocumentIdentityContext
{
    private static readonly AsyncLocal<ComponentIdentity?> CurrentSlot = new();

    public static ComponentIdentity? Current => CurrentSlot.Value;

    public static IDisposable Push(ComponentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var previous = CurrentSlot.Value;
        CurrentSlot.Value = identity;
        return new Scope(previous);
    }

    private sealed class Scope(ComponentIdentity? previous) : IDisposable
    {
        private ComponentIdentity? _previous = previous;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            CurrentSlot.Value = _previous;
            _previous = null;
            _disposed = true;
        }
    }
}
