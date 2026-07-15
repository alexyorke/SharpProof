using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicOperationTransferModelTests
{
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
        var declaration = (Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclarationSyntax)declarator.Parent!;
        var statement = (Microsoft.CodeAnalysis.CSharp.Syntax.LocalDeclarationStatementSyntax)declaration.Parent!;
        var operation = fixture.SemanticModel.GetOperation(declarator)!;
        var context = new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None);
        var lowered = SymbolicOperationLowerer.Lower(operation, context, context);
        Assert.That(lowered.IsExact, Is.True);

        var legacyState = new SymbolicState();
        SymbolicAssignmentStateTransfer.AddVariableDeclarationInitializerStateFacts(
            ref legacyState,
            declaration,
            statement,
            fixture.SemanticModel,
            CancellationToken.None,
            "operation-lowering.declaration");
        var canonical = SymbolicOperationTransferKernel.Apply(new SymbolicState(), lowered.Value!);
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
        var operation = fixture.SemanticModel.GetOperation(assignment)!;
        var targetSymbol = fixture.SemanticModel.GetSymbolInfo(assignment.Left).Symbol!;
        var context = new SymbolicLoweringContext(fixture.SemanticModel, CancellationToken.None);
        var lowered = SymbolicOperationLowerer.Lower(operation, context, context);
        Assert.That(lowered.IsExact, Is.True);
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
        SymbolicAssignmentStateTransfer.AddAssignedValueStateFacts(
            ref legacyState,
            targetSymbol,
            assignment.Right,
            fixture.SemanticModel,
            CancellationToken.None,
            "operation-lowering.assignment");
        var canonical = SymbolicOperationTransferKernel.Apply(initialState, lowered.Value!);
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
        var operation = fixture.SemanticModel.GetOperation(assignment)!;
        var symbolic = SymbolicOperationTransferAdapter.Apply(
            new SymbolicState(),
            operation,
            fixture.SemanticModel,
            CancellationToken.None);
        var purity = PurityOperationTransferAdapter.Apply(
            PurityAnalysisEngine.PurityAnalysisState.Pure,
            operation,
            fixture.SemanticModel,
            CancellationToken.None,
            PurityAnalysisEngine.PurityAnalysisState.Pure,
            out var purityTransition);

        Assert.That(symbolic.IsExact, Is.True);
        Assert.That(purityTransition.IsExact, Is.True);
        Assert.That(purity.PathState.NormalizedProofKey, Is.EqualTo(symbolic.State.NormalizedProofKey));
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
}
