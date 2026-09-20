using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerRelationalSummaryProviderTests
{
    private static readonly CSharpCompilation ClosedFormsCompilation =
        CreateCompilation(
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
        var compilation = ClosedFormsCompilation;
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

    [Test]
    public void NestedImplementationIlDependencyDoesNotDuplicateEvidence()
    {
        using var temporary = new TempDirectory(
            "SharpProof.CompilerRelationalSummaryProvider-");
        var implementationPath = Path.Combine(
            temporary.FullName,
            "Lib.dll");
        var implementation = TestCompilation.Create(
            "NestedImplementationIlLibrary",
            """
            public static class Lib
            {
                public static int Inner(int value) => value;
                public static int Outer(int value) => Inner(value);
            }
            """,
            includeSharpProofReference: false);
        using (var stream = new FileStream(
                   implementationPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
            var emit = implementation.Emit(stream);
            Assert.That(
                emit.Success,
                Is.True,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Select(static diagnostic =>
                        diagnostic.ToString())));
        }

        var compilation = CreateCompilationWithReferences(
            """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;

            public static class Subject
            {
                public static int VerifyOuter(int value)
                {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return Lib.Outer(value);
                }

                public static int VerifyInner(int value)
                {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return Lib.Inner(value);
                }
            }
            """,
            Path.Combine(temporary.FullName, "Subject.cs"),
            MetadataReference.CreateFromFile(implementationPath));
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var lowerer = new CompilerCallableLowerer(
            compilation,
            new IrFactory());
        var preparations = discovery.Targets.Values
            .OrderBy(static candidate => candidate.Method.MetadataName,
                StringComparer.Ordinal)
            .Select(candidate => lowerer.Prepare(candidate))
            .ToArray();

        Assert.That(
            preparations.Select(static preparation => preparation.IsSuccess),
            Is.All.True,
            string.Join(
                ", ",
                preparations.Select(static preparation =>
                    preparation.FailureReason.ToString())) + " / " +
                lowerer.LastImplementationIlAbstention);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lowerer.SummaryEvidenceAuthorities, Has.Length.EqualTo(2));
            Assert.That(
                lowerer.SummaryEvidenceAuthorities.Count(authority =>
                    authority.CallIdentity == "M:Lib.Inner(System.Int32)"),
                Is.EqualTo(1));
        }
        var artifact = CompilerManifestArtifactProducer.Create(
            compilation,
            temporary.FullName,
            "net8.0",
            WorkerFeatureSet.All,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Compilation.SummaryEvidence, Has.Length.EqualTo(2));
            Assert.That(
                artifact.Compilation.SummaryEvidence.Count(row =>
                    row.Origin == CompilerSummaryOrigin.ImplementationIl &&
                    row.CallIdentity == "M:Lib.Inner(System.Int32)"),
                Is.EqualTo(1));
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
        return TestCompilation.Create(
            "CompilerRelationalSummaryProviderTests",
            ("Subject.cs", source));
    }

    private static CSharpCompilation CreateCompilationWithReferences(
        string source,
        string sourcePath,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CSharpCompilation.Create(
            "CompilerRelationalSummaryProviderTests",
            [CSharpSyntaxTree.ParseText(
                SourceText.From(
                    source,
                    Encoding.UTF8,
                    SourceHashAlgorithm.Sha256),
                new CSharpParseOptions(
                    LanguageVersion.CSharp12,
                    preprocessorSymbols: [Contract.ConditionalSymbol]),
                sourcePath)],
            TestMetadataReferences.WithSharpProof.AddRange(additionalReferences),
            TestCompilation.CreateOptions(
                OutputKind.DynamicallyLinkedLibrary,
                NullableContextOptions.Enable));
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }
}
