using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreMissingFieldInitializerCoverageTests
{
    private static readonly string[] MissingValueCompilerErrors = ["CS1525"];

    [TestCase(false)]
    [TestCase(true)]
    public async Task OrdinaryMissingFieldValueRetainsEditorRecoveryAdmission(bool missingValue)
    {
        var value = missingValue ? string.Empty : "1";
        var source = "using SharpProof.Attributes; public static class InitializerFixture { " +
            "public static int Value = " + value + "; } " +
            "public static class SelectedFixture { [EnforcePure] public static int Target() => 1; }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0002", "SP0047"]);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id), missingValue ? Is.EqualTo(MissingValueCompilerErrors) : Is.Empty);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "effects", analyzer: new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.EqualTo(1));
        Assert.That(factory.Session, Is.Not.Null);
        var actual = factory.Session!.Compilation;
        var root = await actual.SyntaxTrees.Single().GetRootAsync().ConfigureAwait(false);
        var variable = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        Assert.That(variable.Initializer, Is.Not.Null);
        var initializer = variable.Initializer!;
        var model = actual.GetSemanticModel(variable.SyntaxTree);
        var field = (IFieldSymbol)model.GetDeclaredSymbol(variable)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(field.ContainingAssembly, actual.Assembly), Is.True);
        Assert.That(field.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(initializer.Value.GetFirstToken(includeZeroWidth: true).IsMissing, Is.EqualTo(missingValue));
        Assert.That(initializer.Value.Span.IsEmpty, Is.EqualTo(missingValue));
        var enclosing = model.GetEnclosingSymbol(initializer.Value.SpanStart);
        var expected = missingValue ? (ISymbol)field.ContainingType : field;
        Assert.That(SymbolEqualityComparer.Default.Equals(enclosing, expected), Is.True);
        var operation = model.GetOperation(initializer) as IFieldInitializerOperation;
        Assert.That(operation, Is.Not.Null);
        Assert.That(operation!.InitializedFields, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(operation.InitializedFields.Single(), field), Is.True);
        Assert.That(operation.Value.Kind, Is.EqualTo(missingValue ? OperationKind.Invalid : OperationKind.Literal));
        var targetDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(targetDeclaration)!;
        var (body, gap) = AdvisoryLowering.Lower((Microsoft.CodeAnalysis.CSharp.CSharpCompilation)actual, target,
            targetDeclaration, CancellationToken.None);
        Assert.That(gap, Is.Null);
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Lowering.IsExact, Is.True);
        Assert.That(body.Program.Blocks.Sum(block => block.Instructions.Length), Is.LessThanOrEqualTo(4096));
        Assert.That(factory.GetNamedOutcome("Target"), Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }
}
