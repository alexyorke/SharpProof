using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class ExceptionFlowResultGraphTests
{
    [Test]
    public void AnalyzeMethod_NormalizedGraphIsDeterministicAndPreservesHazardOrder()
    {
        const string source = """
                              using System;
                              class C
                              {
                                  static void Callee() => throw new InvalidOperationException();
                                  static int M(int divisor)
                                  {
                                      if (divisor == 0)
                                          throw new ArgumentException();
                                      Callee();
                                      return 10 / divisor;
                                  }
                              }
                              """;

        var first = Analyze(source);
        var second = Analyze(source);
        var firstGraph = Normalize(first);
        var secondGraph = Normalize(second);

        Assert.That(secondGraph, Is.EqualTo(firstGraph));
        Assert.That(first.Sites.Select(static site => site.ExceptionType), Is.EqualTo(new[]
        {
            "System.ArgumentException",
            "System.InvalidOperationException"
        }));
        Assert.That(first.RawHazards.Any(static hazard => !string.IsNullOrWhiteSpace(hazard.TriggerCondition)),
            Is.True);
    }

    private static ExceptionFlowEngine.ExceptionFlowResult Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "ExceptionFlowGraph.cs");
        var compilation = CSharpCompilation.Create(
            "ExceptionFlowGraph",
            new[] { tree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static declaration => declaration.Identifier.ValueText == "M");
        var methodSymbol = (IMethodSymbol)model.GetDeclaredSymbol(method)!;
        using var smt = new SmtAnalysisService(SmtAnalysisOptions.Default);
        return ExceptionFlowEngine.AnalyzeMethod(
            SymbolicMethodAnalysisInput.Create(methodSymbol, method, model),
            CancellationToken.None,
            EffectSummaryCatalog.Empty,
            smt,
            SharpProofAttributeIdentityPolicy.Create(ImmutableHashSet<string>.Empty));
    }

    private static string Normalize(ExceptionFlowEngine.ExceptionFlowResult result) => JsonSerializer.Serialize(new
    {
        Sites = result.Sites.Select(static site => new
        {
            Span = new[] { site.Site.SpanStart, site.Site.Span.End },
            Method = site.Method.OriginalDefinition.ToDisplayString(),
            Type = site.ExceptionType,
            site.Category,
            site.Source,
            site.ExceptionSymbol,
            Edges = site.Edges.Select(static edge => new
            {
                edge.ExceptionType,
                edge.Category,
                edge.SourcePath,
                edge.CallChain,
                edge.CalleeIdentity,
                edge.Depth
            })
        }),
        Hazards = result.RawHazards.Select(static hazard => new
        {
            hazard.SpanStart,
            hazard.SpanEnd,
            Kind = hazard.Kind.ToString(),
            Status = hazard.Status.ToString(),
            hazard.StatusReason,
            hazard.ExceptionType,
            hazard.Category,
            hazard.TriggerCondition,
            Unknown = hazard.Proof.UnknownReason.ToString(),
            Truncated = hazard.AnalysisTruncation.IsTruncated
        }),
        Evidence = new
        {
            result.Evidence.Types,
            Categories = result.Evidence.FormatCategories(),
            Sources = result.Evidence.FormatSources(),
            Edges = result.Evidence.FormatEdges()
        }
    });
}
