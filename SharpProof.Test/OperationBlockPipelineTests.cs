using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class OperationBlockPipelineTests
{
    [Test]
    public async Task OperationBlockPipeline_ReportsEachMethodFeatureDiagnosticOnce()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class PipelineTarget
            {
                [EnforcePure]
                public void Purity() => Console.WriteLine("purity");

                [ZeroAllocations]
                public object Allocation() => new object();

                [AllowedCapabilities(SharpProofCapability.None)]
                public void Capability() => Console.WriteLine("capability");

                [Ensures("result > 0")]
                public int Postcondition() => -1;

                [ExpectedComplexity(ComplexityKind.Linear)]
                public int Complexity(int n)
                {
                    var sum = 0;
                    for (var i = 0; i < n; i++)
                    {
                        for (var j = 0; j < n; j++)
                        {
                            sum += i + j;
                        }
                    }

                    return sum;
                }

                [Requires("result > 0")]
                public void Precondition(int value)
                {
                }

                [DoesNotThrow]
                public void Exceptions()
                {
                    throw new InvalidOperationException();
                }
            }
            """,
            globalOptions: ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure", "false"),
            concurrentAnalysis: true,
            compilationName: "OperationBlockFeatureDiagnostics");

        var expectedIds = new[]
        {
            SharpProofDiagnostics.PurityNotVerifiedId,
            SharpProofDiagnostics.AllocationInZeroAllocationMethodId,
            SharpProofDiagnostics.CapabilityViolationId,
            SharpProofDiagnostics.EnsuresNotProvenId,
            SharpProofDiagnostics.ComplexityExceededId,
            SharpProofDiagnostics.RequiresUnsupportedId,
            SharpProofDiagnostics.ExceptionContractViolationId
        };

        Assert.That(diagnostics, Has.Length.EqualTo(expectedIds.Length));
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EquivalentTo(expectedIds));
        foreach (var diagnosticId in expectedIds)
            Assert.That(diagnostics.Count(diagnostic => diagnostic.Id == diagnosticId), Is.EqualTo(1), diagnosticId);
    }

    [Test]
    public async Task OperationBlockPipeline_GuardedThrowReportsPurityAndExceptionDiagnostics()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class PipelineTarget
            {
                [EnforcePure]
                public int TestMethod(string text)
                {
                    if (text == null)
                    {
                        throw new ArgumentNullException(nameof(text));
                    }

                    return text.Length;
                }
            }
            """,
            globalOptions: ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_report_exceptions", "true"),
            concurrentAnalysis: true,
            compilationName: "OperationBlockGuardedThrow");

        Assert.That(
            diagnostics.Count(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.EqualTo(1));
        Assert.That(
            diagnostics.Count(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId),
            Is.EqualTo(1));
    }

    [Test]
    public async Task MethodBodyAnalysisState_CachesBodyFactsAndSymbolicQueries()
    {
        const string source = """
                              using System;

                              public sealed class PipelineTarget
                              {
                                  public int Analyze(int n)
                                  {
                                      Console.WriteLine(n);
                                      for (var i = 0; i < n; i++)
                                      {
                                          n += i;
                                      }

                                      return n;
                                  }
                              }
                              """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "src/PipelineTarget.cs");
        var compilation = CSharpCompilation.Create(
            "MethodBodyAnalysisStateCaching",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences()
                .Add(MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var declaration = syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var methodSymbol = (IMethodSymbol)semanticModel.GetDeclaredSymbol(declaration)!;
        var rootOperation = MethodBodyOperationResolver.GetMethodBodyRootOperation(
            declaration,
            semanticModel,
            CancellationToken.None)!;
        var state = new MethodBodyAnalysisState(
            MethodAnalysisRequest.Create(
                methodSymbol,
                declaration,
                semanticModel,
                ImmutableArray.Create(rootOperation),
                CancellationToken.None),
            CancellationToken.None);

        var firstCapability = state.GetCapabilityOutcome(CancellationToken.None);
        var secondCapability = state.GetCapabilityOutcome(CancellationToken.None);
        var firstComplexity = state.GetComplexityOutcome(CancellationToken.None);
        var secondComplexity = state.GetComplexityOutcome(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(secondCapability, Is.SameAs(firstCapability));
            Assert.That(secondComplexity, Is.SameAs(firstComplexity));
            Assert.That(firstCapability.IsSuccess, Is.True);
            Assert.That(firstComplexity.IsSuccess, Is.True);
            Assert.That(state.GetSymbolicQueryExecutionCount("capability"), Is.EqualTo(1));
            Assert.That(state.GetSymbolicQueryExecutionCount("complexity"), Is.EqualTo(1));
            Assert.That(state.Snapshot.SemanticFacts.OperationBlockCount, Is.EqualTo(1));
            Assert.That(state.Snapshot.SemanticFacts.HasRootOperation, Is.True);
            Assert.That(state.Snapshot.SemanticFacts.VisibleOperationCount, Is.GreaterThan(0));
            Assert.That(state.Snapshot.SemanticFacts.ReturnOperationCount, Is.EqualTo(1));
        });

        using (var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default))
        {
            var failedProof = state.QueryService.TryProveAtSyntaxNode(
                semanticModel,
                declaration,
                " ",
                smtAnalysis,
                false,
                CancellationToken.None);
            var conservativeProof = AnalyzerSymbolicQueryBoundary.ResolveProof(
                failedProof,
                " ",
                CancellationToken.None);

            Assert.That(failedProof.IsSuccess, Is.False);
            Assert.That(conservativeProof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
            Assert.That(conservativeProof.Reason, Does.Contain(SymbolicErrorCodes.InvalidRequest));
        }

        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFactory = new ManualResetEventSlim(false);
        var factoryExecutions = 0;
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => state.GetOrCreateSymbolicQueryResult(
                "shared-test-query",
                () =>
                {
                    Interlocked.Increment(ref factoryExecutions);
                    factoryEntered.TrySetResult();
                    if (!releaseFactory.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Timed out waiting to release the shared query factory.");
                    return new CachedMarker();
                })))
            .ToArray();

        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        releaseFactory.Set();
        var results = await Task.WhenAll(tasks);

        Assert.That(factoryExecutions, Is.EqualTo(1));
        Assert.That(results, Has.All.SameAs(results[0]));
        Assert.That(state.GetSymbolicQueryExecutionCount("shared-test-query"), Is.EqualTo(1));
    }

    [Test]
    public void SyntaxFallbackManifest_CoversOnlyDeclarationsWithoutMethodOperationBlocks()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            """
            public abstract class PipelineTarget
            {
                public abstract int Bodyless();

                public int MethodBody()
                {
                    return 1;
                }

                public int ExpressionProperty => 1;

                public int Outer()
                {
                    int Local() => 1;
                    return Local();
                }

                public int Accessors
                {
                    get { return 1; }
                    set;
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.Preview));
        var root = syntaxTree.GetRoot();
        var bodyless = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Bodyless");
        var methodBody = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "MethodBody");
        var expressionProperty = root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(property => property.Identifier.ValueText == "ExpressionProperty");
        var localFunction = root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var accessors = root.DescendantNodes().OfType<AccessorDeclarationSyntax>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(AnalyzerFeaturePipeline.RequiresSyntaxFallback(bodyless), Is.True);
            Assert.That(AnalyzerFeaturePipeline.RequiresSyntaxFallback(methodBody), Is.False);
            Assert.That(AnalyzerFeaturePipeline.RequiresSyntaxFallback(expressionProperty), Is.True);
            Assert.That(AnalyzerFeaturePipeline.RequiresSyntaxFallback(localFunction), Is.True);
            Assert.That(AnalyzerFeaturePipeline.RequiresSyntaxFallback(accessors[0]), Is.False);
            Assert.That(AnalyzerFeaturePipeline.RequiresSyntaxFallback(accessors[1]), Is.True);
        });
    }

    private sealed class CachedMarker
    {
    }
}
