using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FrontendPublicLoweringBoundaryTests
{
    [Test]
    public void PublicCandidateRejectsAContextOwnedByAnotherFactory()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => x;");
        var lowerer = new RoslynProgramLowerer(new IrFactory(IrExecutionSemantics.Total));
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            _ = lowerer.LowerCandidate(subject.Graph, subject.Context)));
        Assert.That(error!.ParamName, Is.EqualTo("context"));
    }

    [Test]
    public void SourceAndShadowEntryPointsRejectAnotherFactory()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => x;");
        var lowerer = new RoslynProgramLowerer(new IrFactory(IrExecutionSemantics.Total));
        var source = Assert.Throws<ArgumentException>((Action)(() =>
            _ = lowerer.LowerCandidate(subject.Graph, subject.Context, static _ => true, default)));
        var shadow = Assert.Throws<ArgumentException>((Action)(() =>
            _ = lowerer.LowerShadowSourceBody(subject.Graph, subject.Context, static _ => true, default)));
        Assert.That(source!.ParamName, Is.EqualTo("context"));
        Assert.That(shadow!.ParamName, Is.EqualTo("context"));
    }

    [Test]
    public void TotalContextRequiresTotalFactorySemantics()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => x;");
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            _ = new TotalLoweringContext(new IrFactory(), subject.Context.Target)));
        Assert.That(error!.ParamName, Is.EqualTo("factory"));
    }

    [Test]
    public void EntryVariablesMatchTheActualCallableInputs()
    {
        using var subject = TypedProgramSubject.Create("int Target(int left, int right) => left + right;");
        Assert.That(subject.Context.EntryVariables,
            Is.EqualTo(subject.Context.Inputs.Select(binding => binding.Entry)));
        Assert.That(subject.Context.EntryVariables, Has.Length.EqualTo(2));
    }

    [Test]
    public void RealAutoAccessorRejectsAContextFromAnotherFactory()
    {
        var tree = CSharpSyntaxTree.ParseText("public sealed class Subject { public int Value { get; set; } }");
        var compilation = CSharpCompilation.Create("AccessorBoundary", [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        var getter = tree.GetRoot().DescendantNodes().OfType<AccessorDeclarationSyntax>()
            .Single(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(getter)!;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method);
        var lowerer = new RoslynProgramLowerer(new IrFactory(IrExecutionSemantics.Total));
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            _ = lowerer.LowerAutoAccessor(context, getter)));
        Assert.That(error!.ParamName, Is.EqualTo("context"));
    }

    [TestCase("int Target(ref int x) { ref int alias = ref (x); return alias; }")]
    [TestCase("int Target(ref int x) => Read(ref (x)); static int Read(ref int value) => value;")]
    public void ParenthesizedReferenceUsesRemainOutsideReadOnlyParameterAdmission(string source)
    {
        using var subject = TypedProgramSubject.Create(source);
        Assert.That(subject.Invoke([7]), Is.EqualTo(7));
        Assert.That(subject.Context.HasScalarSignature, Is.False);
        Assert.That(subject.Lower().IsExact, Is.False);
    }
}
