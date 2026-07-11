using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using SearchLib.Smt;

namespace SharpProof.Symbolic;

internal enum SymbolicPipelineMode
{
    Legacy,
    New,
    Shadow
}

internal sealed record SymbolicPipelineDisagreement(
    string Stage,
    Location Location,
    bool LegacySucceeded,
    SmtFormula? LegacyFormula,
    bool NewSucceeded,
    SmtFormula? NewFormula);

internal static class SymbolicPipelineTestControl
{
    private static readonly AsyncLocal<ScopeState?> s_current = new();

    internal static SymbolicPipelineMode Mode => s_current.Value?.Mode ?? SymbolicPipelineMode.Legacy;

    internal static ImmutableArray<SymbolicPipelineDisagreement> Disagreements =>
        s_current.Value?.Disagreements.ToImmutableArray() ?? ImmutableArray<SymbolicPipelineDisagreement>.Empty;

    internal static IDisposable UseMode(SymbolicPipelineMode mode)
    {
        var previous = s_current.Value;
        var current = new ScopeState(mode);
        s_current.Value = current;
        return new RestoreScope(previous, current);
    }

    internal static void RecordFormulaDisagreement(
        string stage,
        SyntaxNode source,
        bool legacySucceeded,
        SmtFormula? legacyFormula,
        bool newSucceeded,
        SmtFormula? newFormula)
    {
        if (legacySucceeded == newSucceeded && Equals(legacyFormula, newFormula)) return;

        s_current.Value?.Disagreements.Add(new SymbolicPipelineDisagreement(
            stage,
            source.GetLocation(),
            legacySucceeded,
            legacyFormula,
            newSucceeded,
            newFormula));
    }

    private sealed class ScopeState
    {
        internal ScopeState(SymbolicPipelineMode mode)
        {
            Mode = mode;
        }

        internal SymbolicPipelineMode Mode { get; }
        internal List<SymbolicPipelineDisagreement> Disagreements { get; } = new();
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly ScopeState? _previous;
        private ScopeState? _current;

        internal RestoreScope(ScopeState? previous, ScopeState current)
        {
            _previous = previous;
            _current = current;
        }

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _current, null);
            if (current == null) return;

            if (ReferenceEquals(s_current.Value, current)) s_current.Value = _previous;
        }
    }
}
