using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Dataflow;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ManagedAbstractFlowTests
{
    [Test]
    public void UninitializedLocalsAreTrackedBeforeAssignment()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Calls() {
                    try {
                    }
                    catch (System.Exception error) {
                    }
                }
            }
            """);
        var (_, root, _, analysis) = AnalyzeCalls(compilation);
        Assert.That(
            root.Descendants().OfType<IVariableDeclaratorOperation>()
                .Any(static declarator => declarator.Initializer == null),
            Is.True);

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
    }

    [Test]
    public void MultidimensionalArrayEvaluationRemainsNonNull()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int[,] Calls() => new int[1, 2];
            }
            """);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var creation = (IArrayCreationOperation)model.GetOperation(
            syntax.ExpressionBody!.Expression)!;

        var value = ManagedAbstractFlow.ForCompilation(compilation)
            .Evaluate(creation, ManagedFlowState.Empty);

        Assert.That(value.IsDefinitelyNonNull, Is.True);
    }

    [Test]
    public void IntrinsicArrayLengthUsesTheReceiverTypeWhenCardinalityIsUnknown()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int Calls(int[] value) => value.Length;
            }
            """);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var property = (IPropertyReferenceOperation)model.GetOperation(
            syntax.ExpressionBody!.Expression)!;
        var value = ManagedAbstractFlow.ForCompilation(compilation)
            .Evaluate(
                property,
                ManagedFlowState.Empty.Set(
                    method.Parameters.Single(),
                    ManagedAbstractValue.NonNull));

        Assert.That(value.TryGetInteger(out var interval), Is.True);
        Assert.That(
            interval,
            Is.EqualTo(IntervalValue.Range(0, int.MaxValue)));
    }

    [Test]
    public void UnaryBooleanNegationUsesTheBooleanAbstractValue()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static bool Calls(bool value) => !value;
            }
            """);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var unary = (IUnaryOperation)model.GetOperation(
            syntax.ExpressionBody!.Expression)!;
        var value = ManagedAbstractFlow.ForCompilation(compilation)
            .Evaluate(
                unary,
                ManagedFlowState.Empty.Set(
                    method.Parameters.Single(),
                    ManagedAbstractValue.Boolean(true)));

        Assert.That(value.TryGetBoolean(out var result), Is.True);
        Assert.That(result, Is.False);
    }

    [Test]
    public void UnaryBooleanNegationPreservesUnknownBooleanDomain()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static bool Calls(bool value) => !value;
            }
            """);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var unary = (IUnaryOperation)model.GetOperation(
            syntax.ExpressionBody!.Expression)!;
        var value = ManagedAbstractFlow.ForCompilation(compilation)
            .Evaluate(
                unary,
                ManagedFlowState.Empty.Set(
                    ((IMethodSymbol)model.GetDeclaredSymbol(syntax)!).Parameters.Single(),
                    ManagedAbstractValue.BooleanUnknown));

        Assert.That(value.IsBoolean, Is.True);
        Assert.That(value.TryGetBoolean(out _), Is.False);
    }

    [Test]
    public void UnaryIntegerNegationOfUnknownUsesTheTypeTopValue()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int Calls(int value) => -value;
            }
            """);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var unary = (IUnaryOperation)model.GetOperation(
            syntax.ExpressionBody!.Expression)!;
        var value = ManagedAbstractFlow.ForCompilation(compilation)
            .Evaluate(
                unary,
                ManagedFlowState.Empty.Set(
                    method.Parameters.Single(),
                    ManagedAbstractValue.Unknown));

        Assert.That(value.TryGetInteger(out var interval), Is.True);
        Assert.That(
            interval,
            Is.EqualTo(IntervalValue.Range(int.MinValue, int.MaxValue)));
    }

    [Test]
    public void NullTestsUseKnownReferenceFacts()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static string? Calls(object value) => value?.ToString();
            }
            """);
        var (method, root, graph) = GetCallsContext(compilation);
        var operation = graph.Blocks
            .SelectMany(static block => block.Operations.Append(block.BranchValue)
                .Where(static operation => operation != null)!)
            .SelectMany(static operation => operation!.DescendantsAndSelf())
            .OfType<IIsNullOperation>()
            .Single();
        var flow = ManagedAbstractFlow.ForCompilation(compilation);
        ManagedFlowState State(ManagedAbstractValue value)
        {
            return operation.Operand is IFlowCaptureReferenceOperation capture
                ? ManagedFlowState.Empty.Set(capture.Id, value)
                : ManagedFlowState.Empty.Set(method.Parameters.Single(), value);
        }

        var nullResult = flow.Evaluate(
            operation,
            State(ManagedAbstractValue.Null));
        var nonNullResult = flow.Evaluate(
            operation,
            State(ManagedAbstractValue.NonNull));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nullResult.TryGetBoolean(out var nullTest), Is.True);
            Assert.That(nullTest, Is.True);
            Assert.That(nonNullResult.TryGetBoolean(out var nonNullTest), Is.True);
            Assert.That(nonNullTest, Is.False);
        }
    }

    [Test]
    public void UnknownConditionalAndMaybeNullCoalesceRemainExplicit()
    {
        var conditionalCompilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Calls(bool condition) {
                    if (condition) {
                    }
                }
            }
            """);
        var conditionalSyntax = conditionalCompilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var conditionalModel = conditionalCompilation.GetSemanticModel(
            conditionalSyntax.SyntaxTree);
        var conditional = conditionalModel.GetOperation(conditionalSyntax)!
            .Descendants().OfType<IConditionalOperation>().Single();
        var conditionalValue = ManagedAbstractFlow.ForCompilation(conditionalCompilation)
            .Evaluate(conditional, ManagedFlowState.Empty);

        var coalesceCompilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static object Calls(object value) => value ?? new object();
            }
            """);
        var coalesceSyntax = coalesceCompilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var coalesceModel = coalesceCompilation.GetSemanticModel(
            coalesceSyntax.SyntaxTree);
        var coalesce = (ICoalesceOperation)coalesceModel.GetOperation(
            coalesceSyntax.ExpressionBody!.Expression)!;
        var coalesceMethod = (IMethodSymbol)coalesceModel.GetDeclaredSymbol(
            coalesceSyntax)!;
        var coalesceValue = ManagedAbstractFlow.ForCompilation(coalesceCompilation)
            .Evaluate(
                coalesce,
                ManagedFlowState.Empty.Set(
                    coalesceMethod.Parameters.Single(),
                    ManagedAbstractValue.Reference(NullnessValue.MaybeNull)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(conditionalValue.IsUnknown, Is.True);
            Assert.That(coalesceValue.IsDefinitelyNull, Is.False);
            Assert.That(coalesceValue.IsDefinitelyNonNull, Is.True);
        }
    }

    [Test]
    public void MutatedValuesCannotBeEvaluatedAtTheirOrigin()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Calls(int value) {
                    value = 1;
                }
            }
            """);
        var (_, root, _, analysis) = AnalyzeCalls(compilation);
        var assignment = root.Descendants()
            .OfType<ISimpleAssignmentOperation>().Single();

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(
            analysis.Result!.TryEvaluateAtOrigin(assignment, assignment, out _),
            Is.False);
    }

    [Test]
    public void NullResultApiSpecificationProducesNullFact()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static string Return() => null!;
                public static string Calls() => Return();
            }
            """);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Calls");
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var invocation = (IInvocationOperation)model.GetOperation(
            syntax.ExpressionBody!.Expression)!;
        var evidence = new SpecEvidence(SpecEvidenceKind.Observed, "flow-test");
        var table = ApiSpecTable.Create([
            new ApiSpecDeclaration(
                new ApiSpecTarget(
                    "flow.null-result",
                    "M:Sample.Return",
                    "Sample",
                    SpecTargetMemberKind.Method,
                    "Return",
                    true,
                    0,
                    null,
                    [],
                    IrTypeKind.Reference,
                    [new ApiSpecAssemblyIdentity("EffectsTest", string.Empty)]),
                new ApiSpecFacets(
                    new SpecEffectFacet(SpecEffect.None, evidence),
                    new SpecAllocationFacet(SpecAllocationBehavior.None, evidence),
                    new SpecThrowFacet(SpecThrowBehavior.DoesNotThrow, [], evidence),
                    new SpecNullnessFacet(SpecNullness.Null, evidence),
                    new SpecCardinalityFacet(
                        SpecCardinality.NotApplicable,
                        null,
                        evidence)),
                [])
        ]);
        var resolved = new ApiSpecResolver(table).Resolve(compilation);
        var value = ManagedAbstractFlow.Create(compilation, resolved)
            .Evaluate(invocation, ManagedFlowState.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.IsComplete, Is.True);
            Assert.That(value.IsDefinitelyNull, Is.True);
        }
    }

    [Test]
    public void OperationBudgetExhaustionFailsClosed()
    {
        var statements = string.Concat(
            Enumerable.Repeat("value++;", ManagedAbstractFlow.MaxAnalyzedOperations + 1));
        var compilation = EffectTestHost.CreateCompilation(
            "public static class Sample { public static void Calls() { int value = 0;" +
            statements +
            "} }");
        var (_, _, graph, analysis) = AnalyzeCalls(compilation);

        Assert.That(graph.Blocks.Length, Is.LessThanOrEqualTo(ManagedAbstractFlow.MaxAnalyzedBlocks));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.BudgetExceeded));
            Assert.That(
                analysis.IncompleteReason,
                Is.EqualTo(EffectAnalysisIncompleteReason.OperationBudgetExceeded));
            Assert.That(analysis.Result, Is.Null);
        }
    }

    [Test]
    public void BlockBudgetExhaustionHasASeparateDeterministicReason()
    {
        var branches = string.Concat(
            Enumerable.Repeat(
                "if (condition) { value++; }",
                ManagedAbstractFlow.MaxAnalyzedBlocks));
        var compilation = EffectTestHost.CreateCompilation(
            "public static class Sample {" +
            " public static void Calls(bool condition) { int value = 0;" +
            branches +
            "} }");
        var (_, _, graph, analysis) = AnalyzeCalls(compilation);

        Assert.That(graph.Blocks.Length, Is.GreaterThan(ManagedAbstractFlow.MaxAnalyzedBlocks));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.BudgetExceeded));
            Assert.That(
                analysis.IncompleteReason,
                Is.EqualTo(EffectAnalysisIncompleteReason.BlockBudgetExceeded));
            Assert.That(analysis.Result, Is.Null);
        }
    }

    [Test]
    public void CyclicControlFlowIsTypedAndHasNoScalarResult()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Calls(bool condition) {
                    while (condition) {
                    }
                }
            }
            """);
        var (_, _, _, analysis) = AnalyzeCalls(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Cyclic));
            Assert.That(
                analysis.IncompleteReason,
                Is.EqualTo(EffectAnalysisIncompleteReason.CyclicControlFlow));
            Assert.That(analysis.Result, Is.Null);
        }
    }

    [Test]
    public void IncrementAndDecrementUpdateSubsequentIntervals()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static void Sink(int value) {
                }

                public static void Calls(bool condition) {
                    var safe = condition ? 0 : 1;
                    safe++;
                    Sink(safe);

                    var violated = 0;
                    violated--;
                    Sink(violated);
                }
            }
            """);
        var (_, root, _, analysis) = AnalyzeCalls(compilation);
        var flow = analysis.Result;
        var calls = root.Descendants().OfType<IInvocationOperation>()
            .OrderBy(static call => call.Syntax.SpanStart).ToArray();

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(flow, Is.Not.Null);
        Assert.That(calls, Has.Length.EqualTo(2));
        AssertIntegerInterval(
            analysis,
            calls[0],
            0,
            IntervalValue.Range(1, 2));
        AssertIntegerInterval(
            analysis,
            calls[1],
            0,
            IntervalValue.Constant(-1));
    }

    [TestCase("==", 3, 7,
        TestName = "EqualityBranchesIntersectBothVariableIntervals")]
    [TestCase("<", 0, 6,
        TestName = "OrderedBranchesRefineBothVariableIntervals")]
    public void BranchesRefineBothVariableIntervals(
        string comparison,
        int expectedLeftMinimum,
        int expectedLeftMaximum)
    {
        var (analysis, call) = AnalyzeSingleCall(
            $$"""
            public static class Sample {
                private static void Sink(int left, int right) {
                }

                public static void Calls(
                    [SharpProof.Attributes.InRange(0, 10)] int left,
                    [SharpProof.Attributes.InRange(3, 7)] int right) {
                    if (left {{comparison}} right)
                        Sink(left, right);
                }
            }
            """);

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        AssertIntegerInterval(
            analysis,
            call,
            0,
            IntervalValue.Range(expectedLeftMinimum, expectedLeftMaximum));
        AssertIntegerInterval(
            analysis,
            call,
            1,
            IntervalValue.Range(3, 7));
    }

    [Test]
    public void UserDefinedEqualityDoesNotRefineNullness()
    {
        var (analysis, call) = AnalyzeSingleCall(
            """
            public sealed class Token {
                public static bool operator ==(Token left, Token right) => true;
                public static bool operator !=(Token left, Token right) => false;
                public override bool Equals(object other) => true;
                public override int GetHashCode() => 0;
            }

            public static class Sample {
                private static void Sink(Token value) {
                }

                public static void Calls(Token value) {
                    if (value == null)
                        Sink(value);
                }
            }
            """);

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(
            analysis.Result!.TryEvaluate(
                call,
                call.Arguments[0].Value,
                out var value),
            Is.True);
        Assert.That(value.TryGetNullness(out var nullness), Is.True);
        Assert.That(nullness, Is.EqualTo(NullnessValue.MaybeNull));
    }

    [Test]
    public void UserDefinedEqualityEvaluatesAsUnknown()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Token {
                public static bool operator ==(Token left, Token right) => true;
                public static bool operator !=(Token left, Token right) => false;
                public override bool Equals(object other) => true;
                public override int GetHashCode() => 0;
            }

            public static class Sample {
                public static bool Calls() => new Token() == null;
            }
            """);
        var (_, root, _) = GetCallsContext(compilation);
        var binary = root.Descendants().OfType<IBinaryOperation>().Single();

        var value = ManagedAbstractFlow.ForCompilation(compilation)
            .Evaluate(binary, ManagedFlowState.Empty);

        Assert.That(value.IsBoolean, Is.True);
        Assert.That(value.TryGetBoolean(out _), Is.False);
    }

    [Test]
    public void SharedEdgeRefinementPreservesBooleanFactsAcrossOperationIdentity()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static void Sink(bool value) {
                }

                public static void Calls(bool condition) {
                    if (condition != true)
                        Sink(condition);
                }
            }
            """);
        var (method, root, _, analysis) = AnalyzeCalls(compilation);
        var call = root.Descendants().OfType<IInvocationOperation>().Single();
        var flow = analysis.Result;

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(flow, Is.Not.Null);
        Assert.That(flow!.TryEvaluate(call, call.Arguments[0].Value, out var value), Is.True);
        Assert.That(value.TryGetBoolean(out var boolean), Is.True);
        Assert.That(boolean, Is.False);
    }

    [Test]
    public void ContractRequiresRefinesSubsequentFacts()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static void Sink(int value) {
                }

                public static void Calls(int value) {
                    SharpProof.Attributes.Contract.Requires(value > 0);
                    Sink(value);
                }
            }
            """);
        var (method, root, _, analysis) = AnalyzeCalls(compilation);
        var sink = root.Descendants().OfType<IInvocationOperation>()
            .Single(static invocation => invocation.TargetMethod.Name == "Sink");

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        AssertIntegerInterval(
            analysis,
            sink,
            0,
            IntervalValue.Range(1, int.MaxValue));
    }

    [TestCase("left > 0 && right > 0", 1, int.MaxValue,
        TestName = "AssumeRefinesCompoundAndFacts")]
    [TestCase("!(left > 0 || right > 0)", int.MinValue, 0,
        TestName = "AssumeRefinesNegatedCompoundOrFacts")]
    public void AssumeRefinesCompoundFacts(
        string condition,
        int expectedMinimum,
        int expectedMaximum)
    {
        var compilation = EffectTestHost.CreateCompilation(
            $$"""
            public static class Sample {
                public static void Calls(int left, int right) {
                    SharpProof.Attributes.Contract.Requires(
                        {{condition}});
                }
            }
            """);
        var (method, root, _) = GetCallsContext(compilation);
        var requires = root.Descendants().OfType<IInvocationOperation>()
            .Single(static invocation => invocation.TargetMethod.Name == "Requires");
        var state = ManagedAbstractFlow.ForCompilation(compilation)
            .Assume(ManagedFlowState.Empty, requires.Arguments[0].Value, true);
        var expected = IntervalValue.Range(expectedMinimum, expectedMaximum);

        AssertIntegerInterval(state, method.Parameters[0], expected);
        AssertIntegerInterval(state, method.Parameters[1], expected);
    }

    [TestCase("left > 0 && right > 0", 1, int.MaxValue,
        TestName = "ContractRequiresRefinesConditionalAndFacts")]
    [TestCase("!(left > 0 || right > 0)", int.MinValue, 0,
        TestName = "ContractRequiresRefinesNegatedConditionalOrFacts")]
    public void ContractRequiresRefinesConditionalFacts(
        string condition,
        int expectedMinimum,
        int expectedMaximum)
    {
        var compilation = EffectTestHost.CreateCompilation(
            $$"""
            public static class Sample {
                private static void Sink(int value) {
                }

                public static void Calls(int left, int right) {
                    if ({{condition}}) {
                        Sink(left);
                        Sink(right);
                    }
                }
            }
            """);
        var (method, root, _, analysis) = AnalyzeCalls(compilation);
        var sinks = root.Descendants().OfType<IInvocationOperation>()
            .Where(static invocation => invocation.TargetMethod.Name == "Sink")
            .ToArray();
        var expected = IntervalValue.Range(expectedMinimum, expectedMaximum);

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(sinks, Has.Length.EqualTo(2));
        foreach (var sink in sinks)
            AssertIntegerInterval(analysis, sink, 0, expected);
    }

    [Test]
    public void SourceShadowedContractClauseDoesNotRefineScalarFacts()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            namespace SharpProof.Attributes {
                public static class Contract {
                    public static void Requires(bool condition) {
                    }
                }
            }

            public static class Sample {
                private static void Sink(int value) {
                }

                public static void Calls(int value) {
                    SharpProof.Attributes.Contract.Requires(value > 0);
                    Sink(value);
                }
            }
            """);
        var (method, root, _, analysis) = AnalyzeCalls(compilation);
        var sink = root.Descendants().OfType<IInvocationOperation>()
            .Single(static invocation =>
                invocation.TargetMethod.Name == "Sink");

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        AssertIntegerInterval(
            analysis,
            sink,
            0,
            IntervalValue.Range(int.MinValue, int.MaxValue));
    }

    [Test]
    public void SourceShadowedContractClauseDoesNotBypassCompletionChecks()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            namespace SharpProof.Attributes {
                public static class Contract {
                    public static void Requires(bool condition) {
                        throw new System.InvalidOperationException();
                    }
                }
            }

            public static class Sample {
                public static void Calls() {
                    SharpProof.Attributes.Contract.Requires(true);
                }
            }
            """);
        var invocation = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single();
        var model = compilation.GetSemanticModel(invocation.SyntaxTree);
        var operation = (IInvocationOperation)model.GetOperation(invocation)!;

        var facts = new DefiniteOperationFacts(compilation, default);

        Assert.That(facts.CompletesNormally(operation), Is.False);
    }

    [Test]
    public void SourceShadowedClosedAttributesDoNotRefineEntryFacts()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            namespace SharpProof.Attributes {
                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class NotNullAttribute :
                    System.Attribute {
                }

                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class PositiveAttribute :
                    System.Attribute {
                }

                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class InRangeAttribute :
                    System.Attribute {
                    public InRangeAttribute(long minimum, long maximum) {
                    }
                }
            }

            public static class Sample {
                private static void Sink(
                    string text,
                    int positive,
                    int range) {
                }

                public static void Calls(
                    [SharpProof.Attributes.NotNull] string text,
                    [SharpProof.Attributes.Positive] int positive,
                    [SharpProof.Attributes.InRange(1, 5)] int range) {
                    Sink(text, positive, range);
                }
            }
            """);
        var (method, root, _, analysis) = AnalyzeCalls(compilation);
        var sink = root.Descendants().OfType<IInvocationOperation>().Single();

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(
            analysis.Result!.TryEvaluate(
                sink,
                sink.Arguments[0].Value,
                out var text),
            Is.True);
        Assert.That(text.TryGetNullness(out var nullness), Is.True);
        Assert.That(nullness, Is.EqualTo(NullnessValue.MaybeNull));
        AssertIntegerInterval(
            analysis,
            sink,
            1,
            IntervalValue.Range(int.MinValue, int.MaxValue));
        AssertIntegerInterval(
            analysis,
            sink,
            2,
            IntervalValue.Range(int.MinValue, int.MaxValue));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnapprovedContractPackageCannotRefineClosedFacts(
        bool validContractShape)
    {
        var contractReference =
            EffectTestHost.EmitUnapprovedContractApiReference(
                validContractShape);
        var compilation =
            EffectTestHost.CreateCompilationWithoutContractPackage(
                """
                public static class Sample {
                    private static void Sink(string? text) {
                    }

                    public static void Calls(
                        [SharpProof.Attributes.NotNull] string? text) {
                        Sink(text);
                    }
                }
                """,
                contractReference);
        var (method, root, _, analysis) = AnalyzeCalls(compilation);
        var sink = root.Descendants().OfType<IInvocationOperation>().Single();

        Assert.That(analysis.Status, Is.EqualTo(ManagedFlowStatus.Complete));
        Assert.That(
            analysis.Result!.TryEvaluate(
                sink,
                sink.Arguments[0].Value,
                out var text),
            Is.True);
        Assert.That(text.TryGetNullness(out var nullness), Is.True);
        Assert.That(nullness, Is.EqualTo(NullnessValue.MaybeNull));
    }

    private static (IMethodSymbol Method, IMethodBodyOperation Root,
        ControlFlowGraph Graph)
        GetCallsContext(CSharpCompilation compilation)
    {
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Calls");
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var root = (IMethodBodyOperation)model.GetOperation(syntax)!;
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        return (method, root, ControlFlowGraph.Create(root));
    }

    private static (IMethodSymbol Method, IMethodBodyOperation Root,
        ControlFlowGraph Graph, ManagedFlowAnalysis Analysis)
        AnalyzeCalls(CSharpCompilation compilation)
    {
        var (method, root, graph) = GetCallsContext(compilation);
        var analysis = ManagedAbstractFlow.ForCompilation(compilation)
            .Analyze(method, graph, null, default);
        return (method, root, graph, analysis);
    }

    private static void AssertIntegerInterval(
        ManagedFlowAnalysis analysis,
        IInvocationOperation invocation,
        int argumentIndex,
        IntervalValue expected)
    {
        Assert.That(
            analysis.Result!.TryEvaluate(
                invocation,
                invocation.Arguments[argumentIndex].Value,
                out var value),
            Is.True);
        Assert.That(value.TryGetInteger(out var interval), Is.True);
        Assert.That(interval, Is.EqualTo(expected));
    }

    private static void AssertIntegerInterval(
        ManagedFlowState state,
        IParameterSymbol parameter,
        IntervalValue expected)
    {
        Assert.That(state.Get(parameter).TryGetInteger(out var interval), Is.True);
        Assert.That(interval, Is.EqualTo(expected));
    }

    private static (ManagedFlowAnalysis Analysis, IInvocationOperation Call)
        AnalyzeSingleCall(string source)
    {
        var compilation = EffectTestHost.CreateCompilation(source);
        var (method, root, graph) = GetCallsContext(compilation);
        var call = root.Descendants().OfType<IInvocationOperation>().Single();
        var analysis = ManagedAbstractFlow.ForCompilation(compilation)
            .Analyze(method, graph, null, default);
        return (analysis, call);
    }

    [Test]
    public void ApprovedApiSpecificationRefinesReturnNullnessAndCardinality()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int[] Calls() => System.Array.Empty<int>();
            }
            """);
        var invocation = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var model = compilation.GetSemanticModel(invocation.SyntaxTree);
        var operation = (IInvocationOperation)model.GetOperation(invocation)!;

        var value = ManagedAbstractFlow.ForCompilation(compilation)
            .Evaluate(operation, ManagedFlowState.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.IsDefinitelyNonNull, Is.True);
            Assert.That(value.TryGetCardinality(out var cardinality), Is.True);
            Assert.That(cardinality, Is.EqualTo(IntervalValue.Constant(0)));
        }
    }

    [Test]
    public void LookalikeLengthPropertyCannotReadReceiverCardinality()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Lookalike {
                public int Length => 7;
            }

            public static class Sample {
                public static int Calls(Lookalike value) => value.Length;
            }
            """);
        var syntax = compilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Calls");
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var property = (IPropertyReferenceOperation)model.GetOperation(
            syntax.ExpressionBody!.Expression)!;
        var state = ManagedFlowState.Empty.Set(
            method.Parameters.Single(),
            ManagedAbstractValue.Reference(
                NullnessValue.NonNull,
                IntervalValue.Constant(99)));

        var value = ManagedAbstractFlow.ForCompilation(compilation)
            .Evaluate(property, state);

        Assert.That(value.TryGetInteger(out var interval), Is.True);
        Assert.That(
            interval,
            Is.EqualTo(IntervalValue.Range(int.MinValue, int.MaxValue)));
    }

    [Test]
    public void NonConvergentAnalysisDegradesToAnIncompleteSummary()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static long Calls(long value) {
                    var total = 0L;
                    if (value > 0) { total = value; } else { total = -value; }
                    if (value > 10) { total += 1; } else { total += 2; }
                    return total;
                }
            }
            """);
        var (method, root, graph) = GetCallsContext(compilation);

        // The body must be ACYCLIC: a loop is rejected by the IsAcyclic gate
        // before the solver ever runs, so a cyclic fixture would report Cyclic
        // and never exercise the convergence catch at all. Branching gives a
        // multi-block graph that cannot settle within one worklist round.
        var analysis = ManagedAbstractFlow.ForCompilation(compilation)
            .AnalyzeWithIterationLimitForTesting(
                method,
                graph,
                null,
                maxIterations: 1,
                default);

        // Assert the specific status. "not Complete" would also be satisfied by
        // Cyclic, which is how the previous version of this test passed without
        // reaching the catch.
        Assert.That(
            analysis.Status,
            Is.EqualTo(ManagedFlowStatus.BudgetExceeded));
    }

    [Test]
    public void DeepExpressionEvaluationAbstainsInsteadOfExhaustingTheStack()
    {
        static (ManagedAbstractValue Value, ManagedAbstractFlow Flow) Evaluate(int terms)
        {
            var chain = string.Join(" + ", Enumerable.Repeat("value", terms));
            var compilation = EffectTestHost.CreateCompilation(
                $$"""
                public static class Sample {
                    public static long Calls(long value) => {{chain}};
                }
                """);
            var syntax = compilation.SyntaxTrees.Single().GetRoot()
                .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
            var expression = model.GetOperation(syntax.ExpressionBody!.Expression)!;
            var state = ManagedFlowState.Empty.Set(
                method.Parameters[0],
                ManagedAbstractValue.Integer(IntervalValue.Constant(1)));
            var flow = ManagedAbstractFlow.ForCompilation(compilation);
            return (flow.Evaluate(expression, state), flow);
        }

        // Entered directly, so the walk budget is spent here rather than in
        // Transfer, which is what makes this guard reachable at all.
        var shallow = Evaluate(4).Value;
        var deep = Evaluate(400).Value;

        // With the parameter bound, a shallow chain folds to an exact interval.
        // Past the depth budget the walk stops and the operand becomes unknown,
        // which widens the result to the whole domain rather than recursing.
        Assert.That(shallow.TryGetInteger(out var shallowInterval), Is.True);
        Assert.That(shallowInterval, Is.EqualTo(IntervalValue.Constant(4)));
        Assert.That(deep.TryGetInteger(out var deepInterval), Is.True);
        Assert.That(deepInterval, Is.EqualTo(
            IntervalValue.Range(long.MinValue, long.MaxValue)));
    }

    [Test]
    public void IrScalarArithmeticKeepsTheIntervalItComputes()
    {
        var ten = ManagedAbstractValue.Integer(IntervalValue.Constant(10));
        var one = ManagedAbstractValue.Integer(IntervalValue.Constant(1));

        // No Roslyn type symbol is available for an IR term, and the general
        // Binary overload discards a computed interval when it cannot bound it.
        // The IR integer domain is exactly Int64 and TryArithmetic already
        // refuses anything outside it, so the result is kept.
        var kept = ManagedAbstractValue.BinaryOverIrScalars(
            BinaryOperatorKind.Subtract, ten, one);
        var discarded = ManagedAbstractValue.Binary(
            BinaryOperatorKind.Subtract, ten, one);

        Assert.That(kept.TryGetInteger(out var keptInterval), Is.True);
        Assert.That(keptInterval, Is.EqualTo(IntervalValue.Constant(9)));
        Assert.That(discarded.IsUnknown, Is.True);
    }
}
