using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NullValueCompilerApiCoverageTests
{
    private static readonly ImmutableArray<string> ExpectedDiagnosticIds = ["CS0037"];

    [Test]
    public void NullToIntegerRecoveryRetainsCompilerErrorAndExpressionApiAbstention()
    {
        const string source = "private static int Target() => null;";
        var tree = CSharpSyntaxTree.ParseText(FrontendTestHelpers.WrapSubjectMembers(source),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("NullValueCompilerApi", [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToArray();
        Assert.That(errors.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedDiagnosticIds));
        using var output = new MemoryStream();
        Assert.That(compilation.Emit(output).Success, Is.False);
        var syntax = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Single(literal => literal is null ? throw new System.ArgumentNullException(nameof(literal)) : (literal.IsKind(SyntaxKind.NullLiteralExpression)));
        var model = compilation.GetSemanticModel(tree);
        var operation = model.GetOperation(syntax);
        Assert.That(operation, Is.AssignableTo<ILiteralOperation>());
        Assert.That(operation!.Type, Is.Null);
        Assert.That(operation.ConstantValue.HasValue, Is.True);
        Assert.That(operation.ConstantValue.Value, Is.Null);
        Assert.That(model.GetTypeInfo(syntax).ConvertedType!.SpecialType,
            Is.EqualTo(SpecialType.System_Int32));
        // Inspect only the actual recovery literal; failed Emit supplies no
        // executable program or trusted proof context.
        var lowered = new RoslynOperationLowerer(new IrFactory()).Lower(operation);
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(lowered.Term, Is.TypeOf<IrOpaqueTerm>());
    }
}
