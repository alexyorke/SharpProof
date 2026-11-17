using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class BlockerCensusTests
{
    private static readonly string[] ExactRow = ["(lowers exactly)"];

    [Test]
    public void EveryMethodHasAtLeastOneRowAndExactBodiesStandAlone()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var document = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());
        var source = document with
        {
            Methods = [.. document.Methods.Where(static method => method.SourceId == "nodatime").Take(40)]
        };
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(source, cancellationToken, purity: true);
        var declarations = compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(static declaration => OpenSourceCorpusRunner.CorpusMethodId(declaration) != null)
            .ToArray();

        var rows = declarations.Select(declaration => BlockerCensus.Blockers(compilation, declaration, cancellationToken)).ToArray();

        Assert.That(declarations, Has.Length.EqualTo(40));
        Assert.That(rows, Has.All.Not.Empty);
        Assert.That(rows.Where(static row => row.Contains("(lowers exactly)")), Has.All.Length.EqualTo(1));
        Assert.That(rows.Any(static row => row.Contains("(lowers exactly)")), Is.True);
        Assert.That(rows.Any(static row => !row.Contains("(lowers exactly)")), Is.True);
    }

    [Test]
    public void EffectSitesAreReportedOnlyForBodiesThatLowerExactly()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var document = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());
        var source = document with
        {
            Methods = [.. document.Methods.Where(static method => method.SourceId == "humanizer").Take(60)]
        };
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(source, cancellationToken, purity: true);
        var exact = compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(static declaration => OpenSourceCorpusRunner.CorpusMethodId(declaration) != null)
            .Where(declaration => BlockerCensus.Blockers(compilation, declaration, cancellationToken).Contains("(lowers exactly)"))
            .ToArray();

        var rows = exact.Select(declaration => BlockerCensus.EffectSites(compilation, declaration, cancellationToken)).ToArray();

        Assert.That(exact, Is.Not.Empty);
        Assert.That(rows, Has.All.Not.Empty);
        Assert.That(rows.Where(static row => row.Contains("(no purity-relevant effect site)")), Has.All.Length.EqualTo(1));
        Assert.That(rows.SelectMany(static row => row), Has.None.EqualTo("(does not lower exactly)"));
    }

    // The census lowers a body as the compiler collector does, so a struct
    // member reads `this` as an input, and a target-typed `new(...)` of a
    // modeled struct builds a value.
    [TestCase("public Span Plus(Span other) => this + other;")]
    [TestCase("public bool Same(Span other) => this == other;")]
    [TestCase("public static Span FromTicks(long ticks) => new(ticks * 100);")]
    [TestCase("public static Span Sum(long a, long b) { return new(a, b); }")]
    public void StructMembersLowerExactly(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var source = """
            public readonly struct Span
            {
                private readonly long ticks;
                public Span(long ticks) { this.ticks = ticks; }
                internal Span(long a, long b) { ticks = a + b; }
                public static Span operator +(Span left, Span right) => new Span(left.ticks + right.ticks);
                public static bool operator ==(Span left, Span right) => left.ticks == right.ticks;
                public static bool operator !=(Span left, Span right) => left.ticks != right.ticks;
                public override bool Equals(object? other) => false;
                public override int GetHashCode() => 0;
            """ + "\n" + member + "\n}";
        var compilation = CSharpCompilation.Create("census", [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.CurrentContext.CancellationToken)],
            TrustedPlatformAssemblyPaths.Get().Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var declaration = compilation.SyntaxTrees[0].GetRoot(TestContext.CurrentContext.CancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Last();

        Assert.That(BlockerCensus.Blockers(compilation, declaration, TestContext.CurrentContext.CancellationToken), Is.EqualTo(ExactRow));
    }
}
