using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicSemanticPipelineTests
{
    [Test]
    public void LoweringResult_DistinguishesExactAndUnsupportedWithStableProvenance()
    {
        var exact = CreateExpressionContext("int value", "value > 0");
        var exactResult = SymbolicSemanticPipeline.LowerCondition(exact.Expression, exact.LoweringContext);

        Assert.That(exactResult.Support, Is.EqualTo(SymbolicLoweringSupport.Exact));
        Assert.That(exactResult.Value, Is.Not.Null);
        Assert.That(exactResult.UnknownReason, Is.EqualTo(SymbolicUnknownReason.None));
        Assert.That(exactResult.Provenance.Single().Stage, Is.EqualTo("roslyn-to-ir.condition"));
        Assert.That(exactResult.Provenance.Single().SourceSpan, Is.EqualTo(exact.Expression.Span));

        var unsupported = CreateExpressionContext("object value", "new object()");
        var unsupportedResult = SymbolicSemanticPipeline.LowerTerm(
            unsupported.Expression,
            unsupported.LoweringContext);

        Assert.That(unsupportedResult.Support, Is.EqualTo(SymbolicLoweringSupport.Unsupported));
        Assert.That(unsupportedResult.Value, Is.Null);
        Assert.That(unsupportedResult.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
        Assert.That(unsupportedResult.Provenance.Single().Detail, Is.EqualTo("unsupported"));
    }

    [Test]
    public void EnumConversions_PreserveByteLongAndUnsignedIntegralValues()
    {
        const string declarations = """
                                    public enum ByteMode : byte { Max = byte.MaxValue }
                                    public enum LongMode : long { Min = long.MinValue }
                                    public enum UIntMode : uint { Max = uint.MaxValue }
                                    """;
        var expected = new long[] { byte.MaxValue, long.MinValue, uint.MaxValue };
        var expressions = new[]
        {
            "(long)ByteMode.Max",
            "(long)LongMode.Min",
            "(long)UIntMode.Max"
        };

        for (var index = 0; index < expressions.Length; index++)
        {
            var context = CreateExpressionContext(string.Empty, expressions[index], declarations);
            var result = SymbolicSemanticPipeline.LowerConversion(context.Expression, context.LoweringContext);

            Assert.That(result.Support, Is.EqualTo(SymbolicLoweringSupport.Exact), expressions[index]);
            Assert.That(result.Value, Is.EqualTo(new SymbolicIntegerConstantTerm(expected[index])),
                expressions[index]);
        }
    }

    [TestCase("ByteMode", "long")]
    [TestCase("LongMode", "long")]
    [TestCase("UIntMode", "long")]
    [TestCase("UIntMode", "ulong")]
    public void EnumConversions_PreserveNonConstantIntegralTerms(string enumType, string targetType)
    {
        const string declarations = """
                                    public enum ByteMode : byte { Value = 1 }
                                    public enum LongMode : long { Value = 1 }
                                    public enum UIntMode : uint { Value = 1 }
                                    """;
        var context = CreateExpressionContext(
            enumType + " value",
            "(" + targetType + ")value",
            declarations);

        var result = SymbolicSemanticPipeline.LowerConversion(context.Expression, context.LoweringContext);

        Assert.That(result.Support, Is.EqualTo(SymbolicLoweringSupport.Exact));
        Assert.That(result.Value, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)result.Value!).Name, Does.StartWith("value#"));
    }

    [Test]
    public void ExecutionTraversal_StopsAtLambdasAndLocalFunctionsByDefault()
    {
        var tree = CSharpSyntaxTree.ParseText("""
                                              using System;
                                              class Target
                                              {
                                                  void M()
                                                  {
                                                      int value = 0;
                                                      value = 1;
                                                      Action nested = () => value = 2;
                                                      void Local() { value = 3; }
                                                  }
                                              }
                                              """);
        var body = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;

        var visibleAssignments = CSharpSyntaxFacts.DescendantNodesInExecution(body)
            .OfType<AssignmentExpressionSyntax>()
            .Select(static assignment => assignment.Right.ToString())
            .ToArray();
        var allAssignments = CSharpSyntaxFacts.DescendantNodesInExecution(body, includeNestedCallables: true)
            .OfType<AssignmentExpressionSyntax>()
            .Select(static assignment => assignment.Right.ToString())
            .ToArray();

        Assert.That(visibleAssignments, Is.EqualTo(new[] { "1" }));
        Assert.That(allAssignments, Is.EqualTo(new[] { "1", "2", "3" }));
    }

    [Test]
    public void RangeShapeWriteDetection_IgnoresUnexecutedLambdaBody()
    {
        var tree = CSharpSyntaxTree.ParseText("""
                                              using System;
                                              class Target
                                              {
                                                  string Slice(string value)
                                                  {
                                                      Range range = 1..^1;
                                                      Action mutateLater = () => range = ..;
                                                      return value[range];
                                                  }
                                              }
                                              """);
        var compilation = CreateCompilation(tree, "RangeShapeExecutionProbe");
        var semanticModel = compilation.GetSemanticModel(tree);
        var access = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
        var context = new SymbolicLoweringContext(semanticModel, CancellationToken.None);

        Assert.That(
            SymbolicIrLowerer.TryCreateBuiltInElementAccessInRangeCondition(
                access.Expression,
                access.ArgumentList.Arguments.Single().Expression,
                access,
                "test.range-shape",
                context,
                out var condition),
            Is.True);
        Assert.That(condition, Is.Not.Null);
    }

    [Test]
    public void FalseTypePatternBranch_ProducesComplementaryTypedFacts()
    {
        var context = CreateExpressionContext("object value", "value is string");

        var result = SymbolicSemanticPipeline.LowerBranchFacts(
            context.Expression,
            false,
            context.LoweringContext);

        Assert.That(result.Support, Is.EqualTo(SymbolicLoweringSupport.Exact));
        Assert.That(result.Value!.PathConditions, Has.Length.EqualTo(1));
        Assert.That(result.Value.PathConditions[0], Is.TypeOf<SymbolicNotCondition>());

        var formulas = new List<SmtFormula>();
        Assert.That(
            SymbolicReachabilityService.TryCollectBranchAssumptions(
                context.Expression,
                false,
                context.SemanticModel,
                CancellationToken.None,
                formulas),
            Is.True);
        Assert.That(formulas.Any(IsNegatedRuntimeTypeCondition), Is.True);
    }

    [Test]
    public void StructuralKeys_AreCultureAndAllocationOrderIndependent()
    {
        static SymbolicCondition CreateCondition(bool reverse)
        {
            var left = new SymbolicElementTerm(
                new SymbolicMemberTerm(
                    new SymbolicVariableTerm("receiver", SmtValueKind.Reference),
                    "Values",
                    SmtValueKind.Reference),
                new SymbolicIntegerConstantTerm(12),
                SmtValueKind.Int);
            var right = new SymbolicIntegerConstantTerm(34);
            var relation = reverse
                ? new SymbolicRelationAtom(SymbolicRelationOperator.Equal, right, left)
                : new SymbolicRelationAtom(SymbolicRelationOperator.Equal, left, right);
            return new SymbolicFactCondition(new SymbolicFact(
                relation,
                true,
                SymbolicFactConfidence.Exact,
                "test",
                default,
                null,
                null));
        }

        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = SymbolicStructuralKey.ForCondition(CreateCondition(false));

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            var second = SymbolicStructuralKey.ForCondition(CreateCondition(true));

            Assert.That(second, Is.EqualTo(first));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Test]
    public void MixedAggregateTrigger_DoesNotUseExactSubsetAsReachabilityProof()
    {
        var site = SyntaxFactory.ParseExpression("new int[first, second]");
        var subject = new SymbolicVariableTerm("first", SmtValueKind.Int);
        var exactFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                subject,
                new SymbolicIntegerConstantTerm(0)),
            site,
            "test.exact-subset");
        var exactSubset = new SymbolicFactCondition(exactFact);
        var method = typeof(SymbolicRuntimeHazardQueryService).GetMethod(
            "CreateAggregateExceptionPreconditionTrigger",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var trigger = method.Invoke(null, new object?[]
        {
            site,
            SymbolicExceptionPreconditionKind.NegativeLength,
            subject,
            exactSubset,
            false,
            "test.aggregate"
        })!;
        var preconditionProperty = trigger.GetType().GetProperty(
            "Precondition",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var precondition = (SymbolicFact)preconditionProperty.GetValue(trigger)!;

        Assert.That(precondition.Confidence, Is.EqualTo(SymbolicFactConfidence.Unsupported));
        Assert.That(precondition.Atom, Is.TypeOf<SymbolicExceptionPreconditionAtom>());
        var atom = (SymbolicExceptionPreconditionAtom)precondition.Atom;
        Assert.That(atom.Subject, Is.EqualTo(subject));
        Assert.That(atom.Trigger, Is.TypeOf<SymbolicFactCondition>());
        Assert.That(atom.Trigger, Is.Not.EqualTo(exactSubset));
        Assert.That(((SymbolicFactCondition)atom.Trigger).Fact.Provenance,
            Does.EndWith(".unsupported.trigger"));
    }

    [Test]
    public void ExtendedPropertyPattern_LowersIntermediateNonNullCondition()
    {
        var context = CreateExpressionContext(
            "Box box",
            "box is { Child.Value: > 0 }",
            "public sealed class Box { public Child Child { get; } = new(); } " +
            "public sealed class Child { public int Value { get; } }");

        var lowering = SymbolicSemanticPipeline.LowerCondition(context.Expression, context.LoweringContext);

        Assert.That(lowering.Support, Is.EqualTo(SymbolicLoweringSupport.Exact));
        Assert.That(lowering.Value, Is.Not.Null);
        Assert.That(SymbolicStructuralKey.ForCondition(lowering.Value!), Does.Contain("Child"));
    }

    [Test]
    public void ListPatternDesignation_LowersBindingAndLengthConditions()
    {
        var context = CreateExpressionContext("int[] values", "values is [var first, ..]");

        var lowering = SymbolicSemanticPipeline.LowerCondition(context.Expression, context.LoweringContext);

        Assert.That(lowering.Support, Is.EqualTo(SymbolicLoweringSupport.Exact));
        Assert.That(lowering.Value, Is.Not.Null);
        var key = SymbolicStructuralKey.ForCondition(lowering.Value!);
        Assert.That(key, Does.Contain("first"));
        Assert.That(key, Does.Contain("length"));
    }

    private static bool IsNegatedRuntimeTypeCondition(SmtFormula formula)
    {
        return formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not, Operand: var operand } &&
               ContainsRuntimeTypeTest(operand);
    }

    private static bool ContainsRuntimeTypeTest(SmtFormula formula)
    {
        return formula switch
        {
            SmtRuntimeTypeTestFormula => true,
            SmtUnaryFormula unary => ContainsRuntimeTypeTest(unary.Operand),
            SmtBinaryFormula binary =>
                ContainsRuntimeTypeTest(binary.Left) || ContainsRuntimeTypeTest(binary.Right),
            _ => false
        };
    }

    private static ExpressionContext CreateExpressionContext(
        string parameters,
        string expression,
        string declarations = "")
    {
        var source = declarations + "\npublic sealed class Probe { public object M(" + parameters + ") => " +
                     expression + "; }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CreateCompilation(tree, "SemanticPipelineProbe");
        var semanticModel = compilation.GetSemanticModel(tree);
        var expressionSyntax = tree.GetRoot()
            .DescendantNodes()
            .OfType<ArrowExpressionClauseSyntax>()
            .Single()
            .Expression;
        return new ExpressionContext(
            semanticModel,
            expressionSyntax,
            new SymbolicLoweringContext(semanticModel, CancellationToken.None));
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree tree, string assemblyName)
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed record ExpressionContext(
        SemanticModel SemanticModel,
        ExpressionSyntax Expression,
        SymbolicLoweringContext LoweringContext);
}
