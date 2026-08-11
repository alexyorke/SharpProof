using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SharpProofAnalyzer : DiagnosticAnalyzer
{
    private readonly SharpProofAnalyzerEngine _engine;

    public SharpProofAnalyzer()
        : this(DefaultAnalyzerSessionFactory.Instance)
    {
    }

    internal SharpProofAnalyzer(IAnalyzerSessionFactory sessionFactory)
    {
        _engine = new SharpProofAnalyzerEngine(sessionFactory);
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        SharpProofAnalyzerEngine.SupportedDiagnostics;

    public override void Initialize(AnalysisContext context)
    {
        context = ArgumentNullGuard.NotNull(context, nameof(context));
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze |
            GeneratedCodeAnalysisFlags.ReportDiagnostics);
        _engine.RegisterActions(context);
    }
}
