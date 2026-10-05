using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

internal sealed class RecordingSessionFactory : IAnalyzerSessionFactory
{
    private int _createCount;
    private readonly ConcurrentDictionary<
        string,
        AnalyzerSemanticOutcome> _outcomes =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _outcomeCounts =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<MethodIdentity, AnalyzerSemanticOutcome>
        _methodOutcomes = new();

    internal ConcurrentDictionary<string, AnalyzerSemanticOutcome> Outcomes =>
        _outcomes;
    internal ConcurrentDictionary<string, int> OutcomeCounts =>
        _outcomeCounts;
    internal int CreateCount => Volatile.Read(ref _createCount);
    internal AnalyzerSemanticOutcome GetNamedOutcome(string name)
    {
        return _methodOutcomes.Single(pair =>
                string.Equals(pair.Key.Name, name, StringComparison.Ordinal))
            .Value;
    }

    internal IEnumerable<AnalyzerSemanticOutcome> GetOutcomes(MethodKind kind)
    {
        return _methodOutcomes
            .Where(pair => pair.Key.Kind == kind)
            .Select(static pair => pair.Value);
    }
    internal AnalyzerSession? Session
    {
        get;
        private set;
    }

    public AnalyzerSession Create(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createCount);
        Session = new AnalyzerSession(
            compilation,
            configuration,
            cancellationToken,
            (method, outcome) =>
            {
                _methodOutcomes.AddOrUpdate(
                    MethodIdentity.Create(method),
                    outcome,
                    (_, current) =>
                        AnalyzerSemanticOutcomes.Combine(current, outcome));
                _outcomeCounts.AddOrUpdate(
                    method.Name,
                    1,
                    static (_, current) => current + 1);
                _outcomes.AddOrUpdate(
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
            return new(
                method.MethodKind,
                method.Name,
                method.DeclaringSyntaxReferences.FirstOrDefault()?.Span.Start ?? -1);
        }
    }
}
