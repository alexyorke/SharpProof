using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OpaqueSemanticIdentityTests
{
    [Test]
    public void ReferenceConversionIdentitySeparatesTryCastFromThrowingCast()
    {
        var operations = GetTargetExpression(
                """
                #nullable enable
                public sealed class Widget {}
                public static class Subject
                {
                    public static bool Target(object value) =>
                        (value as Widget) == (Widget)value;
                }
                """)
            .DescendantsAndSelf()
            .OfType<IConversionOperation>()
            .Where(static operation =>
                operation.Conversion.IsReference && !operation.IsImplicit)
            .OrderByDescending(static operation => operation.IsTryCast)
            .ToArray();
        Assert.That(operations, Has.Length.EqualTo(2));
        Assert.That(operations[0].IsTryCast, Is.True);
        Assert.That(operations[1].IsTryCast, Is.False);

        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var tryCast = lowerer.Lower(operations[0]);
        var throwingCast = lowerer.Lower(operations[1]);

        AssertPureConversionAbstention(tryCast);
        AssertPureConversionAbstention(throwingCast);
        Assert.That(throwingCast.Term, Is.Not.SameAs(tryCast.Term));
        Assert.That(throwingCast.Term.Id, Is.Not.EqualTo(tryCast.Term.Id));
    }

    [Test]
    public void ConstantFieldIdentitySeparatesDistinctFieldsOfTheSameType()
    {
        var fields = GetTargetExpression(
                """
                public static class Subject
                {
                    private const long First = 1L;
                    private const long Second = 2L;
                    public static long Target() => First + Second;
                }
                """)
            .DescendantsAndSelf()
            .OfType<IFieldReferenceOperation>()
            .OrderBy(static operation => operation.Field.Name)
            .ToArray();
        Assert.That(fields, Has.Length.EqualTo(2));
        Assert.That(fields[0].ConstantValue.Value, Is.EqualTo(1L));
        Assert.That(fields[1].ConstantValue.Value, Is.EqualTo(2L));

        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var first = lowerer.Lower(fields[0]);
        var second = lowerer.Lower(fields[1]);

        AssertPureUnsupportedOperationAbstention(first);
        AssertPureUnsupportedOperationAbstention(second);
        Assert.That(second.Term, Is.Not.SameAs(first.Term));
        Assert.That(second.Term.Id, Is.Not.EqualTo(first.Term.Id));
    }

    private static void AssertPureConversionAbstention(
        FrontendLoweringResult result)
    {
        Assert.That(result.Term, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(
            ((IrOpaqueTerm)result.Term).Purity,
            Is.EqualTo(IrOpaquePurity.Pure));
        Assert.That(
            result.Classification.Decision,
            Is.EqualTo(FrontendSubsetDecision.ClosedAbstention));
        Assert.That(
            result.Classification.Abstention,
            Is.EqualTo(FrontendAbstention.ConversionMayChangeValue));
    }

    private static void AssertPureUnsupportedOperationAbstention(
        FrontendLoweringResult result)
    {
        Assert.That(result.Term, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(
            ((IrOpaqueTerm)result.Term).Purity,
            Is.EqualTo(IrOpaquePurity.Pure));
        Assert.That(
            result.Classification.Decision,
            Is.EqualTo(FrontendSubsetDecision.ClosedAbstention));
        Assert.That(
            result.Classification.Abstention,
            Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
    }

    private static IOperation GetTargetExpression(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            "OpaqueSemanticIdentityTests_" + Guid.NewGuid().ToString("N"),
            [tree],
            PlatformReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            diagnostics,
            Is.Empty,
            string.Join(Environment.NewLine, diagnostics.Select(
                static diagnostic => diagnostic.ToString())));

        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static candidate => candidate.Identifier.ValueText == "Target");
        return compilation.GetSemanticModel(tree)
            .GetOperation(method.ExpressionBody!.Expression)!;
    }

    private static ImmutableArray<MetadataReference> PlatformReferences
    {
        get;
    } =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path =>
                (MetadataReference)MetadataReference.CreateFromFile(path))];
}
