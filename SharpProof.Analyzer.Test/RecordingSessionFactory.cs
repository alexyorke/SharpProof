using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

internal sealed class RecordingSessionFactory : IAnalyzerSessionFactory
{
    private readonly ConcurrentDictionary<
        string,
        AnalyzerSemanticOutcome> _outcomes =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _outcomeCounts =
        new(StringComparer.Ordinal);

    internal ConcurrentDictionary<string, AnalyzerSemanticOutcome> Outcomes =>
        _outcomes;
    internal ConcurrentDictionary<string, int> OutcomeCounts =>
        _outcomeCounts;
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
        Session = new AnalyzerSession(
            compilation,
            configuration,
            cancellationToken,
            (method, outcome) =>
            {
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
}
