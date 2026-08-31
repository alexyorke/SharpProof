using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerRelationalSummaryProviderTests
{
    [Test]
    public void LongSourceDependencyChainAbstainsAtResourceLimit()
    {
        const int dependencyCount = 65;
        var methods = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, dependencyCount).Select(index =>
                index == dependencyCount - 1
                    ? $"internal static int Dependency{index}(int value) " +
                      "=> value;"
                    : $"internal static int Dependency{index}(int value) " +
                      $"=> Dependency{index + 1}(value);"));
        var compilation = CreateCompilation(
            $$"""
            internal static class Subject
            {
                {{methods}}
                internal static int Verify(int value) => Dependency0(value);
            }
            """);
        var factory = new IrFactory();
        var provider = new CompilerRelationalSummaryProvider(
            compilation,
            factory,
            new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation));
        var call = GetCall(
            compilation,
            factory,
            "Verify",
            "Dependency0");

        var prepared = provider.TryGet(
            call.Method,
            call.Member,
            CancellationToken.None,
            out var summary);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared, Is.False);
            Assert.That(summary, Is.Null);
            Assert.That(
                provider.LastImplementationIlAbstention,
                Is.EqualTo(
                    CompilerImplementationIlAbstentionReason
                        .SummaryResourceLimit));
        }
    }

    [TestCase("VerifyInt", "VerifyLong")]
    [TestCase("VerifyLong", "VerifyInt")]
    public void ClosedFormsNestedInsideGenericOuterHaveIndependentCacheEntries(
        string firstMethod,
        string secondMethod)
    {
        var compilation = CreateCompilation(
            """
            using SharpProof.Attributes;

            internal static class Outer<T> {
                internal static class Inner {
                    internal static int F(int value) => value;
                }
            }

            internal static class Subject {
                internal static int VerifyInt(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return Outer<int>.Inner.F(value);
                }

                internal static int VerifyLong(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return Outer<long>.Inner.F(value);
                }
            }
            """);
        var factory = new IrFactory();
        var provider = new CompilerRelationalSummaryProvider(
            compilation,
            factory,
            new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation));
        var firstCall = GetCall(
            compilation,
            factory,
            firstMethod);
        var secondCall = GetCall(
            compilation,
            factory,
            secondMethod);
        var openMethod = compilation.GetTypeByMetadataName("Outer`1")!
            .GetTypeMembers("Inner")
            .Single()
            .GetMembers("F")
            .OfType<IMethodSymbol>()
            .Single();

        Assert.That(
            provider.IsAdmissiblePureCall(firstCall.Method),
            Is.True);
        Assert.That(
            provider.IsAdmissiblePureCall(secondCall.Method),
            Is.True);
        var firstPrepared = provider.TryGet(
            firstCall.Method,
            firstCall.Member,
            CancellationToken.None,
            out var firstSummary);
        var secondPrepared = provider.TryGet(
            secondCall.Method,
            secondCall.Member,
            CancellationToken.None,
            out var secondSummary);

        Assert.That(
            firstPrepared,
            Is.True,
            "The first closed form did not prepare.");
        Assert.That(
            secondPrepared,
            Is.True,
            "The second closed form did not prepare.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                provider.IsAdmissiblePureCall(openMethod),
                Is.False);
            Assert.That(
                firstSummary!.Signature.Member,
                Is.EqualTo(firstCall.Member));
            Assert.That(
                secondSummary!.Signature.Member,
                Is.EqualTo(secondCall.Member));
            Assert.That(
                provider.SummaryEvidenceAuthorities,
                Has.Length.EqualTo(2));
            Assert.That(
                provider.SummaryEvidenceAuthorities
                    .Select(static authority => authority.CallIdentity)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(2));
        }
    }

    private static (IMethodSymbol Method, IrMemberId Member) GetCall(
        CSharpCompilation compilation,
        IrFactory factory,
        string callerName,
        string calledMethodName = "F")
    {
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == callerName);
        var syntax = declaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString()
                .EndsWith(calledMethodName, StringComparison.Ordinal));
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, tree);
        var invocation = (IInvocationOperation)model.GetOperation(syntax)!;
        var lowered = new RoslynOperationLowerer(
            factory,
            static _ => true).Lower(invocation);
        var opaque = (IrOpaqueTerm)lowered.Term;
        Assert.That(opaque.Receiver, Is.Null);
        return (invocation.TargetMethod, opaque.Member);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parse = new CSharpParseOptions(
            LanguageVersion.CSharp12,
            preprocessorSymbols: [Contract.ConditionalSymbol]);
        var compilation = CSharpCompilation.Create(
            "CompilerRelationalSummaryProviderTests",
            [CSharpSyntaxTree.ParseText(source, parse, "Subject.cs")],
            WorkerTestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static error => error.ToString())));
        return compilation;
    }
}
