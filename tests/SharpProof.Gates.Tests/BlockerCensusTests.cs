using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class BlockerCensusTests
{
    private static readonly string[] ExactRow = ["(lowers exactly)"];

    [Test]
    public void NodaTimeCachedEmptyParseResultHasImmutableStorage()
    {
        var token = TestContext.CurrentContext.CancellationToken;
        var document = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());
        var source = document with
        {
            Methods = [.. document.Methods.Where(static method => method.SourceId == "nodatime").Take(1)]
        };
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(source, token, purity: true);
        var type = compilation.GetTypeByMetadataName("NodaTime.Text.ParseResult`1")!;
        var property = type.GetMembers("ValueStringEmpty").OfType<IPropertySymbol>().Single();
        var field = CSharpOperationSemantics.StaticGetOnlyAutoPropertyField(property);
        Assert.That(field, Is.Not.Null);
        Assert.That(TypeInitialization.Classify(compilation, type, token), Is.EqualTo(InitializationEffect.FreshValues));
        Assert.That(TypeInitialization.HoldsImmutableValue(compilation, field!, token), Is.True);
    }

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

    // Labels name what keeps the body from lowering, not where Roslyn put it.
    [TestCase("public static async System.Threading.Tasks.Task M() { await System.Threading.Tasks.Task.Yield(); }", "ContractBinding: UnsupportedTarget: async")]
    [TestCase("public static void M(ref System.DateTime d) { }", "ContractBinding: UnsupportedTarget: Ref parameter of System_DateTime")]
    [TestCase("public static void M(out int value, ref System.DateTime d) { value = 0; }", "ContractBinding: UnsupportedTarget: Ref parameter of System_DateTime")]
    [TestCase("public static int M(int x) { var text = $\"{x:X}\"; return text.Length; }", "UnsupportedOperationKind: InterpolatedString of System_String")]
    public void LabelsNameTheBlockingConstruct(string member, string label)
    {
        ArgumentNullException.ThrowIfNull(member);
        var (compilation, declaration) = Compile("public static class C { " + member + " }");
        var labels = BlockerCensus.Blockers(compilation, declaration, TestContext.CurrentContext.CancellationToken);
        Assert.That(labels, Is.EqualTo(new[] { label }), string.Join(" ; ", labels));
    }

    // A sole call into source code is charged to what refuses the call: the
    // callee type's initializer, or what blocks the callee's own body.
    [TestCase("""
        public static class D
        {
            private static readonly int Seed = System.Environment.ProcessorCount;
            public static int Leaf(int x) { return x + Seed; }
        }
        public static class C { public static int M(int x) { return D.Leaf(x); } }
        """, "(callee type initializer)")]
    public void SourceCallBlockersAreChargedToTheirRootCause(string source, string cause)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (compilation, declaration) = Compile(source);
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var labels = BlockerCensus.Blockers(compilation, declaration, cancellationToken);
        Assert.That(labels.Single(), Does.Contain(": call source "), string.Join(" ; ", labels));
        Assert.That(BlockerCensus.RootCause(compilation, declaration, cancellationToken), Is.EqualTo(cause));
    }

    private static (CSharpCompilation Compilation, MethodDeclarationSyntax Declaration) Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = CSharpCompilation.Create("census", [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.CurrentContext.CancellationToken)],
            TrustedPlatformAssemblyPaths.Get().Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var declaration = compilation.SyntaxTrees[0].GetRoot(TestContext.CurrentContext.CancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Last();
        return (compilation, declaration);
    }

    // Floating-point values are opaque: an integer converted into one, an
    // argument of a source helper and a field store lower
    // without modeling the value. The last method is the one tabulated.
    [TestCase("public static System.TimeSpan Seconds(double s) => System.TimeSpan.FromSeconds(s); public static System.TimeSpan M(int s) { return Seconds((double)s); }")]
    [TestCase("public static decimal M(long x) { return x; }")]
    [TestCase("public sealed class Box { public double Value; } public static void M(Box b, int v) { b.Value = v; }")]
    public void FloatingValuesLowerExactly(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var (compilation, declaration) = Compile("public static class C { " + member + " }");
        var labels = BlockerCensus.Blockers(compilation, declaration, TestContext.CurrentContext.CancellationToken);
        Assert.That(labels, Is.EqualTo(ExactRow), string.Join(" ; ", labels));
    }

    // Conversions out of floating point and arithmetic still abstain.
    [TestCase("public static int M(double x) { return checked((int)x); }")]
    [TestCase("public static decimal M(decimal x) { return x + 1m; }")]
    [TestCase("public static double M(double x) { return x * 2.0; }")]
    public void FloatingFaultsStillAbstain(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var (compilation, declaration) = Compile("public static class C { " + member + " }");
        Assert.That(BlockerCensus.Blockers(compilation, declaration, TestContext.CurrentContext.CancellationToken), Is.Not.EqualTo(ExactRow));
    }

    // The census lowers a body as the compiler collector does, so a struct
    // member reads `this` as an input, and a target-typed `new(...)` of a
    // modeled struct builds a value, also through a `: this(...)` chain.
    [TestCase("public Span Plus(Span other) => this + other;")]
    [TestCase("public bool Same(Span other) => this == other;")]
    [TestCase("public static Span FromTicks(long ticks) => new(ticks * 100);")]
    [TestCase("public static Span Sum(long a, long b) { return new(a, b); }")]
    [TestCase("public static Span Narrow(int ticks) => new Span(ticks, true);")]
    [TestCase("public static Span Zero { get; } = new Span(0L); public long Low => Zero.ticks;")]
    [TestCase("public static readonly Span One = new Span(1L); public static bool IsOne(Span s) => s == One;")]
    public void StructMembersLowerExactly(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var source = """
            public readonly struct Span
            {
                private readonly long ticks;
                public Span(long ticks) { this.ticks = ticks; }
                internal Span(long a, long b) { ticks = a + b; }
                internal Span(int ticks, bool scaled) : this(scaled ? ticks * 100L : ticks) { }
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

    // Converting a lambda to a delegate runs none of its body, so factories
    // that return or store one lower exactly, generic and capturing ones too.
    [TestCase("public static System.Func<int,int> M() { return x => x + 1; }")]
    [TestCase("public static System.Func<int,int> M(int k) { if (k < 0) { throw new System.ArgumentOutOfRangeException(nameof(k)); } return x => x + k; }")]
    [TestCase("public static System.Func<T,int> M<T>(System.Func<T,int> g) { return t => g(t) + 1; }")]
    [TestCase("public static System.Func<int,System.Func<int>> M() { return x => { int c = x * 2; return () => c; }; }")]
    [TestCase("public static System.Func<int,int> M(int k) => x => x + k;")]
    [TestCase("public static int M(System.Collections.Generic.List<int> l) { System.Predicate<int> p = x => x > 0; return l.Count; }")]
    public void LambdaFactoriesLowerExactly(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var (compilation, declaration) = Compile("public static class C { " + member + " }");
        var labels = BlockerCensus.Blockers(compilation, declaration, TestContext.CurrentContext.CancellationToken);
        Assert.That(labels, Is.EqualTo(ExactRow), string.Join(" ; ", labels));
    }
}
