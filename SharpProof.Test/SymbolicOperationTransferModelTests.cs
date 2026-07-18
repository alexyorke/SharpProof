using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
internal sealed class SymbolicOperationTransferModelTests
{
    [Test]
    public void UnsupportedCurrentAssignmentExpression_UsesConservativeCompletionFallback()
    {
        const string source = "static class C { static int Get() => 1; static void M() { _ = Get(); } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(UnsupportedCurrentAssignmentExpression_UsesConservativeCompletionFallback));
        var assignment = fixture.Root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var state = new SymbolicState();

        var applied = SymbolicCfgProgramPointStateCollector.TryApplyCurrentExpressionCompletion(
            ref state,
            assignment,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.That(applied, Is.True);
    }

    [TestCase("/", "definite_divide_by_zero")]
    [TestCase("%", "definite_modulo_by_zero")]
    public void DivideHazardLowering_EmitsExactTypedOperation(string op, string expectedCategory)
    {
        var source = $"static class C {{ static int M(int divisor) => 10 {op} divisor; }}";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(DivideHazardLowering_EmitsExactTypedOperation));
        var expression = fixture.Root.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var operation = fixture.SemanticModel.GetOperation(expression)!;

        var lowered = SymbolicOperationLowerer.TryLowerDivideByZeroHazard(
            operation,
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None),
            out var hazard);

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.True);
            Assert.That(hazard.HazardKind, Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero));
            Assert.That(hazard.PreconditionKind, Is.EqualTo(SymbolicExceptionPreconditionKind.DivideByZero));
            Assert.That(hazard.Confidence, Is.EqualTo(SymbolicFactConfidence.Exact));
            Assert.That(hazard.Subject, Is.TypeOf<SymbolicVariableTerm>());
            Assert.That(hazard.Category, Is.EqualTo(expectedCategory));
            Assert.That(hazard.Origin.Provenance, Is.EqualTo("ir.runtime-hazard.divide-by-zero"));
        });
    }

    [Test]
    public void DivideHazardLowering_PreservesUnsupportedTrigger()
    {
        const string source = "static class C { static int Divisor() => 1; static int M() => 10 / Divisor(); }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(DivideHazardLowering_PreservesUnsupportedTrigger));
        var expression = fixture.Root.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var operation = fixture.SemanticModel.GetOperation(expression)!;

        var lowered = SymbolicOperationLowerer.TryLowerDivideByZeroHazard(
            operation,
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None),
            out var hazard);

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.True);
            Assert.That(hazard.Confidence, Is.EqualTo(SymbolicFactConfidence.Unsupported));
            Assert.That(hazard.Subject, Is.Null);
            Assert.That(hazard.Origin.Provenance, Is.EqualTo("ir.runtime-hazard.divide-by-zero.unsupported"));
            Assert.That(hazard.Trigger, Is.TypeOf<SymbolicFactCondition>());
        });
    }

    [TestCase("return checked(value + 1);", "value + 1", "definite_checked_integral_overflow")]
    [TestCase("return checked(-value);", "-value", "definite_checked_integral_overflow")]
    [TestCase("return checked(++value);", "++value", "definite_checked_integral_overflow")]
    [TestCase("return checked(value += 1);", "value += 1", "definite_checked_integral_overflow")]
    [TestCase("return checked((byte)value);", "(byte)value", "definite_checked_numeric_conversion_overflow")]
    public void CheckedOverflowLowering_EmitsTypedOperation(
        string statement,
        string operationText,
        string expectedCategory)
    {
        var source = $"static class C {{ static int M(int value) {{ {statement} }} }}";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(CheckedOverflowLowering_EmitsTypedOperation));
        var expression = fixture.Root.DescendantNodes().OfType<ExpressionSyntax>()
            .Single(candidate => candidate.ToString() == operationText);
        var operation = fixture.SemanticModel.GetOperation(expression)!;

        var lowered = SymbolicOperationLowerer.TryLowerCheckedOverflowHazard(
            operation,
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None),
            out var hazard);

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.True);
            Assert.That(hazard.HazardKind, Is.EqualTo(SymbolicRuntimeHazardKind.CheckedIntegralOverflow));
            Assert.That(hazard.PreconditionKind, Is.EqualTo(SymbolicExceptionPreconditionKind.CheckedOverflow));
            Assert.That(hazard.Confidence, Is.EqualTo(SymbolicFactConfidence.Exact));
            Assert.That(hazard.Category, Is.EqualTo(expectedCategory));
        });
    }

    [TestCase(
        "string",
        SymbolicRuntimeHazardKind.NullDereference,
        (int)SymbolicExceptionPreconditionKind.NullDereference)]
    [TestCase(
        "object",
        SymbolicRuntimeHazardKind.ArgumentNull,
        (int)SymbolicExceptionPreconditionKind.ArgumentNull)]
    [TestCase(
        "dynamic",
        SymbolicRuntimeHazardKind.DynamicNullBinding,
        (int)SymbolicExceptionPreconditionKind.DynamicNullBinding)]
    public void ReferenceNullHazardLowering_EmitsExactTypedOperation(
        string parameterType,
        SymbolicRuntimeHazardKind hazardKind,
        int preconditionKindValue)
    {
        var preconditionKind = (SymbolicExceptionPreconditionKind)preconditionKindValue;
        var source = $"static class C {{ static object M({parameterType} value) => value; }}";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ReferenceNullHazardLowering_EmitsExactTypedOperation));
        var expression = fixture.Root.DescendantNodes().OfType<ArrowExpressionClauseSyntax>().Single().Expression;

        var lowered = SymbolicOperationLowerer.TryLowerReferenceNullHazard(
            expression,
            hazardKind,
            preconditionKind,
            "TestException",
            "test_category",
            "test.reference-null",
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None),
            suppressDefinitelyNotNull: false,
            out var hazard);

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.True);
            Assert.That(hazard.HazardKind, Is.EqualTo(hazardKind));
            Assert.That(hazard.PreconditionKind, Is.EqualTo(preconditionKind));
            Assert.That(hazard.Confidence, Is.EqualTo(SymbolicFactConfidence.Exact));
            Assert.That(hazard.Subject, Is.TypeOf<SymbolicVariableTerm>());
            Assert.That(hazard.Trigger, Is.TypeOf<SymbolicFactCondition>());
            Assert.That(hazard.Origin.Provenance, Is.EqualTo("test.reference-null"));
        });
    }

    [Test]
    public void ReferenceNullHazardLowering_PreservesUnsupportedTrigger()
    {
        const string source = "static class C { static object Get() => new(); static object M() => Get(); }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ReferenceNullHazardLowering_PreservesUnsupportedTrigger));
        var expression = fixture.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        var lowered = SymbolicOperationLowerer.TryLowerReferenceNullHazard(
            expression,
            SymbolicRuntimeHazardKind.NullDereference,
            SymbolicExceptionPreconditionKind.NullDereference,
            "TestException",
            "test_category",
            "test.reference-null",
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None),
            suppressDefinitelyNotNull: false,
            out var hazard);

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.True);
            Assert.That(hazard.Confidence, Is.EqualTo(SymbolicFactConfidence.Unsupported));
            Assert.That(hazard.Subject, Is.Null);
            Assert.That(hazard.Trigger, Is.TypeOf<SymbolicFactCondition>());
            Assert.That(hazard.Origin.Provenance, Is.EqualTo("test.reference-null.unsupported"));
        });
    }

    [Test]
    public void NullableValueHazardLowering_EmitsExactTypedOperation()
    {
        const string source = "static class C { static int M(int? value) => value.Value; }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(NullableValueHazardLowering_EmitsExactTypedOperation));
        var expression = fixture.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single().Expression;

        var lowered = SymbolicOperationLowerer.TryLowerNullableValueHazard(
            expression,
            "System.InvalidOperationException",
            "definite_nullable_value_without_value",
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None),
            out var hazard);

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.True);
            Assert.That(hazard.HazardKind, Is.EqualTo(SymbolicRuntimeHazardKind.NullableValueWithoutValue));
            Assert.That(hazard.PreconditionKind,
                Is.EqualTo(SymbolicExceptionPreconditionKind.NullableValueWithoutValue));
            Assert.That(hazard.Confidence, Is.EqualTo(SymbolicFactConfidence.Exact));
            Assert.That(hazard.Subject, Is.TypeOf<SymbolicVariableTerm>());
            Assert.That(hazard.Trigger, Is.TypeOf<SymbolicNotCondition>());
        });
    }

    [Test]
    public void NegativeLengthHazardLowering_AggregatesExactDimensions()
    {
        const string source = "static class C { static int[,] M(int rows, int columns) => new int[rows, columns]; }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(NegativeLengthHazardLowering_AggregatesExactDimensions));
        var creation = fixture.Root.DescendantNodes().OfType<ArrayCreationExpressionSyntax>().Single();

        var lowered = SymbolicOperationLowerer.TryLowerNegativeLengthHazard(
            fixture.SemanticModel.GetOperation(creation)!,
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None),
            out var hazard);

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.True);
            Assert.That(hazard.Confidence, Is.EqualTo(SymbolicFactConfidence.Exact));
            Assert.That(hazard.Subject, Is.TypeOf<SymbolicVariableTerm>());
            Assert.That(hazard.Trigger, Is.TypeOf<SymbolicBinaryCondition>());
            Assert.That(hazard.Origin.Provenance,
                Is.EqualTo("ir.runtime-hazard.array.negative-length.aggregate"));
        });
    }

    [Test]
    public void NegativeLengthHazardLowering_PreservesUnsupportedAggregate()
    {
        const string source = "static class C { static int Size() => 1; static int[,] M(int rows) => new int[rows, Size()]; }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(NegativeLengthHazardLowering_PreservesUnsupportedAggregate));
        var creation = fixture.Root.DescendantNodes().OfType<ArrayCreationExpressionSyntax>().Single();

        var lowered = SymbolicOperationLowerer.TryLowerNegativeLengthHazard(
            fixture.SemanticModel.GetOperation(creation)!,
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None),
            out var hazard);

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.True);
            Assert.That(hazard.Confidence, Is.EqualTo(SymbolicFactConfidence.Unsupported));
            Assert.That(hazard.Subject, Is.TypeOf<SymbolicVariableTerm>());
            Assert.That(hazard.Origin.Provenance,
                Is.EqualTo("ir.runtime-hazard.array.negative-length.aggregate.unsupported"));
        });
    }

    [Test]
    public void CollectionCardinalityHazardLowering_EmitsExactCountPrecondition()
    {
        const string source = "using System.Collections.Generic; static class C { static int M(Queue<int> values) => values.Peek(); }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(CollectionCardinalityHazardLowering_EmitsExactCountPrecondition));
        var invocation = fixture.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        var lowered = SymbolicOperationLowerer.TryLowerInvalidCollectionCardinalityHazard(
            fixture.SemanticModel.GetOperation(invocation)!,
            new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None),
            out var hazard);

        Assert.Multiple(() =>
        {
            Assert.That(lowered, Is.True);
            Assert.That(hazard.HazardKind, Is.EqualTo(SymbolicRuntimeHazardKind.InvalidCollectionCardinality));
            Assert.That(hazard.PreconditionKind,
                Is.EqualTo(SymbolicExceptionPreconditionKind.InvalidCollectionCardinality));
            Assert.That(hazard.Confidence, Is.EqualTo(SymbolicFactConfidence.Exact));
            Assert.That(hazard.Subject, Is.TypeOf<SymbolicCountTerm>());
            Assert.That(hazard.Origin.Provenance, Is.EqualTo("ir.runtime-hazard.collection-cardinality"));
        });
    }

    [Test]
    public void OperationDescriptors_KeepTypedPayloadAndEvaluationSequence()
    {
        var target = new SymbolicVariableTerm("target", SmtValueKind.Int);
        var source = new SymbolicIntegerConstantTerm(42);
        var origin = new SymbolicOperationOrigin(new TextSpan(10, 5), 3, "test.assignment");
        SymbolicOperationDescriptor descriptor = new SymbolicAssignmentOperation(
            ImmutableArray.Create(new SymbolicAssignmentBinding("target", target, source)),
            ImmutableArray<SymbolicCondition>.Empty,
            SymbolicAssignmentOperationKind.Simple,
            IsChecked: false,
            origin);

        var assignment = descriptor as SymbolicAssignmentOperation;

        Assert.Multiple(() =>
        {
            Assert.That(assignment, Is.Not.Null);
            Assert.That(assignment!.Bindings.Single().Target, Is.SameAs(target));
            Assert.That(assignment.Bindings.Single().Source, Is.SameAs(source));
            Assert.That(assignment.AssignmentKind, Is.EqualTo(SymbolicAssignmentOperationKind.Simple));
            Assert.That(assignment.Origin.Sequence, Is.EqualTo(3));
            Assert.That(assignment.Origin.SourceSpan, Is.EqualTo(new TextSpan(10, 5)));
            Assert.That(assignment.Origin.Provenance, Is.EqualTo("test.assignment"));
        });
    }

    [Test]
    public void LocalDeclarationLoweringAndTransfer_MatchesLegacyState()
    {
        const string source = "static class C { static void M(int input) { int value = input; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(LocalDeclarationLoweringAndTransfer_MatchesLegacyState));
        var declarator = fixture.Root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>().Single();
        var targetSymbol = fixture.SemanticModel.GetDeclaredSymbol(declarator)!;

        var legacyState = new SymbolicState();
        Assert.That(TryApplySymbolAssignment(
            ref legacyState,
            targetSymbol,
            declarator.Initializer!.Value,
            fixture.SemanticModel,
            CancellationToken.None,
            "operation-lowering.declaration"), Is.True);
        var canonical = SymbolicOperationTransfer.ApplyAssignment(
            new SymbolicState(),
            targetSymbol,
            declarator.Initializer.Value,
            fixture.SemanticModel,
            CancellationToken.None,
            provenance: "operation-lowering.declaration");
        Assert.That(canonical.IsExact, Is.True);
        var expected = SymbolicStateDifferentialHarness.Capture(
            legacyState,
            canonical.Support,
            canonical.UnknownReason,
            canonical.Provenance,
            canonical.Truncation);
        var actual = SymbolicStateDifferentialHarness.Capture(
            canonical.State,
            canonical.Support,
            canonical.UnknownReason,
            canonical.Provenance,
            canonical.Truncation);

        SymbolicStateDifferentialHarness.AssertEquivalent(expected, actual, "local declaration");
    }

    [Test]
    public void SimpleAssignmentLoweringAndTransfer_MatchesLegacyState()
    {
        const string source = "static class C { static void M(int input) { int value = 0; value = input + 1; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(SimpleAssignmentLoweringAndTransfer_MatchesLegacyState));
        var assignment = fixture.Root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax>()
            .Single();
        var targetSymbol = fixture.SemanticModel.GetSymbolInfo(assignment.Left).Symbol!;
        var initialState = new SymbolicState(pathConditions: new[]
        {
            new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(targetSymbol), SmtValueKind.Int),
                    new SymbolicIntegerConstantTerm(0)),
                assignment,
                "test.initial"))
        });

        var legacyState = initialState;
        Assert.That(TryApplySymbolAssignment(
            ref legacyState,
            targetSymbol,
            assignment.Right,
            fixture.SemanticModel,
            CancellationToken.None,
            "operation-lowering.assignment"), Is.True);
        var canonical = SymbolicOperationTransfer.ApplyAssignment(
            initialState,
            targetSymbol,
            assignment.Right,
            fixture.SemanticModel,
            CancellationToken.None);
        Assert.That(canonical.IsExact, Is.True);
        var expected = SymbolicStateDifferentialHarness.Capture(
            legacyState,
            canonical.Support,
            canonical.UnknownReason,
            canonical.Provenance,
            canonical.Truncation);
        var actual = SymbolicStateDifferentialHarness.Capture(
            canonical.State,
            canonical.Support,
            canonical.UnknownReason,
            canonical.Provenance,
            canonical.Truncation);

        SymbolicStateDifferentialHarness.AssertEquivalent(expected, actual, "simple assignment");
    }

    [Test]
    public void SymbolicReferenceAssignment_RecordsNullSuppressedLiteral()
    {
        const string source = "static class C { static void M() { string value = null!; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(SymbolicReferenceAssignment_RecordsNullSuppressedLiteral));
        var declarator = fixture.Root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
            .Single();
        var valueExpression = declarator.Initializer!.Value;
        var target = fixture.SemanticModel.GetDeclaredSymbol(declarator)!;

        var transition = SymbolicOperationTransfer.ApplyAssignment(
            new SymbolicState(),
            target,
            valueExpression,
            fixture.SemanticModel,
            CancellationToken.None,
            provenance: "test.null-assignment",
            postconditionProfile: SymbolicAssignmentPostconditionProfile.Symbolic);

        Assert.Multiple(() =>
        {
            Assert.That(transition.IsExact, Is.True);
            Assert.That(transition.State.PathConditions.Any(condition =>
                condition is SymbolicFactCondition
                {
                    Fact.Atom: SymbolicRelationAtom
                    {
                        Operator: SymbolicRelationOperator.Equal,
                        Right: SymbolicNullTerm
                    }
                }), Is.True);
        });
    }

    [TestCase(
        "static class C { static string M(string? input) { string value = input ?? throw new System.Exception(); return value; } }")]
    [TestCase(
        "static class C { static string M(bool condition, string input) { string value = condition ? input : throw new System.Exception(); return value; } }")]
    [TestCase(
        "static class C { static string M(bool condition, string input) { string value = condition ? throw new System.Exception() : input; return value; } }")]
    public void ThrowGuardedAssignment_MatchesStructuralTransfer(string source)
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(ThrowGuardedAssignment_MatchesStructuralTransfer));
        var declarator = fixture.Root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
            .Single();
        var valueExpression = declarator.Initializer!.Value;
        var target = fixture.SemanticModel.GetDeclaredSymbol(declarator)!;
        const string provenance = "test.throw-guarded-assignment";

        var expected = new SymbolicState();
        Assert.That(TryApplySymbolAssignment(
            ref expected,
            target,
            valueExpression,
            fixture.SemanticModel,
            CancellationToken.None,
            provenance), Is.True);
        var actual = SymbolicOperationTransfer.ApplyAssignment(
            new SymbolicState(),
            target,
            valueExpression,
            fixture.SemanticModel,
            CancellationToken.None,
            provenance: provenance,
            bindingProvenance: provenance + ".assigned-value",
            postconditionProfile: SymbolicAssignmentPostconditionProfile.Symbolic);

        SymbolicStateDifferentialHarness.AssertEquivalent(
            SymbolicStateDifferentialHarness.Capture(
                expected,
                actual.Support,
                actual.UnknownReason,
                actual.Provenance,
                actual.Truncation),
            SymbolicStateDifferentialHarness.Capture(
                actual.State,
                actual.Support,
                actual.UnknownReason,
                actual.Provenance,
                actual.Truncation),
            "throw-guarded assignment");
    }

    [Test]
    public void SymbolicNullableAssignment_MatchesLegacyValueParts()
    {
        const string source = "static class C { static void M() { int? value = 5; } }";
        var fixture = RoslynTestFixture.CreateCompilation(
            source,
            nameof(SymbolicNullableAssignment_MatchesLegacyValueParts));
        var declarator = fixture.Root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
            .Single();
        var valueExpression = declarator.Initializer!.Value;
        var targetSymbol = fixture.SemanticModel.GetDeclaredSymbol(declarator)!;
        var symbolName = SymbolicFactFactory.GetSmtVariableName(targetSymbol);
        var targetHasValue = new SymbolicNullableHasValueTerm(symbolName);
        var targetValue = new SymbolicNullableValueTerm(symbolName, SmtValueKind.Int);
        const string provenance = "test.nullable-assignment";
        var expected = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    targetHasValue,
                    new SymbolicBooleanConstantTerm(true)),
                valueExpression,
                provenance + ".nullable.has-value")),
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicNotCondition(new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicTruthAtom(targetHasValue),
                    valueExpression,
                    provenance + ".nullable.value-present",
                    targetSymbol))),
                new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicRelationAtom(
                        SymbolicRelationOperator.Equal,
                        targetValue,
                        new SymbolicIntegerConstantTerm(5)),
                    valueExpression,
                    provenance + ".nullable.value",
                    targetSymbol)))
        });

        var transition = SymbolicOperationTransfer.ApplyAssignment(
            new SymbolicState(),
            targetSymbol,
            valueExpression,
            fixture.SemanticModel,
            CancellationToken.None,
            provenance: provenance,
            postconditionProfile: SymbolicAssignmentPostconditionProfile.Symbolic);

        SymbolicStateDifferentialHarness.AssertEquivalent(
            SymbolicStateDifferentialHarness.Capture(
                expected,
                transition.Support,
                transition.UnknownReason,
                transition.Provenance,
                transition.Truncation),
            SymbolicStateDifferentialHarness.Capture(
                transition.State,
                transition.Support,
                transition.UnknownReason,
                transition.Provenance,
                transition.Truncation),
            "nullable assignment");
    }

    [Test]
    public void TransitionResult_NormalizesStateAndCanonicalizesTruncation()
    {
        var source = SyntaxFactory.ParseExpression("value");
        var value = new SymbolicVariableTerm("value", SmtValueKind.Int);
        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThanOrEqual,
                value,
                new SymbolicIntegerConstantTerm(0)),
            source,
            "test.fact");
        var state = new SymbolicState(new[] { fact, fact });
        var provenance = new SymbolicLoweringProvenance("transfer", new TextSpan(0, 5), "simple");
        var firstLimit = new SymbolicAnalysisTruncationEvent(
            SymbolicAnalysisLimitKind.SwitchFactMerge,
            4,
            6,
            "test.switch",
            8);
        var secondLimit = new SymbolicAnalysisTruncationEvent(
            SymbolicAnalysisLimitKind.IfElseFactMerge,
            2,
            3,
            "test.if",
            4);

        var result = SymbolicOperationTransitionResult.Exact(
            state,
            new[] { provenance },
            new SymbolicAnalysisTruncationInfo(new[] { firstLimit, secondLimit }));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsExact, Is.True);
            Assert.That(result.UnknownReason, Is.EqualTo(SymbolicUnknownReason.None));
            Assert.That(result.State.Facts, Has.Length.EqualTo(1));
            Assert.That(result.Provenance, Is.EqualTo(new[] { provenance }));
            Assert.That(result.Truncation.Events.Select(static item => item.Kind), Is.EqualTo(new[]
            {
                SymbolicAnalysisLimitKind.IfElseFactMerge,
                SymbolicAnalysisLimitKind.SwitchFactMerge
            }));
        });
    }

    [Test]
    public void UnsupportedTransition_RetainsConservativeStateAndReason()
    {
        var state = new SymbolicState(symbolVersions: new[]
        {
            new KeyValuePair<string, int>("value", 2)
        });
        var provenance = new SymbolicLoweringProvenance(
            "operation-transfer",
            new TextSpan(1, 2),
            "unsupported");

        var result = SymbolicOperationTransitionResult.Unsupported(
            state,
            SymbolicUnknownReason.UnsupportedIrEncoding,
            new[] { provenance });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsUnsupported, Is.True);
            Assert.That(result.State.NormalizedProofKey, Is.EqualTo(state.NormalizedProofKey));
            Assert.That(result.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
            Assert.That(result.Truncation, Is.SameAs(SymbolicAnalysisTruncationInfo.None));
        });
        Assert.That(
            () => SymbolicOperationTransitionResult.Unsupported(
                state,
                SymbolicUnknownReason.None,
                new[] { provenance }),
            Throws.ArgumentException);
    }

    [Test]
    public void SymbolicAndPurityAdapters_ProduceTheSameSimpleAssignmentState()
    {
        const string source = "static class C { static void M(int input) { int value = 0; value = input; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(SymbolicAndPurityAdapters_ProduceTheSameSimpleAssignmentState));
        var assignment = fixture.Root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax>()
            .Single();
        var operation = (Microsoft.CodeAnalysis.Operations.IAssignmentOperation)
            fixture.SemanticModel.GetOperation(assignment)!;
        var target = ((Microsoft.CodeAnalysis.Operations.ILocalReferenceOperation)operation.Target).Local;
        var initialState = PurityAnalysisEngine.PurityAnalysisState.Pure;
        var versionedState = initialState.WithSmtSymbolDefinitionVersion(target, operation.Value.Syntax);
        var symbolic = SymbolicOperationTransfer.ApplyAssignment(
            versionedState.PathState,
            target,
            operation.Value.Syntax,
            fixture.SemanticModel,
            CancellationToken.None,
            versionedState.GetSmtSymbolVersion,
            initialState.GetSmtSymbolVersion);
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var containingMethod = (IMethodSymbol)target.ContainingSymbol;
        var context = new PurityAnalysisContext(
            fixture.SemanticModel,
            fixture.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Object),
            pureAttributeSymbol: null,
            allowSynchronizationAttributeSymbol: null,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
            new Dictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult>(SymbolEqualityComparer.Default),
            containingMethod,
            ImmutableList<IPurityRule>.Empty,
            CancellationToken.None,
            purityService: null,
            smtAnalysis);
        var created = PurityAssignmentEnvelope.TryCreate(operation, initialState, context, out var envelope);
        var purity = PurityAssignmentTransition.Apply(envelope, initialState, context);
        var purityTransition = SymbolicOperationTransfer.ApplyAssignment(
            versionedState.PathState,
            target,
            operation.Value.Syntax,
            fixture.SemanticModel,
            CancellationToken.None,
            versionedState.GetSmtSymbolVersion,
            initialState.GetSmtSymbolVersion,
            provenance: "analyzer.assignment",
            bindingProvenance: "analyzer.assignment",
            evidenceKey: "analyzer.assignment.value");

        Assert.That(created, Is.True);
        Assert.That(symbolic.IsExact, Is.True);
        Assert.That(purityTransition.IsExact, Is.True);
        Assert.That(purity.PathState.NormalizedProofKey, Is.EqualTo(symbolic.State.NormalizedProofKey));
    }

    [Test]
    public void PurityAssignmentEnvelope_LocalVersionUsesRhsSyntax()
    {
        const string source = "static class C { static void M(int input) { int value = 0; value = input + 1; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(PurityAssignmentEnvelope_LocalVersionUsesRhsSyntax));
        var syntax = fixture.Root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var operation = (Microsoft.CodeAnalysis.Operations.IAssignmentOperation)fixture.SemanticModel.GetOperation(syntax)!;
        var local = ((Microsoft.CodeAnalysis.Operations.ILocalReferenceOperation)operation.Target).Local;
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var context = CreatePurityContext(fixture, (IMethodSymbol)local.ContainingSymbol, smtAnalysis);

        Assert.That(PurityAssignmentEnvelope.TryCreate(operation, PurityAnalysisEngine.PurityAnalysisState.Pure,
            context, out var envelope), Is.True);
        var actual = PurityAssignmentTransition.Apply(
            envelope, PurityAnalysisEngine.PurityAnalysisState.Pure, context);

        Assert.That(actual.GetSmtSymbolVersion(local),
            Is.EqualTo(SymbolicOperationTransferKernel.GetDefinitionVersion(operation.Value.Syntax.Span)));
    }

    [Test]
    public void PurityAssignmentEnvelope_RefClosureCapturesLifetimeAliasesFromOneSnapshot()
    {
        const string source = """
            using System;
            sealed class D : IDisposable { public void Dispose() { } }
            static class C
            {
                static void M()
                {
                    D owner = new D();
                    D ownedAlias = owner;
                    D disposedAlias = owner;
                    ref D first = ref owner;
                    ref D second = ref first;
                    second = new D();
                }
            }
            """;
        var fixture = RoslynTestFixture.CreateCompilation(
            source, nameof(PurityAssignmentEnvelope_RefClosureCapturesLifetimeAliasesFromOneSnapshot));
        var syntax = fixture.Root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var operation = (Microsoft.CodeAnalysis.Operations.IAssignmentOperation)fixture.SemanticModel.GetOperation(syntax)!;
        var locals = fixture.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .ToDictionary(node => node.Identifier.ValueText,
                node => (ILocalSymbol)fixture.SemanticModel.GetDeclaredSymbol(node)!);
        var initial = PurityAnalysisEngine.PurityAnalysisState.Pure;
        var secondTerm = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(locals["second"], initial);
        var firstTerm = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(locals["first"], initial);
        var ownedAliasTerm = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(locals["ownedAlias"], initial);
        var disposedAliasTerm = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(locals["disposedAlias"], initial);
        var disposedResource = new SymbolicVariableTerm("disposed-resource", SmtValueKind.Reference);
        initial = initial.WithPathState(new SymbolicState(new[]
        {
            SymbolicFact.Exact(new SymbolicResourceLifetimeAtom(
                secondTerm, SymbolicResourceLifetimeState.Owned), syntax, "test.owned", locals["second"]),
            SymbolicFact.Exact(new SymbolicAliasAtom(secondTerm, ownedAliasTerm, true),
                syntax, "test.owned.alias", locals["ownedAlias"]),
            SymbolicFact.Exact(new SymbolicDisposalAtom(
                disposedResource, SymbolicDisposalState.Disposed), syntax, "test.disposed"),
            SymbolicFact.Exact(new SymbolicAliasAtom(firstTerm, disposedResource, true),
                syntax, "test.disposed.bridge", locals["second"]),
            SymbolicFact.Exact(new SymbolicAliasAtom(firstTerm, disposedAliasTerm, true),
                syntax, "test.disposed.alias", locals["disposedAlias"])
        }));
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var context = CreatePurityContext(fixture, (IMethodSymbol)locals["second"].ContainingSymbol, smtAnalysis);

        Assert.That(PurityAssignmentEnvelope.TryCreate(operation, initial, context, out var envelope), Is.True);
        var actual = PurityAssignmentTransition.Apply(envelope, initial, context);

        Assert.Multiple(() =>
        {
            Assert.That(PuritySymbolicStateFacts.HasSymbolicOwnedFactForSymbol(locals["ownedAlias"], actual), Is.True);
            Assert.That(PurityResourceStateFacts.HasDisposedResourceFactForTerm(
                PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(locals["disposedAlias"], actual),
                actual), Is.True);
        });
    }

    [Test]
    public void ExactAliasComponent_IsRootFirstSymmetricCycleSafeAndExactPositiveOnly()
    {
        var source = SyntaxFactory.IdentifierName("alias");
        var root = new SymbolicVariableTerm("root", SmtValueKind.Reference);
        var reverse = new SymbolicVariableTerm("reverse", SmtValueKind.Reference);
        var chained = new SymbolicVariableTerm("chained", SmtValueKind.Reference);
        var mayNot = new SymbolicVariableTerm("may-not", SmtValueKind.Reference);
        var approximate = new SymbolicVariableTerm("approximate", SmtValueKind.Reference);
        var negative = new SymbolicVariableTerm("negative", SmtValueKind.Reference);
        var facts = ImmutableArray.Create(
            Exact(new SymbolicAliasAtom(reverse, root, true), source, "test.alias.reverse"),
            Exact(new SymbolicAliasAtom(reverse, chained, true), source, "test.alias.forward"),
            Exact(new SymbolicAliasAtom(chained, root, true), source, "test.alias.cycle"),
            Exact(new SymbolicAliasAtom(chained, mayNot, false), source, "test.alias.may-not"),
            Exact(new SymbolicAliasAtom(chained, approximate, true), source, "test.alias.approximate") with
                { Confidence = SymbolicFactConfidence.Approximate },
            Exact(new SymbolicAliasAtom(chained, negative, true), source, "test.alias.negative").Negate());

        var actual = SymbolicStateMerger.EnumerateExactAliasComponent(root, facts).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(new[] { root, reverse, chained }));
            Assert.That(SymbolicStateMerger.EnumerateExactAliasComponent(root, facts).Any(term => Equals(term, root)),
                Is.True);
            Assert.That(SymbolicStateMerger.EnumerateExactAliasComponent(root, facts).Any(
                term => Equals(term, mayNot) || Equals(term, approximate) || Equals(term, negative)),
                Is.False);
        });
    }

    [Test]
    public void ExactAliasComponent_AnalyzerQueriesPreserveDirectionalAndLifetimeSemantics()
    {
        const string source = "static class C { static void M() { object owner = null!, ownerAlias = null!, " +
                              "borrow = null!, borrowAlias = null!, resource = null!, resourceAlias = null!; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(ExactAliasComponent_AnalyzerQueriesPreserveDirectionalAndLifetimeSemantics));
        var locals = fixture.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .ToDictionary(node => node.Identifier.ValueText,
                node => (ILocalSymbol)fixture.SemanticModel.GetDeclaredSymbol(node)!);
        var initial = PurityAnalysisEngine.PurityAnalysisState.Pure;
        SymbolicTerm Term(string name) => PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(locals[name], initial);
        var owner = Term("owner");
        var ownerAlias = Term("ownerAlias");
        var borrow = Term("borrow");
        var borrowAlias = Term("borrowAlias");
        var resource = Term("resource");
        var resourceAlias = Term("resourceAlias");
        var returned = new SymbolicVariableTerm("returned", SmtValueKind.Reference);
        var released = new SymbolicVariableTerm("released", SmtValueKind.Reference);
        var allPathReleased = new SymbolicVariableTerm("all-path", SmtValueKind.Reference);
        var syntax = fixture.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var state = initial.WithPathState(new SymbolicState(new[]
        {
            Exact(new SymbolicAliasAtom(ownerAlias, owner, true), syntax, "test.owner.alias"),
            Exact(new SymbolicOwnershipAtom(owner, false), syntax, "test.owner"),
            Exact(new SymbolicAliasAtom(borrow, borrowAlias, true), syntax, "test.borrow.alias"),
            Exact(new SymbolicBorrowAtom(owner, borrow, SymbolicBorrowKind.Shared), syntax, "test.borrow"),
            Exact(new SymbolicAliasAtom(resourceAlias, resource, true), syntax, "test.resource.alias"),
            Exact(new SymbolicDisposalAtom(resource, SymbolicDisposalState.Disposed), syntax, "test.disposed"),
            Exact(new SymbolicResourceLifetimeAtom(returned, SymbolicResourceLifetimeState.Returned),
                syntax, "test.returned"),
            Exact(new SymbolicResourceLifetimeAtom(released, SymbolicResourceLifetimeState.Released),
                syntax, "test.released"),
            Exact(new SymbolicResourceLifetimeAtom(allPathReleased, SymbolicResourceLifetimeState.Released),
                syntax, "analyzer.resource.merge.all-path-release")
        }));

        Assert.Multiple(() =>
        {
            Assert.That(PuritySymbolicStateFacts.HasSymbolicOwnedFactForSymbol(locals["ownerAlias"], state), Is.True);
            Assert.That(PuritySymbolicStateFacts.HasSymbolicBorrowFactForLocal(
                locals["borrowAlias"], state, SymbolicBorrowKind.Shared), Is.True);
            Assert.That(PuritySymbolicStateFacts.HasSymbolicBorrowFactForLocal(
                locals["borrowAlias"], state, SymbolicBorrowKind.Mutable), Is.False);
            Assert.That(PuritySymbolicStateFacts.HasSymbolicBorrowerFactForSymbol(locals["ownerAlias"], state), Is.True);
            Assert.That(PurityResourceStateFacts.HasDisposedResourceFactForTerm(resourceAlias, state), Is.True);
            Assert.That(PurityResourceStateFacts.HasReleasedResourceFact(resourceAlias, state), Is.True);
            Assert.That(PurityResourceStateFacts.HasDisposedResourceFactForTerm(returned, state), Is.False);
            Assert.That(PurityResourceStateFacts.HasReleasedResourceFact(returned, state), Is.True);
            Assert.That(PurityResourceStateFacts.HasDisposedResourceFactForTerm(released, state), Is.False);
            Assert.That(PurityResourceStateFacts.HasReleasedResourceFact(released, state), Is.True);
            Assert.That(PurityResourceStateFacts.HasDisposedResourceFactForTerm(allPathReleased, state), Is.True);
        });
    }

    [Test]
    public void ExactAliasComponent_AllPathReleaseAndOnePathOutstandingRemainDistinct()
    {
        const string source = "static class C { static void M() { object resource = null!, alias = null!; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(ExactAliasComponent_AllPathReleaseAndOnePathOutstandingRemainDistinct));
        var declarations = fixture.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToArray();
        var resourceSymbol = (ILocalSymbol)fixture.SemanticModel.GetDeclaredSymbol(declarations[0])!;
        var aliasSymbol = (ILocalSymbol)fixture.SemanticModel.GetDeclaredSymbol(declarations[1])!;
        var initial = PurityAnalysisEngine.PurityAnalysisState.Pure;
        var resource = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(resourceSymbol, initial);
        var alias = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(aliasSymbol, initial);
        var syntax = fixture.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var owned = SymbolicFact.Exact(new SymbolicResourceLifetimeAtom(
            resource, SymbolicResourceLifetimeState.Owned), syntax, "test.owned", resourceSymbol);
        var aliasFact = SymbolicFact.Exact(new SymbolicAliasAtom(
            resource, alias, true), syntax, "test.alias", aliasSymbol);
        var disposedResource = SymbolicFact.Exact(new SymbolicDisposalAtom(
            resource, SymbolicDisposalState.Disposed), syntax, "test.disposed.root", resourceSymbol);
        var disposedAlias = SymbolicFact.Exact(new SymbolicDisposalAtom(
            alias, SymbolicDisposalState.Disposed), syntax, "test.disposed.alias", aliasSymbol);
        var first = new SymbolicState(new[] { owned, aliasFact, disposedResource });
        var secondReleased = new SymbolicState(new[] { owned, aliasFact, disposedAlias });
        var secondOutstanding = new SymbolicState(new[] { owned, aliasFact });
        var allPath = SymbolicStateMerger.MergePathStatesAcrossAll(
            new[] { first, secondReleased }, SymbolicStateMerger.AreEvidenceEquivalentFacts, 7);
        var onePath = SymbolicStateMerger.MergePathStatesAcrossAll(
            new[] { first, secondOutstanding }, SymbolicStateMerger.AreEvidenceEquivalentFacts, 7);
        var allPathState = initial.WithPathState(allPath);
        var onePathState = initial.WithPathState(onePath);

        Assert.Multiple(() =>
        {
            Assert.That(SymbolicStateMerger.HasExactResourceRelease(allPath, resource), Is.True);
            Assert.That(PurityResourceStateFacts.HasDisposedResourceFactForTerm(resource, allPathState), Is.True);
            Assert.That(SymbolicStateMerger.HasExactResourceRelease(onePath, resource), Is.False);
            Assert.That(PurityResourceStateFacts.HasDisposedResourceFactForTerm(resource, onePathState), Is.False);
            Assert.That(PuritySymbolicStateFacts.HasSymbolicOwnedFactForSymbol(resourceSymbol, onePathState), Is.True);
        });
    }

    [Test]
    public void PurityAssignmentEnvelope_DeclarationKeepsRecoveredDelegateTargetWhenResolutionIsNull()
    {
        const string source = """
            using System;
            static class C
            {
                static readonly Action Callback = GetCallback();
                static Action GetCallback() => () => { };
                static void PureTarget() { }
                static void M() { Action action = Callback; }
            }
            """;
        var fixture = RoslynTestFixture.CreateCompilation(
            source, nameof(PurityAssignmentEnvelope_DeclarationKeepsRecoveredDelegateTargetWhenResolutionIsNull));
        var declaration = fixture.Root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();
        var operation = (Microsoft.CodeAnalysis.Operations.IVariableDeclarationGroupOperation)
            fixture.SemanticModel.GetOperation(declaration)!;
        var local = operation.Declarations.Single().Declarators.Single().Symbol;
        var knownTarget = (IMethodSymbol)fixture.SemanticModel.GetDeclaredSymbol(
            fixture.Root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "PureTarget"))!;
        var initial = PurityAnalysisEngine.PurityAnalysisState.Pure.WithDelegateTarget(
            local, PurityAnalysisEngine.PotentialTargets.FromSingle(knownTarget));
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var context = CreatePurityContext(fixture, (IMethodSymbol)local.ContainingSymbol, smtAnalysis);

        Assert.That(PurityAssignmentEnvelope.TryCreate(operation, initial, context, out var envelope), Is.True);
        var actual = PurityAssignmentTransition.Apply(envelope, initial, context);

        Assert.That(actual.DelegateTargetMap[local],
            Is.EqualTo(PurityAnalysisEngine.PotentialTargets.FromSingle(knownTarget)));
    }

    [Test]
    public void TransferKernel_PreservesSequenceOrderAndRejectsReordering()
    {
        var firstTarget = new SymbolicVariableTerm("first", SmtValueKind.Int);
        var secondTarget = new SymbolicVariableTerm("second", SmtValueKind.Int);
        var first = Assignment(firstTarget, 1, sequence: 0, "first");
        var second = Assignment(secondTarget, 2, sequence: 1, "second");
        var ordered = SymbolicOperationTransferKernel.Apply(
            new SymbolicState(),
            new SymbolicOperationSequence(ImmutableArray.Create<SymbolicOperationDescriptor>(first, second)));
        var reordered = SymbolicOperationTransferKernel.Apply(
            new SymbolicState(),
            new SymbolicOperationSequence(ImmutableArray.Create<SymbolicOperationDescriptor>(second, first)));

        Assert.Multiple(() =>
        {
            Assert.That(ordered.IsExact, Is.True);
            Assert.That(
                ordered.Provenance.Select(static item => item.Detail),
                Is.EqualTo(new[] { "first", "second" }));
            Assert.That(reordered.IsUnsupported, Is.True);
            Assert.That(reordered.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
        });
    }

    [Test]
    public void TransferKernel_InvalidationRemovesVersionedReferencesOnly()
    {
        var state = new SymbolicState(new[]
        {
            Relation("value@v3", 3),
            Relation("other", 4)
        });

        var result = SymbolicOperationTransferKernel.Invalidate(
            state,
            ImmutableArray.Create(new SymbolicInvalidationTarget("value")),
            default,
            "test.invalidate");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsExact, Is.True);
            Assert.That(result.State.Facts.Length, Is.EqualTo(1));
            Assert.That(result.State.Facts.Single().Atom,
                Is.EqualTo(Relation("other", 4).Atom));
        });
    }

    [Test]
    public void TransferKernel_InvalidationOwnsDefinitionVersionUpdate()
    {
        var state = new SymbolicState(
            new[] { Relation("value@v3", 3) },
            symbolVersions: new[] { new KeyValuePair<string, int>("value", 3) });

        var result = SymbolicOperationTransferKernel.Invalidate(
            state,
            ImmutableArray.Create(new SymbolicInvalidationTarget("value", DefinitionVersion: 8)),
            default,
            "test.invalidate-version");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsExact, Is.True);
            Assert.That(result.State.Facts, Is.Empty);
            Assert.That(result.State.SymbolVersions["value"], Is.EqualTo(8));
        });
    }

    [TestCase(0, 2)]
    [TestCase(17, 36)]
    public void TransferKernel_DefinitionVersionsAreStableEvenValues(int spanStart, int expectedVersion)
    {
        var span = new TextSpan(spanStart, 1);

        Assert.That(SymbolicOperationTransferKernel.GetDefinitionVersion(span), Is.EqualTo(expectedVersion));
    }

    [Test]
    public void TransferKernel_PropagatesRequestedDirectSourceFacts()
    {
        var source = new SymbolicVariableTerm("source", SmtValueKind.Int);
        var target = new SymbolicVariableTerm("target", SmtValueKind.Int);
        var initial = new SymbolicState(pathConditions: new[]
        {
            new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.GreaterThan,
                    source,
                    new SymbolicIntegerConstantTerm(0)),
                SyntaxFactory.ParseExpression("source"),
                "test.source-positive"))
        });
        var operation = new SymbolicAssignmentOperation(
            ImmutableArray.Create(new SymbolicAssignmentBinding(
                "target",
                target,
                source,
                PropagateSourceFacts: true)),
            ImmutableArray<SymbolicCondition>.Empty,
            SymbolicAssignmentOperationKind.Simple,
            IsChecked: false,
            new SymbolicOperationOrigin(default, 0, "test.snapshot"));

        var transition = SymbolicOperationTransferKernel.Apply(
            initial,
            SymbolicOperationSequence.Single(operation));

        Assert.That(transition.State.PathConditions.Any(condition =>
            condition is SymbolicFactCondition
            {
                Fact.Atom: SymbolicRelationAtom
                {
                    Operator: SymbolicRelationOperator.GreaterThan,
                    Left: SymbolicVariableTerm { Name: "target" }
                }
            }), Is.True);
    }

    [TestCase(0, typeof(SymbolicAliasAtom))]
    [TestCase(1, typeof(SymbolicBorrowAtom))]
    [TestCase(2, typeof(SymbolicBorrowAtom))]
    public void TransferKernel_AppliesAliasAndBorrowLifetimeEvents(
        int kindValue,
        Type expectedAtomType)
    {
        var kind = kindValue switch
        {
            0 => SymbolicLifetimeOperationKind.Alias,
            1 => SymbolicLifetimeOperationKind.BorrowShared,
            _ => SymbolicLifetimeOperationKind.BorrowMutable
        };
        var operation = new SymbolicLifetimeOperation(
            new SymbolicVariableTerm("owner", SmtValueKind.Reference),
            kind,
            new SymbolicVariableTerm("related", SmtValueKind.Reference),
            SymbolicEscapeKind.RefAlias,
            null,
            "test.evidence",
            new SymbolicOperationOrigin(default, 0, "test.lifetime"));

        var result = SymbolicOperationTransferKernel.Apply(
            new SymbolicState(),
            SymbolicOperationSequence.Single(operation));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsExact, Is.True);
            Assert.That(result.State.Facts.Single().Atom, Is.TypeOf(expectedAtomType));
            Assert.That(result.State.Facts.Single().Provenance, Is.EqualTo("test.lifetime"));
            Assert.That(result.State.Facts.Single().EvidenceKey, Is.EqualTo("test.evidence"));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void TransferKernel_OwnershipLifetimeBundlesMatchLegacyFacts(int caseValue)
    {
        var source = SyntaxFactory.ParseExpression("resource");
        var term = new SymbolicVariableTerm("resource", SmtValueKind.Reference);
        const string provenance = "test.resource";
        const string evidence = "test.resource.evidence";
        var (kind, legacyFacts) = caseValue switch
        {
            0 => (SymbolicLifetimeOperationKind.CreateOwnedValue,
                ImmutableArray.Create(
                    Exact(new SymbolicFreshnessAtom(term), source, provenance + ".fresh", evidence),
                    Exact(new SymbolicOwnershipAtom(term, false), source, provenance + ".owned", evidence))),
            1 => (SymbolicLifetimeOperationKind.CreateOwned,
                OwnedFacts(term, source, provenance, evidence)),
            2 => (SymbolicLifetimeOperationKind.AcquireDisposable,
                OwnedFacts(term, source, provenance, evidence).Add(
                    Exact(
                        new SymbolicDisposalAtom(term, SymbolicDisposalState.NotDisposed),
                        source,
                        provenance + ".disposal",
                        evidence))),
            3 => (SymbolicLifetimeOperationKind.Return,
                ImmutableArray.Create(
                    Exact(new SymbolicReturnedOwnershipAtom(term), source, provenance, evidence),
                    Exact(
                        new SymbolicResourceLifetimeAtom(term, SymbolicResourceLifetimeState.Returned),
                        source,
                        provenance + ".lifetime",
                        evidence))),
            _ => (SymbolicLifetimeOperationKind.Dispose,
                ImmutableArray.Create(
                    Exact(
                        new SymbolicDisposalAtom(term, SymbolicDisposalState.Disposed),
                        source,
                        provenance,
                        evidence),
                    Exact(
                        new SymbolicResourceLifetimeAtom(term, SymbolicResourceLifetimeState.Released),
                        source,
                        provenance + ".lifetime",
                        evidence)))
        };

        var result = SymbolicOperationTransferKernel.TransitionLifetime(
            new SymbolicState(),
            term,
            kind,
            source.Span,
            provenance,
            evidenceKey: evidence);

        Assert.That(result.State.Facts, Is.EqualTo(legacyFacts));
    }

    [TestCase(false, 3)]
    [TestCase(true, 2)]
    public void TransferKernel_LifetimeTransitionReplacesExclusiveResourceState(
        bool dispose,
        int expectedFactCount)
    {
        var source = SyntaxFactory.ParseExpression("resource");
        var resource = new SymbolicVariableTerm("resource", SmtValueKind.Reference);
        var initial = new SymbolicState(new[]
        {
            Exact(
                new SymbolicResourceLifetimeAtom(resource, SymbolicResourceLifetimeState.Owned),
                source,
                "test.owned"),
            Exact(
                new SymbolicDisposalAtom(resource, SymbolicDisposalState.NotDisposed),
                source,
                "test.not-disposed")
        });

        var result = SymbolicOperationTransferKernel.TransitionLifetime(
            initial,
            resource,
            dispose ? SymbolicLifetimeOperationKind.Dispose : SymbolicLifetimeOperationKind.Return,
            source.Span,
            "test.transition");

        Assert.Multiple(() =>
        {
            Assert.That(result.State.Facts.Length, Is.EqualTo(expectedFactCount));
            Assert.That(result.State.Facts.Any(static fact =>
                fact.Atom is SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned }), Is.False);
            Assert.That(result.State.Facts.Any(fact => fact.Atom is SymbolicDisposalAtom
                {
                    State: SymbolicDisposalState.NotDisposed
                }), Is.EqualTo(!dispose));
        });
    }

    private static SymbolicAssignmentOperation Assignment(
        SymbolicTerm target,
        long value,
        int sequence,
        string provenance)
    {
        return new SymbolicAssignmentOperation(
            ImmutableArray.Create(new SymbolicAssignmentBinding(
                ((SymbolicVariableTerm)target).Name,
                target,
                new SymbolicIntegerConstantTerm(value))),
            ImmutableArray<SymbolicCondition>.Empty,
            SymbolicAssignmentOperationKind.Simple,
            IsChecked: false,
            new SymbolicOperationOrigin(default, sequence, provenance));
    }

    private static SymbolicFact Relation(string name, long value)
    {
        return new SymbolicFact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicVariableTerm(name, SmtValueKind.Int),
                new SymbolicIntegerConstantTerm(value)),
            true,
            SymbolicFactConfidence.Exact,
            "test.relation",
            default,
            null,
            null);
    }

    private static bool TryApplySymbolAssignment(
        ref SymbolicState state,
        ISymbol target,
        ExpressionSyntax value,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance) =>
        semanticModel.GetOperation(value, cancellationToken) is { } operation &&
        SymbolicCfgProgramPointStateCollector.TryApplyAssignment(
            ref state,
            target,
            operation,
            guard: null,
            allowGuardedReferenceAssignments: true,
            allowGuardMutation: true,
            semanticModel,
            cancellationToken,
            provenance,
            out _);

    private static PurityAnalysisContext CreatePurityContext(
        RoslynTestFixture.CompilationFixture fixture,
        IMethodSymbol containingMethod,
        SmtAnalysisService smtAnalysis) =>
        new(
            fixture.SemanticModel,
            fixture.Compilation.GetSpecialType(SpecialType.System_Object),
            pureAttributeSymbol: null,
            allowSynchronizationAttributeSymbol: null,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
            new Dictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult>(SymbolEqualityComparer.Default),
            containingMethod,
            ImmutableList<IPurityRule>.Empty,
            CancellationToken.None,
            purityService: null,
            smtAnalysis);

    private static ImmutableArray<SymbolicFact> OwnedFacts(
        SymbolicTerm term,
        SyntaxNode source,
        string provenance,
        string evidence)
    {
        return ImmutableArray.Create(
            Exact(new SymbolicFreshnessAtom(term), source, provenance + ".fresh", evidence),
            Exact(new SymbolicOwnershipAtom(term, false), source, provenance + ".owned", evidence),
            Exact(
                new SymbolicResourceLifetimeAtom(term, SymbolicResourceLifetimeState.Owned),
                source,
                provenance + ".lifetime",
                evidence));
    }

    private static SymbolicFact Exact(
        SymbolicAtom atom,
        SyntaxNode source,
        string provenance,
        string? evidenceKey = null)
    {
        return new SymbolicFact(
            atom,
            true,
            SymbolicFactConfidence.Exact,
            provenance,
            source.Span,
            null,
            evidenceKey);
    }
}
