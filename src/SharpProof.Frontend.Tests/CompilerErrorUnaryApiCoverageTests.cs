using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerErrorUnaryApiCoverageTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["CS0023"];

    [Test]
    public void LogicalNotOnIntegerPreservesCompilerErrorAndTypedApiAbstention()
    {
        const string source = "private static bool Target(long value) => !value;";
        var tree = CSharpSyntaxTree.ParseText(
            FrontendTestHelpers.WrapSubjectMembers(source),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            "CompilerErrorUnaryApi",
            [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .ToArray();
        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
            Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.That(emitted.Success, Is.False);
        Assert.That(emitted.Diagnostics.Any(static diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == "CS0023" && diagnostic.Severity == DiagnosticSeverity.Error)), Is.True);

        // This is a compiler recovery operation inspected by the expression API.
        // Failed compilation supplies no executable program or proof context.
        var syntax = tree.GetRoot().DescendantNodes()
            .OfType<PrefixUnaryExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.AssignableTo<IUnaryOperation>());
        var unary = (IUnaryOperation)operation!;
        Assert.That(unary.OperatorKind, Is.EqualTo(UnaryOperatorKind.Not));
        Assert.That(unary.OperatorMethod, Is.Null);
        Assert.That(unary.IsLifted, Is.False);
        Assert.That(unary.Operand.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int64));
        TestContext.WriteLine($"Recovery unary result kind={unary.Type?.TypeKind}; special type={unary.Type?.SpecialType}; scalar={CSharpOperationSemantics.IsScalar(unary.Type)}");
        var lowered = new RoslynOperationLowerer(new IrFactory()).Lower(unary);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(lowered.Term, Is.TypeOf<IrOpaqueTerm>());
    }
}
