using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerTotalCallRetirementTests
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
    public async Task ClosedGenericSourceFramesRemainIndependent()
    {
        foreach (var target in new ClaimManifestBuilder(ClosedFormsCompilation).Build().Targets.Values)
        {
            var preparation = new CompilerCallableLowerer(ClosedFormsCompilation, new SharpProof.Ir.IrFactory()).Prepare(target);
            await AssertProvesAsync(preparation);
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ElidedAndEmittedContractsDoNotRequireRelationalSummaries(bool emitted, bool resultPlaceholder)
    {
        var source = $$"""
            using SharpProof.Attributes;
            internal static class Subject {
                private static int Read(int value) {
                    Contract.Requires(value >= 0);
                    Contract.Ensures({{(resultPlaceholder ? "Contract.Result<int>() == value" : "value >= 0")}});
                    Contract.Assume(value >= 0);
                    return value;
                }
                internal static int Verify(int value) {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() == value);
                    return Read(value);
                }
            }
            """;
        var parse = new CSharpParseOptions(LanguageVersion.CSharp12,
            preprocessorSymbols: emitted ? ["SHARPPROOF_CONTRACTS"] : []);
        var compilation = CSharpCompilation.Create("TotalContractCallTests",
            [CSharpSyntaxTree.ParseText(source, parse, "Subject.cs")], TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary, NullableContextOptions.Enable));
        TestCompilation.AssertNoErrors(compilation);
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single(target => target.Method.Name == "Verify");
        var preparation = new CompilerCallableLowerer(compilation, new SharpProof.Ir.IrFactory()).Prepare(target);
        await AssertProvesAsync(preparation, emitted && resultPlaceholder ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task DeepSourceExpansionCannotProveFromAnApproximation()
    {
        var methods = string.Join(Environment.NewLine, Enumerable.Range(0, 65).Select(index =>
            index == 64 ? $"private static int Dependency{index}(int value) => value;" :
            $"private static int Dependency{index}(int value) => Dependency{index + 1}(value);"));
        var compilation = CreateCompilation($$"""
            using SharpProof.Attributes;
            internal static class Subject {
                {{methods}}
                internal static int Verify(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return Dependency0(value);
                }
            }
            """);
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        var preparation = new CompilerCallableLowerer(compilation, new SharpProof.Ir.IrFactory()).Prepare(target);
        var results = new List<WorkerClaimResult>();
        await TotalCallableVerifier.VerifyAsync(preparation, new WorkerBudgets(),
            check => results.Add(CallableClaimResultAssembler.FromTotal(preparation, check)), CancellationToken.None);
        Assert.That(results.All(result => result.Outcome != WorkerClaimOutcome.Proven), Is.True);
    }

    private static async Task AssertProvesAsync(CompilerCallablePreparation preparation, WorkerClaimOutcome expected = WorkerClaimOutcome.Proven)
    {
        Assert.That(preparation.Total, Is.Not.Null);
        var results = new Dictionary<string, WorkerClaimResult>(StringComparer.Ordinal);
        await TotalCallableVerifier.VerifyAsync(preparation, new WorkerBudgets(),
            check => results[check.ClaimId] = CallableClaimResultAssembler.FromTotal(preparation, check), CancellationToken.None);
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results.Values.Single().Outcome, Is.EqualTo(expected), results.Values.Single().Reason.ToString());
    }

    [Test]
    public async Task DuplicateImplementationReferenceUsesTotalIlEvidence()
    {
        using var temporary = new TempDirectory(
            "SharpProof.CompilerRelationalSummaryProvider-duplicate-");
        var implementationPath = Path.Combine(
            temporary.FullName,
            "Lib.dll");
        var duplicatePath = Path.Combine(
            temporary.FullName,
            "Lib-copy.dll");
        var implementation = TestCompilation.Create(
            "DuplicateImplementationIlLibrary",
            """
            public static class Lib
            {
                public static int Identity(int value) => value;
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
        File.Copy(implementationPath, duplicatePath);
        var firstReference = MetadataReference.CreateFromFile(implementationPath,
            new MetadataReferenceProperties(aliases: ["first"]));
        var secondReference = MetadataReference.CreateFromFile(duplicatePath,
            new MetadataReferenceProperties(aliases: ["second"]));

        var compilation = CreateCompilationWithReferences(
            """
            #undef SHARPPROOF_CONTRACTS
            extern alias first;
            extern alias second;
            using SharpProof.Attributes;

            public static class Subject
            {
                public static int Verify(int value)
                {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return first::Lib.Identity(value);
                }
                public static int VerifySecond(int value)
                {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return second::Lib.Identity(value);
                }
            }
            """,
            Path.Combine(temporary.FullName, "Subject.cs"),
            firstReference,
            secondReference);
        var method = GetCall(compilation, "Verify", "Identity");
        var resolution = new CompilerMetadataResolution.MetadataResolutionContext(compilation);
        for (var lookup = 0; lookup < 2; lookup++)
        {
            Assert.That(resolution.TryFindReference(method.ContainingAssembly.Identity, method.ContainingModule.Name,
                CancellationToken.None, out _, out _, out var selectedPath), Is.True);
            Assert.That(new[] { implementationPath, duplicatePath }, Does.Contain(selectedPath));
        }
        var discovery = new ClaimManifestBuilder(compilation).Build();

        var artifact = CompilerManifestArtifactProducer.Create(
            compilation,
            temporary.FullName,
            "net8.0",
            WorkerFeatureSet.All,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);

        foreach (var preparation in CompilerManifestArtifactJson.DecodeCallables(artifact))
        {
            Assert.That(preparation.Total, Is.Not.Null);
            Assert.That(preparation.Body, Is.Null);
            await AssertProvesAsync(preparation);
        }
    }

    private static IMethodSymbol GetCall(CSharpCompilation compilation, string callerName, string calledMethodName)
    {
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == callerName);
        var syntax = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString().EndsWith(calledMethodName, StringComparison.Ordinal));
        var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree);
        return ((IInvocationOperation)model.GetOperation(syntax)!).TargetMethod;
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
