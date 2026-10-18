using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class SemanticClaimRelativeSyntaxPathObservationTests
{
    private const string Marker = "SP_RELATIVE_SYNTAX_BASELINE_V1 ";

    [Test]
    public void CSharpLocalBaselineUsesActualOwnerAndIndependentChildPaths()
    {
        var first = ObserveLocals("CSharp", "base", "first", "second", selectSecond: false);
        var renamed = ObserveLocals("CSharp", "renamed", "renamedFirst", "renamedSecond", selectSecond: false);
        var other = ObserveLocals("CSharp", "second-role", "first", "second", selectSecond: true);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Fingerprint, Has.Length.EqualTo(64));
            Assert.That(renamed.Fingerprint, Is.EqualTo(first.Fingerprint));
            Assert.That(other.Fingerprint, Is.Not.EqualTo(first.Fingerprint));
            Assert.That(first.OwnerAdmitted, Is.True);
            Assert.That(other.OwnerAdmitted, Is.True);
        }
    }

    [Test]
    public void VisualBasicLocalBaselineRecordsActualPublicOwnerAdmission()
    {
        // Baseline observation deliberately does not assume a VB method reference
        // contains its body, nor assert that distinct VB roles have distinct hashes.
        ObserveLocals("VisualBasic", "base", "first", "second", selectSecond: false);
        ObserveLocals("VisualBasic", "renamed", "renamedFirst", "renamedSecond", selectSecond: false);
        ObserveLocals("VisualBasic", "second-role", "first", "second", selectSecond: true);
    }

    [Test]
    public void CSharpQueryBaselineRetainsActualPublicFingerprintAcrossRenames()
    {
        var first = ObserveQuery("CSharp", "base", "outerValue", "innerValue");
        var renamed = ObserveQuery("CSharp", "renamed", "renamedOuter", "renamedInner");
        Assert.That(first.Fingerprint, Has.Length.EqualTo(64));
        Assert.That(renamed.Fingerprint, Is.EqualTo(first.Fingerprint));
    }

    [Test]
    public void VisualBasicQueryBaselineRecordsAstPathsWithoutClaimingPublicSupport()
    {
        ObserveQuery("VisualBasic", "base", "item", "unused");
        ObserveQuery("VisualBasic", "renamed", "renamedItem", "unused");
    }

    private static FingerprintObservation ObserveLocals(
        string language, string variant, string firstName, string secondName, bool selectSecond)
    {
        System.ArgumentNullException.ThrowIfNull(variant);
        var selected = selectSecond ? secondName : firstName;
        var source = language == "CSharp" ? $$"""
            #define SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static int Root(int value) {
                    int {{firstName}} = value;
                    int {{secondName}} = value + 1;
                    Contract.Ensures({{selected}} >= 0);
                    return {{selected}};
                }
            }
            """ : $$"""
            #Const SHARPPROOF_CONTRACTS = True
            Imports SharpProof.Attributes
            Public Class Subject
                Public Shared Function Root(value As Integer) As Integer
                    Dim {{firstName}} As Integer = value
                    Dim {{secondName}} As Integer = value + 1
                    Contract.Ensures({{selected}} >= 0)
                    Return {{selected}}
                End Function
            End Class
            """;
        var sample = CompileAndRun(language, source, selectSecond ? [8, -2] : [7, -3]);
        var owner = sample.Compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var tree = sample.Compilation.SyntaxTrees.Single();
        var model = sample.Compilation.GetSemanticModel(tree);
        var invocation = ActualEnsures(tree, model);
        var reference = invocation.Arguments.Single().Value.DescendantsAndSelf().OfType<ILocalReferenceOperation>().Single();
        var local = reference.Local;
        var declaration = local.DeclaringSyntaxReferences.Select(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.GetSyntax())).First();
        var ownerReferences = ((IMethodSymbol)local.ContainingSymbol).DeclaringSyntaxReferences
            .Select(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.GetSyntax())).ToArray();
        var admittedOwner = ownerReferences.FirstOrDefault(node =>
            node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.SyntaxTree == declaration.SyntaxTree && node.Span.Contains(declaration.Span)));
        var astContainer = language == "CSharp"
            ? owner.DeclaringSyntaxReferences.Single().GetSyntax()
            : tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.MethodBlockSyntax>().Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(local.ContainingSymbol, owner), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(model.GetEnclosingSymbol(invocation.Syntax.SpanStart), owner), Is.True);
        var astPath = IndependentPath(astContainer, declaration);
        Assert.That(astPath, Is.Not.Null);
        var admittedPath = admittedOwner == null ? null : IndependentPath(admittedOwner, declaration);
        if (admittedOwner != null)
        { Assert.That(admittedPath, Is.Not.Null); }
        var fingerprint = PublicFingerprint(sample.Compilation, invocation, owner, language);
        var observation = fingerprint with { OwnerAdmitted = admittedOwner != null };
        WriteObservation(new
        {
            Scenario = "locals",
            Variant = variant,
            Language = language,
            sample.SourceSha256,
            CompilerErrorCount = 0,
            EmitSucceeded = true,
            RuntimeInputs = new[] { 7, -3 },
            sample.RuntimeOutputs,
            LocalDeclaration = Site(declaration),
            ActualOwnerReferences = ownerReferences.Select(Site).ToArray(),
            ActualOwnerAdmitted = admittedOwner != null,
            AdmittedOwner = Site(admittedOwner),
            AdmittedOwnerChildPath = admittedPath,
            AstContainerRole = language == "CSharp" ? "actual-method-declaration" : "VB-MethodBlock-AST-only",
            AstContainer = Site(astContainer),
            AstChildPath = astPath,
            Fingerprint = observation
        });
        return observation;
    }

    private static FingerprintObservation ObserveQuery(
        string language, string variant, string outerName, string innerName)
    {
        System.ArgumentNullException.ThrowIfNull(innerName);
        System.ArgumentNullException.ThrowIfNull(outerName);
        System.ArgumentNullException.ThrowIfNull(variant);
        var expression = $"(from {outerName} in new[] {{ value }} select " +
            $"from {innerName} in new[] {{ {outerName} }} select {innerName}).Single().Single()";
        var source = language == "CSharp" ? $$"""
            #define SHARPPROOF_CONTRACTS
            using System.Linq;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Root(int value) {
                    Contract.Ensures({{expression}} == value);
                    return {{expression}};
                }
            }
            """ : $$"""
            Imports System.Linq
            Public Class Subject
                Public Shared Function Root(value As Integer) As Integer
                    Return (From {{outerName}} In New Integer(){value} Select {{outerName}}).Single()
                End Function
            End Class
            """;
        var sample = CompileAndRun(language, source, [7, -3]);
        var owner = sample.Compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var tree = sample.Compilation.SyntaxTrees.Single();
        var model = sample.Compilation.GetSemanticModel(tree);
        var actualOwnerReferences = owner.DeclaringSyntaxReferences.Select(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.GetSyntax())).ToArray();
        var astContainer = language == "CSharp" ? actualOwnerReferences.Single()
            : tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.MethodBlockSyntax>().Single();
        var queries = astContainer.DescendantNodes().Where(static node =>
            node is null ? throw new System.ArgumentNullException(nameof(node)) : (node is QueryExpressionSyntax or Microsoft.CodeAnalysis.VisualBasic.Syntax.QueryExpressionSyntax)).ToArray();
        Assert.That(queries, Is.Not.Empty);
        var paths = queries.Select(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (IndependentPath(astContainer, query))).ToArray();
        Assert.That(paths, Is.All.Not.Null);
        bool? queryOwnerAdmitted = language == "CSharp"
            ? queries.All(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (actualOwnerReferences.Any(reference =>
                reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.SyntaxTree == query.SyntaxTree && reference.Span.Contains(query.Span)))))
            : null;
        var fingerprint = language == "CSharp"
            ? PublicFingerprint(sample.Compilation, ActualEnsures(tree, model), owner, language)
            : new FingerprintObservation(null, null, null, null, null, "AST-only; no public VB query support claim");
        fingerprint = fingerprint with { OwnerAdmitted = queryOwnerAdmitted };
        WriteObservation(new
        {
            Scenario = "query",
            Variant = variant,
            Language = language,
            sample.SourceSha256,
            CompilerErrorCount = 0,
            EmitSucceeded = true,
            RuntimeInputs = new[] { 7, -3 },
            sample.RuntimeOutputs,
            ActualOwnerReferences = actualOwnerReferences.Select(Site).ToArray(),
            AstContainerRole = language == "CSharp" ? "actual-method-declaration" : "VB-MethodBlock-AST-only",
            AstContainer = Site(astContainer),
            QuerySites = queries.Select(Site).ToArray(),
            QueryChildPaths = paths,
            Fingerprint = fingerprint
        });
        return fingerprint;
    }

    private static IInvocationOperation ActualEnsures(SyntaxTree tree, SemanticModel model)
    {
        System.ArgumentNullException.ThrowIfNull(model);
        System.ArgumentNullException.ThrowIfNull(tree);
        return tree.GetRoot().DescendantNodes().Where(static node =>
                node is null ? throw new System.ArgumentNullException(nameof(node)) : (node is InvocationExpressionSyntax or Microsoft.CodeAnalysis.VisualBasic.Syntax.InvocationExpressionSyntax))
            .Select(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (model.GetOperation(node))).OfType<IInvocationOperation>()
            .Single(static operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.TargetMethod.Name == "Ensures"));
    }

    private static FingerprintObservation PublicFingerprint(
        Compilation compilation, IInvocationOperation invocation, IMethodSymbol owner, string language)
    {
        System.ArgumentNullException.ThrowIfNull(language);
        System.ArgumentNullException.ThrowIfNull(owner);
        System.ArgumentNullException.ThrowIfNull(invocation);
        System.ArgumentNullException.ThrowIfNull(compilation);
        // Actual semantic symbols only. VB observations never replace an owner
        // declaration reference with a MethodBlock to force admission.
        var route = language == "CSharp" ? "actual-manifest-and-fingerprint-entries"
            : "actual-direct-fingerprint-entry-no-manifest-support-claim";
        try
        {
            var fingerprint = SemanticClaimIdentity.CreateInvocationFingerprint(invocation, owner, owner, usesCompanion: false);
            string? claimId = null;
            if (language == "CSharp")
            {
                var manifest = new ClaimManifestBuilder((CSharpCompilation)compilation, WorkerFeatureSet.Contracts).Build();
                claimId = manifest.Targets[owner].Claims.Single().Entry.ClaimId;
            }
            return new(fingerprint, claimId, null, null, null, route);
        }
        catch (InvalidOperationException exception) when (language == "VisualBasic")
        {
            // Observation only: an actual baseline rejection is preserved in the
            // original TRX marker and requires explicit review before any refactor.
            return new(null, null, exception.GetType().FullName, exception.Message, null, route);
        }
    }

    private static int[]? IndependentPath(SyntaxNode container, SyntaxNode target)
    {
        System.ArgumentNullException.ThrowIfNull(container);
        var pending = new Stack<(SyntaxNode Node, int[] Path)>();
        pending.Push((container, []));
        while (pending.Count != 0)
        {
            var (node, path) = pending.Pop();
            if (SameSite(node, target))
            {
                var reconstructed = container;
                foreach (var ordinal in path)
                { reconstructed = reconstructed.ChildNodes().ElementAt(ordinal); }
                Assert.That(SameSite(reconstructed, target), Is.True);
                return path;
            }
            var children = node.ChildNodes().ToArray();
            for (var ordinal = children.Length - 1; ordinal >= 0; ordinal--)
            { pending.Push((children[ordinal], path.Append(ordinal).ToArray())); }
        }
        return null;
    }

    private static bool SameSite(SyntaxNode left, SyntaxNode right)
    {
        System.ArgumentNullException.ThrowIfNull(right);
        System.ArgumentNullException.ThrowIfNull(left);
        return left.SyntaxTree == right.SyntaxTree && left.RawKind == right.RawKind && left.Span == right.Span;
    }

    private static NodeObservation? Site(SyntaxNode? node)
    {
        return node == null ? null : new(node.RawKind, node.SpanStart, node.Span.Length);
    }

    private static CompiledObservation CompileAndRun(string language, string source, int[] expected)
    {
        System.ArgumentNullException.ThrowIfNull(expected);
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(language);
        var references = TestMetadataReferences.WithSharpProof;
        Compilation compilation = language == "CSharp"
            ? CSharpCompilation.Create("RelativeSyntaxBaseline", [CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12))], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release))
            : VisualBasicCompilation.Create("RelativeSyntaxBaseline", [VisualBasicSyntaxTree.ParseText(source)], references,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("RelativeSyntaxBaseline", isCollectible: true);
        int[] outputs;
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject", throwOnError: true)!.GetMethod("Root")!;
            outputs = new[] { 7, -3 }.Select(value => (int)method.Invoke(null, [value])!).ToArray();
            Assert.That(outputs, Is.EqualTo(expected));
        }
        finally
        { runtime.Unload(); }
        return new(compilation, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))), outputs.ToImmutableArray());
    }

    private static void WriteObservation(object value)
    {
        System.ArgumentNullException.ThrowIfNull(value);
        TestContext.Out.WriteLine(Marker + JsonSerializer.Serialize(value));
    }
    private sealed record NodeObservation(int RawKind, int SpanStart, int SpanLength);
    private sealed record CompiledObservation(Compilation Compilation, string SourceSha256, ImmutableArray<int> RuntimeOutputs)
    {
        public Compilation Compilation { get; init; } = Compilation ?? throw new System.ArgumentNullException(nameof(Compilation));
        public string SourceSha256 { get; init; } = SourceSha256 ?? throw new System.ArgumentNullException(nameof(SourceSha256));
        public ImmutableArray<int> RuntimeOutputs { get; init; } = RuntimeOutputs.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(RuntimeOutputs)) : RuntimeOutputs;
    }
    private sealed record FingerprintObservation(string? Fingerprint, string? ClaimId, string? FailureType,
        string? FailureMessage, bool? OwnerAdmitted, string Route)
    {
        public string? Fingerprint { get; init; } = Fingerprint;
        public string? ClaimId { get; init; } = ClaimId;
        public string? FailureType { get; init; } = FailureType;
        public string? FailureMessage { get; init; } = FailureMessage;
        public bool? OwnerAdmitted { get; init; } = OwnerAdmitted;
        public string Route { get; init; } = Route ?? throw new System.ArgumentNullException(nameof(Route));
    }
}
