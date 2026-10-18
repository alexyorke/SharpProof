using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer;
using SharpProof.Compiler.Semantics;

namespace SharpProof.Analyzer.Test;

internal sealed class RecordingSessionFactory : IAnalyzerSessionFactory
{
    private int _createCount;
    private ImmutableDictionary<
        string,
        AnalyzerSemanticOutcome> _outcomes =
        ImmutableDictionary.Create<string, AnalyzerSemanticOutcome>(StringComparer.Ordinal);
    private ImmutableDictionary<string, int> _outcomeCounts =
        ImmutableDictionary.Create<string, int>(StringComparer.Ordinal);
    private ImmutableDictionary<MethodIdentity, AnalyzerSemanticOutcome>
        _methodOutcomes = ImmutableDictionary<MethodIdentity, AnalyzerSemanticOutcome>.Empty;

    internal ImmutableDictionary<string, AnalyzerSemanticOutcome> Outcomes =>
        Volatile.Read(ref _outcomes);
    internal ImmutableDictionary<string, int> OutcomeCounts =>
        Volatile.Read(ref _outcomeCounts);
    internal int CreateCount => Volatile.Read(ref _createCount);
    internal AnalyzerSemanticOutcome GetNamedOutcome(string name)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        return _methodOutcomes.Single(pair =>
                string.Equals(pair.Key.Name, name, StringComparison.Ordinal))
            .Value;
    }

    internal ImmutableArray<AnalyzerSemanticOutcome> GetOutcomes(MethodKind kind)
    {
        return _methodOutcomes
            .Where(pair => pair.Key.Kind == kind)
            .Select(static pair => pair.Value).ToImmutableArray();
    }
    internal AnalyzerSession? Session
    {
        get;
        private set;
    }

    public AnalyzerSession Create(
        Compilation compilation,
        CompilerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(configuration);
        Interlocked.Increment(ref _createCount);
        Session = new AnalyzerSession(
            compilation,
            configuration,
            cancellationToken,
            (method, outcome) =>
            {
                System.ArgumentNullException.ThrowIfNull(method);
                ImmutableInterlocked.AddOrUpdate(ref _methodOutcomes,
                    MethodIdentity.Create(method),
                    outcome,
                    (_, current) =>
                        AnalyzerSemanticOutcomes.Combine(current, outcome));
                ImmutableInterlocked.AddOrUpdate(ref _outcomeCounts,
                    method.Name,
                    1,
                    static (_, current) => current + 1);
                ImmutableInterlocked.AddOrUpdate(ref _outcomes,
                    method.Name,
                    outcome,
                    (_, current) =>
                        AnalyzerSemanticOutcomes.Combine(current, outcome));
            });
        return Session;
    }

    private readonly record struct MethodIdentity(
        MethodKind Kind,
        string Name,
        int SpanStart)
    {
        internal static MethodIdentity Create(IMethodSymbol method)
        {
            System.ArgumentNullException.ThrowIfNull(method);
            return new(
                method.MethodKind,
                method.Name,
                method.DeclaringSyntaxReferences.FirstOrDefault()?.Span.Start ?? -1);
        }
        public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
    }
}
