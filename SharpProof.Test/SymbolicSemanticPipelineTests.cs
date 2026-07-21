using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SharpProof.Test.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicSemanticPipelineTests {
    [Test]
    public void LoweringResult_DistinguishesExactAndUnsupportedWithStableProvenance() {
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
    public void EnumConversions_PreserveByteLongAndUnsignedIntegralValues() {
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

        for (var index = 0; index < expressions.Length; index++) {
            var context = CreateExpressionContext(string.Empty, expressions[index], declarations);
            var result = SymbolicSemanticPipeline.LowerTerm(context.Expression, context.LoweringContext);

            Assert.That(result.Support, Is.EqualTo(SymbolicLoweringSupport.Exact), expressions[index]);
            Assert.That(result.Value, Is.EqualTo(new SymbolicIntegerConstantTerm(expected[index])),
                expressions[index]);
        }
    }

    [TestCase("ByteMode", "long")]
    [TestCase("LongMode", "long")]
    [TestCase("UIntMode", "long")]
    [TestCase("UIntMode", "ulong")]
    public void EnumConversions_PreserveNonConstantIntegralTerms(string enumType, string targetType) {
        const string declarations = """
                                    public enum ByteMode : byte { Value = 1 }
                                    public enum LongMode : long { Value = 1 }
                                    public enum UIntMode : uint { Value = 1 }
                                    """;
        var context = CreateExpressionContext(
            enumType + " value",
            "(" + targetType + ")value",
            declarations);

        var result = SymbolicSemanticPipeline.LowerTerm(context.Expression, context.LoweringContext);

        Assert.That(result.Support, Is.EqualTo(SymbolicLoweringSupport.Exact));
        Assert.That(result.Value, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)result.Value!).Name, Does.StartWith("value#"));
    }

    [Test]
    public void ExecutionTraversal_StopsAtLambdasAndLocalFunctionsByDefault() {
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
    public void TestOracle_TypeTestRequiresNonNullEquivalence() {
        static bool CanTranslate(string valueType, string testedType) {
            var tree = CSharpSyntaxTree.ParseText(
                "class Target { bool M(" + valueType + " value) => value is " + testedType + "; }");
            var compilation = CreateCompilation(tree, "TypeTestOracleProbe");
            var semanticModel = compilation.GetSemanticModel(tree);
            var expression = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>()
                .Single(static candidate => candidate.IsKind(SyntaxKind.IsExpression));

            return CSharpConditionToFormula.TryTranslate(
                expression,
                semanticModel,
                CancellationToken.None,
                out _);
        }

        Assert.That(CanTranslate("object", "string"), Is.False);
        Assert.That(CanTranslate("string", "object"), Is.True);
    }

    [Test]
    public void ListPatternElementPosition_TestOnlyAdapterIsAbsent() {
        Assert.That(
            typeof(CSharpSyntaxFacts).GetMethod(
                "TryGetListPatternElementPosition",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            Is.Null);
    }

    [Test]
    public void VariablePrefixScanner_DoesNotConfuseNumericLocationPrefixes() {
        static SymbolicFact Fact(string name) => SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicVariableTerm(name, SmtValueKind.Int),
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.IdentifierName(name),
            "test.variable-prefix");

        Assert.That(
            SymbolicIrReferenceScanner.ContainsVariablePrefix(
                Fact("x#12"),
                "x#1"),
            Is.False);
        Assert.That(
            SymbolicIrReferenceScanner.ContainsVariablePrefix(
                Fact("x#1@v2"),
                "x#1"),
            Is.True);
    }

    [Test]
    public void InferredNotNullPostcondition_RecognizesSubsequentLeadingParameterGuard() {
        var tree = CSharpSyntaxTree.ParseText("""
                                              #nullable enable
                                              class Target
                                              {
                                                  void M(string? first, string? second)
                                                  {
                                                      if (first is null) throw new System.ArgumentNullException();
                                                      if (second is null) throw new System.ArgumentNullException();
                                                  }
                                              }
                                              """);
        var compilation = CreateCompilation(tree, "MultipleNullGuardProbe");
        var method = compilation.GetSemanticModel(tree).GetDeclaredSymbol(
            tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single())!;

        Assert.That(
            NullableFlowFacts.HasInferredNotNullNormalCompletionPostcondition(
                method.Parameters[1],
                CancellationToken.None),
            Is.True);
    }

    [Test]
    public void RangeShapeWriteDetection_IgnoresUnexecutedLambdaBody() {
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
            TypedSymbolicTestLowering.TryCreateBuiltInElementAccessInRangeCondition(
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
    public void FalseTypePatternBranch_ProducesComplementaryTypedFacts() {
        var context = CreateExpressionContext("object value", "value is string");

        var result = SymbolicSemanticPipeline.LowerBranchCondition(
            context.Expression,
            false,
            context.LoweringContext);

        Assert.That(result.Support, Is.EqualTo(SymbolicLoweringSupport.Exact));
        Assert.That(result.Value, Is.TypeOf<SymbolicNotCondition>());

        var negated = (SymbolicNotCondition)result.Value!;
        Assert.That(ContainsRuntimeTypeTest(negated.Operand), Is.True);
    }

    [Test]
    public void StructuralKeys_AreCultureAndAllocationOrderIndependent() {
        static SymbolicCondition CreateCondition(bool reverse) {
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
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = SymbolicState.CreateProofConditionKey(CreateCondition(false));

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            var second = SymbolicState.CreateProofConditionKey(CreateCondition(true));

            Assert.That(second, Is.EqualTo(first));
        }
        finally {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Test]
    public void MixedAggregateTrigger_DoesNotUseExactSubsetAsReachabilityProof() {
        const string source =
            "static class C { static int Size() => 1; static int[,] M(int first) => new int[first, Size()]; }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(MixedAggregateTrigger_DoesNotUseExactSubsetAsReachabilityProof));
        var site = fixture.Root.DescendantNodes().OfType<ArrayCreationExpressionSyntax>().Single();
        var lowered = SymbolicOperationLowerer.TryLowerNegativeLengthHazard(
            fixture.SemanticModel.GetOperation(site)!,
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None),
            out var hazard);

        Assert.That(lowered, Is.True);
        Assert.That(hazard.Confidence, Is.EqualTo(SymbolicFactConfidence.Unsupported));
        Assert.That(hazard.Subject, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(hazard.Trigger, Is.TypeOf<SymbolicFactCondition>());
        Assert.That(((SymbolicFactCondition)hazard.Trigger).Fact.Provenance,
            Is.EqualTo("ir.runtime-hazard.array.negative-length.aggregate.unsupported.trigger"));
    }

    [Test]
    public void ExtendedPropertyPattern_LowersIntermediateNonNullCondition() {
        var context = CreateExpressionContext(
            "Box box",
            "box is { Child.Value: > 0 }",
            "public sealed class Box { public Child Child { get; } = new(); } " +
            "public sealed class Child { public int Value { get; } }");

        var lowering = SymbolicSemanticPipeline.LowerCondition(context.Expression, context.LoweringContext);

        Assert.That(lowering.Support, Is.EqualTo(SymbolicLoweringSupport.Exact));
        Assert.That(lowering.Value, Is.Not.Null);
        Assert.That(SymbolicState.CreateProofConditionKey(lowering.Value!), Does.Contain("Child"));
    }

    [Test]
    public void ListPatternDesignation_LowersBindingAndLengthConditions() {
        var context = CreateExpressionContext("int[] values", "values is [var first, ..]");

        var lowering = SymbolicSemanticPipeline.LowerCondition(context.Expression, context.LoweringContext);

        Assert.That(lowering.Support, Is.EqualTo(SymbolicLoweringSupport.Exact));
        Assert.That(lowering.Value, Is.Not.Null);
        var key = SymbolicState.CreateProofConditionKey(lowering.Value!);
        Assert.That(key, Does.Contain("first"));
        Assert.That(key, Does.Contain("length"));
    }

    private static bool ContainsRuntimeTypeTest(SmtFormula formula) {
        return formula switch {
            SmtRuntimeTypeTestFormula => true,
            SmtUnaryFormula unary => ContainsRuntimeTypeTest(unary.Operand),
            SmtBinaryFormula binary =>
                ContainsRuntimeTypeTest(binary.Left) || ContainsRuntimeTypeTest(binary.Right),
            _ => false
        };
    }

    private static bool ContainsRuntimeTypeTest(SymbolicCondition condition) {
        return condition switch {
            SymbolicFactCondition { Fact.Atom: SymbolicTypeTestAtom } => true,
            SymbolicNotCondition negation => ContainsRuntimeTypeTest(negation.Operand),
            SymbolicBinaryCondition binary =>
                ContainsRuntimeTypeTest(binary.Left) || ContainsRuntimeTypeTest(binary.Right),
            _ => false
        };
    }

    private static ExpressionContext CreateExpressionContext(
        string parameters,
        string expression,
        string declarations = "") {
        var source = declarations + "\npublic sealed class Probe { public object M(" + parameters + ") => " +
                     expression + "; }";
        var fixture = RoslynTestFixture.CreateSingleNode<ArrowExpressionClauseSyntax>(
            source,
            "SemanticPipelineProbe",
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            });
        return new ExpressionContext(
            fixture.SemanticModel,
            fixture.Node.Expression,
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None));
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree tree, string assemblyName) {
        return RoslynTestFixture.CreateCompilation(
            tree,
            assemblyName,
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            }).Compilation;
    }

    private sealed record ExpressionContext(
        SemanticModel SemanticModel,
        ExpressionSyntax Expression,
        SymbolicLoweringContext LoweringContext);
}
