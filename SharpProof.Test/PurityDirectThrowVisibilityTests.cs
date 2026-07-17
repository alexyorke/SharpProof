using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Test;

[TestFixture]
public sealed class PurityDirectThrowVisibilityTests
{
    public static IEnumerable<TestCaseData> DirectThrowCases()
    {
        yield return Case("BlockMethod", CallableShape.Method, """
            [EnforcePure]
            public int {|SP0002:Target|}()
            {
                throw new InvalidOperationException();
            }
            """, "throw new InvalidOperationException();");
        yield return Case("ExpressionMethod", CallableShape.Method, """
            [EnforcePure]
            public int {|SP0002:Target|}() => throw new InvalidOperationException();
            """, "throw new InvalidOperationException()");
        yield return Case("BlockLocalFunction", CallableShape.LocalFunction, """
            [EnforcePure]
            public int {|SP0002:Target|}()
            {
                int Local()
                {
                    throw new InvalidOperationException();
                }
                return Local();
            }
            """, "throw new InvalidOperationException();");
        yield return Case("ExpressionLocalFunction", CallableShape.LocalFunction, """
            [EnforcePure]
            public int {|SP0002:Target|}()
            {
                int Local() => throw new InvalidOperationException();
                return Local();
            }
            """, "throw new InvalidOperationException()");
        yield return Case("SimpleLambdaWithParameter", CallableShape.SimpleLambda, """
            [EnforcePure]
            public int {|SP0002:Target|}()
            {
                Func<int, int> callback = value => throw new InvalidOperationException();
                return callback(1);
            }
            """, "throw new InvalidOperationException()");
        yield return Case("ParenthesizedLambda", CallableShape.ParenthesizedLambda, """
            [EnforcePure]
            public int {|SP0002:Target|}()
            {
                Func<int, int> callback = (int value) => throw new InvalidOperationException();
                return callback(1);
            }
            """, "throw new InvalidOperationException()");
        yield return Case("AnonymousMethod", CallableShape.AnonymousMethod, """
            [EnforcePure]
            public int {|SP0002:Target|}()
            {
                Func<int, int> callback = delegate(int value)
                {
                    throw new InvalidOperationException();
                };
                return callback(1);
            }
            """, "throw new InvalidOperationException();");
        yield return Case("DirectThrowExpression", CallableShape.Method, """
            [EnforcePure]
            public int {|SP0002:Target|}(int value)
            {
                return value >= 0 ? value : throw new InvalidOperationException();
            }
            """, "throw new InvalidOperationException()");
    }

    [TestCaseSource(nameof(DirectThrowCases))]
    public void VisibleDescendants_ExposeDirectThrowWithExactSyntax(DirectThrowCase testCase)
    {
        var (source, _) = AnalyzerTestHost.StripRequiredSp0002Markup(testCase.MarkedSource);
        var context = AnalyzerTestHost.CreateSourceContext(source, nameof(PurityDirectThrowVisibilityTests));
        var callableSyntax = FindCallableSyntax(context.Root, testCase.Shape);
        var callableOperation = context.SemanticModel.GetOperation(callableSyntax);

        Assert.That(callableOperation, Is.Not.Null);
        var throwOperations = ExecutionVisibility.VisibleDescendants(callableOperation!).OfType<IThrowOperation>()
            .ToArray();
        Assert.That(throwOperations, Has.Length.EqualTo(1));
        Assert.That(throwOperations[0].Syntax.ToString(), Is.EqualTo(testCase.ExpectedThrowSyntax));
    }

    [TestCaseSource(nameof(DirectThrowCases))]
    public async Task Analyzer_PreservesDirectThrowSpanAndEvidence(DirectThrowCase testCase)
    {
        var (_, diagnostic) = await AnalyzerTestHost.AssertSingleSp0002Async(
            testCase.MarkedSource,
            analyzerFeatures: AnalyzerFeatures.Purity);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo("throw"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty],
                Is.EqualTo("ThrowOperationPurityRule"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Throw"));
        });
    }

    [Test]
    public async Task NestedCallableThrow_IsExcludedFromOuterVisibilityAndPurity()
    {
        const string source = """
            using System;
            using SharpProof.Attributes;

            public sealed class TestClass
            {
                [EnforcePure]
                public int Target()
                {
                    Func<int> callback = () => throw new InvalidOperationException();
                    return 42;
                }
            }
            """;
        var context = AnalyzerTestHost.CreateSourceContext(source, nameof(NestedCallableThrow_IsExcludedFromOuterVisibilityAndPurity));
        var method = context.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var lambda = context.Root.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>().Single();
        var methodOperation = context.SemanticModel.GetOperation(method)!;
        var lambdaOperation = context.SemanticModel.GetOperation(lambda)!;

        Assert.Multiple(() =>
        {
            Assert.That(ExecutionVisibility.VisibleDescendants(methodOperation).OfType<IThrowOperation>(), Is.Empty);
            Assert.That(ExecutionVisibility.VisibleDescendants(lambdaOperation).OfType<IThrowOperation>(), Has.Exactly(1).Items);
        });
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, analyzerFeatures: AnalyzerFeatures.Purity);
        Assert.That(diagnostics, Is.Empty);
    }

    private static TestCaseData Case(
        string name,
        CallableShape shape,
        string memberSource,
        string expectedThrowSyntax)
    {
        var source = $$"""
            using System;
            using SharpProof.Attributes;

            public sealed class TestClass
            {
            {{memberSource}}
            }
            """;
        return new TestCaseData(new DirectThrowCase(source, shape, expectedThrowSyntax)).SetName(
            "DirectThrow_" + name);
    }

    private static SyntaxNode FindCallableSyntax(SyntaxNode root, CallableShape shape)
    {
        return shape switch
        {
            CallableShape.Method => root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "Target"),
            CallableShape.LocalFunction => root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single(),
            CallableShape.SimpleLambda => root.DescendantNodes().OfType<SimpleLambdaExpressionSyntax>().Single(),
            CallableShape.ParenthesizedLambda => root.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>()
                .Single(),
            CallableShape.AnonymousMethod => root.DescendantNodes().OfType<AnonymousMethodExpressionSyntax>().Single(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };
    }

    public sealed record DirectThrowCase(
        string MarkedSource,
        CallableShape Shape,
        string ExpectedThrowSyntax);

    public enum CallableShape
    {
        Method,
        LocalFunction,
        SimpleLambda,
        ParenthesizedLambda,
        AnonymousMethod
    }
}
