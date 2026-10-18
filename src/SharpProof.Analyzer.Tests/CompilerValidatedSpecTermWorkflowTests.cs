using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerValidatedSpecTermWorkflowTests
{
    [TestCase(int.MinValue, false)]
    [TestCase(int.MinValue + 1, true)]
    public void DefaultAbsNormalCompletionPreservesTheOverflowBoundary(int value, bool expected)
    {
        var template = ApiSpecTable.Default.Templates.Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Target.WitnessIdentifier == "bcl.math.abs.int32"));
        Assert.That(template.Target.DocumentationCommentId, Is.EqualTo("M:System.Math.Abs(System.Int32)"));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var integer = factory.GetOrCreateIntegerType(32, true);
        var term = CompilerTotalSpecTermInstantiator.Instantiate(factory,
            template.Facets.Throws.NormalCompletion!, [factory.Integer(integer, value)], integer, 0);
        AssertBoolean(factory, term, expected);
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void ValidatedBooleanNegationPreservesItsValue(bool value, bool expected)
    {
        var condition = ValidateMaxPostcondition(new SpecUnaryDeclaration(IrUnaryOperator.Not,
            new SpecBooleanDeclaration(value), IrTypeKind.Boolean));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var integer = factory.GetOrCreateIntegerType(32, true);
        AssertBoolean(factory, CompilerTotalSpecTermInstantiator.Instantiate(factory,
            condition, [factory.Integer(integer, 1), factory.Integer(integer, 2)], integer, 0), expected);
    }

    [TestCase(7, 3)]
    [TestCase(3, 7)]
    public void ValidatedConditionalSelectsTheMaximumParameter(int left, int right)
    {
        var first = new SpecVariableDeclaration(SpecVariableRole.Parameter, 0, IrTypeKind.Integer);
        var second = new SpecVariableDeclaration(SpecVariableRole.Parameter, 1, IrTypeKind.Integer);
        var maximum = new SpecConditionalDeclaration(
            new SpecBinaryDeclaration(IrBinaryOperator.GreaterThan, first, second, IrTypeKind.Boolean),
            first, second, IrTypeKind.Integer);
        var condition = ValidateMaxPostcondition(new SpecBinaryDeclaration(IrBinaryOperator.Equal,
            maximum, new SpecIntegerDeclaration(7), IrTypeKind.Boolean));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var integer = factory.GetOrCreateIntegerType(32, true);
        AssertBoolean(factory, CompilerTotalSpecTermInstantiator.Instantiate(factory, condition,
            [factory.Integer(integer, left), factory.Integer(integer, right)], integer, 0), true);
    }

    [Test]
    public void ValidatedLongBclParametersPreserveTheWideLiteral()
    {
        var condition = ValidateMaxPostcondition(new SpecBinaryDeclaration(IrBinaryOperator.Equal,
            new SpecVariableDeclaration(SpecVariableRole.Parameter, 0, IrTypeKind.Integer),
            new SpecIntegerDeclaration(long.MaxValue), IrTypeKind.Boolean), wide: true);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var integer = factory.GetOrCreateIntegerType(64, true);
        AssertBoolean(factory, CompilerTotalSpecTermInstantiator.Instantiate(factory, condition,
            [factory.Integer(integer, long.MaxValue), factory.Integer(integer, 0)], integer, 0), true);
    }

    [Test]
    public void ValidatedIntegerNegationRemainsOutsideTheTotalSpecSubset()
    {
        var condition = ValidateMaxPostcondition(new SpecBinaryDeclaration(IrBinaryOperator.LessThan,
            new SpecUnaryDeclaration(IrUnaryOperator.Negate, new SpecIntegerDeclaration(1), IrTypeKind.Integer),
            new SpecIntegerDeclaration(0), IrTypeKind.Boolean));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var integer = factory.GetOrCreateIntegerType(32, true);
        Assert.That((Action)(() => CompilerTotalSpecTermInstantiator.Instantiate(factory, condition,
            [factory.Integer(integer, 1), factory.Integer(integer, 2)], integer, 0)),
            Throws.TypeOf<ArgumentException>().With.Message.EqualTo("A specification-pack Total term is unsupported."));
    }

    [Test]
    public void ValidatedStringParameterRemainsOutsideTheScalarConverterTypes()
    {
        var template = ApiSpecTable.Default.Templates.Single(item =>
            item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Target.DocumentationCommentId == "M:System.String.IsNullOrEmpty(System.String)"));
        var compilation = AnalyzerTestHost.CreateCompilation(
            "internal class Subject { bool Run(string value) => System.String.IsNullOrEmpty(value); }", []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol!;
        Assert.That(method.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(method.GetDocumentationCommentId(), Is.EqualTo(template.Target.DocumentationCommentId));
        var condition = new SpecBinaryDeclaration(IrBinaryOperator.Equal,
            new SpecVariableDeclaration(SpecVariableRole.Parameter, 0, IrTypeKind.String),
            new SpecStringDeclaration("text"), IrTypeKind.Boolean);
        var table = ApiSpecTable.Create([new ApiSpecDeclaration(template.Target, template.Facets,
            [new SpecPostconditionDeclaration(condition, template.Facets.Effects.Evidence)])]);
        var validated = table.Templates.Single().Postconditions.Single().Condition;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.That((Action)(() => CompilerTotalSpecTermInstantiator.Instantiate(factory, validated,
            [factory.String("text")], factory.IntegerType, 0)),
            Throws.TypeOf<ArgumentException>().With.Message.EqualTo("A specification-pack Total term has an invalid type."));
    }

    private static SpecTermDeclaration ValidateMaxPostcondition(SpecTermDeclaration condition, bool wide = false)
    {
        System.ArgumentNullException.ThrowIfNull(condition);
        var template = ApiSpecTable.Default.Templates.Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Target.WitnessIdentifier == "bcl.math.max.int32-int32"));
        var arguments = wide ? "1L, 2L" : "1, 2";
        var compilation = AnalyzerTestHost.CreateCompilation($"internal class Subject {{ object Run() => System.Math.Max({arguments}); }}", []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol!;
        Assert.That(method.Parameters.All(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.Type.SpecialType ==
            (wide ? SpecialType.System_Int64 : SpecialType.System_Int32))), Is.True);
        var target = template.Target with
        {
            DocumentationCommentId = method.GetDocumentationCommentId()!,
            WitnessIdentifier = wide ? "bcl.math.max.int64-int64" : template.Target.WitnessIdentifier
        };
        var declaration = new ApiSpecDeclaration(target, template.Facets,
            [new SpecPostconditionDeclaration(condition, template.Facets.Effects.Evidence)]);
        var table = ApiSpecTable.Create([declaration]);
        return table.Templates.Single().Postconditions.Single().Condition;
    }

    private static void AssertBoolean(IrFactory factory, IrTerm term, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(term);
        var result = new IrInterpreter(factory).Evaluate(term);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(term.Type, Is.EqualTo(factory.BooleanType));
            Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(result.Value!.Boolean, Is.EqualTo(expected));
        }
    }
}
