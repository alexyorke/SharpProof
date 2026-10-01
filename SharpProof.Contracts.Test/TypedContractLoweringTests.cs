using System.Collections.Immutable;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;
using SharpProof.Verify;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class TypedContractLoweringTests
{
    [TestCase("unchecked(value + 1)", false)]
    [TestCase("(unchecked(value + 1))", false)]
    [TestCase("checked(value + 1)", true)]
    [TestCase("(checked((value + 1)))", true)]
    public void ExpressionBodyWrappersRetainCheckedSemantics(string expression, bool throws)
    {
        var subject = Subject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) => {{expression}};
            }
            """);
        var binding = subject.Bind();
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        Assert.That(binding.Clauses, Is.Empty);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, subject.EntryValues(int.MaxValue));
        Assert.That(execution.Status, Is.EqualTo(throws ? IrProgramExecutionStatus.Exception : IrProgramExecutionStatus.Returned));
        if (!throws)
        {
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(int.MinValue)));
        }
    }

    [TestCase("Contract.Old(x) + Contract.Old(y)", int.MaxValue, 1, false)]
    [TestCase("Contract.Old(x) + Contract.Old(y)", int.MinValue, -1, false)]
    [TestCase("Contract.Old(x) - Contract.Old(y)", int.MinValue, 1, false)]
    [TestCase("Contract.Old(x) * Contract.Old(y)", int.MinValue, -1, false)]
    [TestCase("Contract.Old(x) * Contract.Old(y)", 0, int.MinValue, true)]
    [TestCase("-Contract.Old(x)", int.MinValue, 0, false)]
    [TestCase("+Contract.Old(x)", int.MinValue, 0, true)]
    [TestCase("Contract.Old(x) * Contract.Old(y)", -2, -3, true)]
    public void CheckedClauseValueAndSafetyRetainOriginalOperands(string expression, int first, int second, bool safe)
    {
        var subject = Subject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x, int y) {
                Contract.Ensures(checked({{expression}}) == unchecked({{expression}}));
                x = 0; y = 0; return 0;
            } }
            """);
        var clause = subject.Bind().Clauses.Single();
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, subject.EntryValues(first, second));
        var interpreter = new IrInterpreter(subject.Factory);
        Assert.That(interpreter.Evaluate(clause.Value, execution.Values).Value!.Boolean, Is.True);
        Assert.That(interpreter.Evaluate(clause.SafeCondition, execution.Values).Value!.Boolean, Is.EqualTo(safe));
        Assert.That(execution.Values[subject.Context.Parameters[0].Current].IntegerNumericValue, Is.EqualTo(BigInteger.Zero));
    }

    [TestCase("x == int.MaxValue || checked(x + 1) > x", true)]
    [TestCase("x == int.MaxValue ? true : checked(x + 1) > x", true)]
    [TestCase("checked(x + 1) > x || x == int.MaxValue", false)]
    public void CheckedClauseSafetyFollowsSelectedOperand(string expression, bool safe)
    {
        var subject = Subject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Ensures({{expression}}); return x;
            } }
            """);
        var clause = subject.Bind().Clauses.Single();
        var interpreter = new IrInterpreter(subject.Factory);
        var values = subject.Values(int.MaxValue);
        Assert.That(interpreter.Evaluate(clause.Value, values).Value!.Boolean, Is.True);
        Assert.That(interpreter.Evaluate(clause.SafeCondition, values).Value!.Boolean, Is.EqualTo(safe));
    }

    [TestCase("Requires")]
    [TestCase("Assume")]
    [TestCase("Ensures")]
    public void UndefinedClausesRetainSafetySeparatelyFromTheirValue(string kind)
    {
        var subject = Subject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int d) {
                    Contract.{{kind}}(10 / d == 10 / d);
                    return 0;
                }
            }
            """);
        var clause = subject.Bind().Clauses.Single();
        var values = subject.Values(0);
        var interpreter = new IrInterpreter(subject.Factory);
        Assert.That(interpreter.Evaluate(clause.Value, values).Value!.Boolean, Is.True);
        Assert.That(interpreter.Evaluate(clause.SafeCondition, values).Value!.Boolean, Is.False);
    }

    [TestCase("false && 10 / d > 0", false)]
    [TestCase("true || 10 / d > 0", true)]
    public void UnselectedClauseOperandsDoNotCreateFaults(string expression, bool expectedValue)
    {
        var subject = Subject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int d) {
                    Contract.Ensures({{expression}});
                    return 0;
                }
            }
            """);
        var clause = subject.Bind().Clauses.Single();
        var values = subject.Values(0);
        var interpreter = new IrInterpreter(subject.Factory);
        Assert.That(interpreter.Evaluate(clause.SafeCondition, values).Value!.Boolean, Is.True);
        Assert.That(interpreter.Evaluate(clause.Value, values).Value!.Boolean, Is.EqualTo(expectedValue));
    }

    [Test]
    public void OldSubstitutesTheSafetyAndValueBeforeBodyMutation()
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x, int d) {
                    Contract.Ensures(Contract.Old(x / d) == 2);
                    d = 0;
                    return x;
                }
            }
            """);
        var clause = subject.Bind().Clauses.Single();
        var program = subject.Lower();
        Assert.That(program.IsExact, Is.True);
        var execution = new IrProgramInterpreter(subject.Factory).Execute(program.Program, subject.EntryValues(4, 2));
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        var interpreter = new IrInterpreter(subject.Factory);
        Assert.That(interpreter.Evaluate(clause.SafeCondition, execution.Values).Value!.Boolean, Is.True);
        Assert.That(interpreter.Evaluate(clause.Value, execution.Values).Value!.Boolean, Is.True);
        Assert.That(execution.Values[subject.Context.Parameters[1].Current].IntegerNumericValue, Is.EqualTo(BigInteger.Zero));
    }

    [TestCase(4, 0, true, true)]
    [TestCase(4, 2, true, false)]
    [TestCase(int.MaxValue, -1, false, false)]
    public void ConditionalEnsuresReadsOnlyTheSelectedBranchSafetyAfterMutation(int input, int divisor, bool safe, bool value)
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x, int d) {
                    Contract.Ensures(d == 0 ? Contract.Result<int>() == 0 : Contract.Result<int>() / d == Contract.Old(x));
                    x = unchecked(x + 1);
                    return d == 0 ? 0 : x;
                }
            }
            """);
        var clause = subject.Bind().Clauses.Single();
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, subject.EntryValues(input, divisor));
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        var interpreter = new IrInterpreter(subject.Factory);
        Assert.That(interpreter.Evaluate(clause.SafeCondition, execution.Values).Value!.Boolean, Is.EqualTo(safe));
        Assert.That(interpreter.Evaluate(clause.Value, execution.Values).Value!.Boolean, Is.EqualTo(value));
        var binding = subject.Context.Parameters[0];
        Assert.That(execution.Values[binding.PreState].IntegerNumericValue, Is.EqualTo(new BigInteger(input)));
        Assert.That(execution.Values[binding.Current].IntegerNumericValue, Is.EqualTo(new BigInteger(unchecked(input + 1))));
    }

    [TestCase(0, true, true)]
    [TestCase(1, true, false)]
    [TestCase(256, false, true)]
    [TestCase(-1, false, false)]
    public void CheckedCastGuardAndUncheckedUnaryValueRetainTheOldOperand(int input, bool safe, bool value)
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int d) {
                    Contract.Ensures(checked((byte)Contract.Old(d)) == unchecked((byte)-Contract.Old(d)));
                    d = 0;
                    return 0;
                }
            }
            """);
        var clause = subject.Bind().Clauses.Single();
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, subject.EntryValues(input));
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        var interpreter = new IrInterpreter(subject.Factory);
        Assert.That(interpreter.Evaluate(clause.SafeCondition, execution.Values).Value!.Boolean, Is.EqualTo(safe));
        Assert.That(interpreter.Evaluate(clause.Value, execution.Values).Value!.Boolean, Is.EqualTo(value));
        Assert.That(execution.Values[subject.Context.Parameters[0].Current].IntegerNumericValue, Is.EqualTo(BigInteger.Zero));
        Assert.That(execution.Values[subject.Context.Parameters[0].PreState].IntegerNumericValue, Is.EqualTo(new BigInteger(input)));
    }

    [Test]
    public async Task SharedBodyAndClauseBindingsReplayTheActualEntryAndExitStates()
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x) {
                    Contract.Requires(x > 0);
                    Contract.Ensures(Contract.Result<int>() == Contract.Old(x) && x == Contract.Result<int>());
                    x = unchecked(x + 1);
                    return x;
                }
            }
            """);
        var clauses = subject.Bind().Clauses;
        var requires = clauses.Single(clause => clause.Kind == BoundContractKind.Requires);
        var ensures = clauses.Single(clause => clause.Kind == BoundContractKind.Ensures);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True, string.Join(';', lowering.Abstentions.Select(item => item.Reason)));
        var binding = subject.Context.Parameters.Single();
        var result = subject.Context.Result!.Value;
        var values = ImmutableDictionary<IrVarId, IrValue>.Empty
            .Add(binding.Entry, subject.Value(binding.Entry, 5))
            .Add(binding.Current, subject.Value(binding.Current, 6))
            .Add(binding.PreState, subject.Value(binding.PreState, 5))
            .Add(result, subject.Value(result, 6));
        var requirement = subject.Factory.Binary(IrBinaryOperator.AndAlso, requires.SafeCondition, requires.Value);
        var assumption = new Assumption(subject.Factory, requirement,
            new LoweredJustification(requires.SourceOperation));
        var query = new VerificationQuery(subject.Factory, [assumption],
            new Goal(subject.Factory, subject.Factory.Binary(IrBinaryOperator.AndAlso, ensures.SafeCondition, ensures.Value),
                ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [binding.Entry]);
        var replay = new CallableReplayContext(lowering.Program, false,
            ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(binding.Entry, binding.Entry).Add(binding.Current, binding.Current),
            ImmutableDictionary<IrVarId, IrVarId?>.Empty.Add(binding.PreState, binding.Entry),
            [result], ensures.Value, ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty,
            1000, [], postconditionGuard: ensures.SafeCondition, replayOptions: null);
        var outcome = await new ProofKernel(new ModelBackend(values)).VerifyCallableAsync(query, replay);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        var execution = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, subject.EntryValues(5));
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(6)));
        Assert.That(execution.Values[binding.Entry].IntegerNumericValue, Is.EqualTo(new BigInteger(5)));
        Assert.That(execution.Values[binding.PreState].IntegerNumericValue, Is.EqualTo(new BigInteger(5)));
        Assert.That(execution.Values[binding.Current].IntegerNumericValue, Is.EqualTo(new BigInteger(6)));
    }

    [Test]
    public async Task UndefinedEnsuresCannotBecomeARefutationUnderTotalTermValues()
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int d) {
                    Contract.Ensures(Contract.Result<int>() / d == Contract.Result<int>() / d);
                    return 0;
                }
            }
            """);
        var clause = subject.Bind().Clauses.Single();
        var lowering = subject.Lower();
        var binding = subject.Context.Parameters.Single();
        var result = subject.Context.Result!.Value;
        var values = subject.Values(0).ToImmutableDictionary().Add(result, subject.Value(result, 0));
        var query = new VerificationQuery(subject.Factory, [],
            new Goal(subject.Factory, subject.Factory.Binary(IrBinaryOperator.AndAlso, clause.SafeCondition, clause.Value),
                ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [.. values.Keys]);
        var replay = new CallableReplayContext(lowering.Program, false,
            ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(binding.Entry, binding.Entry).Add(binding.Current, binding.Current),
            ImmutableDictionary<IrVarId, IrVarId?>.Empty.Add(binding.PreState, binding.Entry),
            [result], clause.Value, ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty, 1000, [],
            postconditionGuard: clause.SafeCondition, replayOptions: null);
        var outcome = await new ProofKernel(new ModelBackend(values)).VerifyCallableAsync(query, replay);
        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(AbstentionReason.PostconditionMayBeUndefined));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PartialImplementationsBindTheirOwnParameterSymbols(bool implementationContext)
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            public static partial class Subject {
                public static partial int Target(int x);
                public static partial int Target(int x) {
                    Contract.Requires(x > 0);
                    return x;
                }
            }
            """, implementationContext: implementationContext);
        Assert.That(subject.Bind().Failure, Is.EqualTo(implementationContext
            ? ContractBindingFailure.None : ContractBindingFailure.UnsupportedTarget));
        Assert.That(subject.Lower().IsExact, Is.EqualTo(implementationContext));
    }

    [Test]
    public void ConstructedGenericParameterSymbolsAbstainBeforeClauseLowering()
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target<T>(int x) {
                    Contract.Requires(x > 0);
                    return x;
                }
            }
            """, constructed: true);
        Assert.That(subject.Bind().Failure, Is.EqualTo(ContractBindingFailure.UnsupportedTarget));
        Assert.That(subject.Lower().IsExact, Is.False);
    }

    [Test]
    public void ForeignBodyCannotBorrowAnotherCallableContext()
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x) { Contract.Requires(x > 0); return x; }
                public static int Other(int y) { Contract.Requires(y > 0); return y; }
            }
            """);
        var other = subject.Compilation.SyntaxTrees.Single().GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Other");
        var model = subject.Compilation.GetSemanticModel(other.SyntaxTree);
        var graph = ControlFlowGraph.Create(other, model)!;
        Assert.That(new RoslynProgramLowerer(subject.Factory).LowerCandidate(graph, subject.Context).IsExact, Is.False);
        Assert.That(new ContractBinder(subject.Compilation, subject.Factory).BindTotal(subject.Context, graph.OriginalOperation).IsSuccess, Is.False);
    }

    [TestCase("closed-return", ContractBindingFailure.InvalidClosedAttribute)]
    [TestCase("closed-parameter", ContractBindingFailure.InvalidClosedAttribute)]
    [TestCase("unsupported-conditional", ContractBindingFailure.UnsupportedExpression)]
    [TestCase("misplaced-clause", ContractBindingFailure.InvalidClausePlacement)]
    [TestCase("invalid-intrinsic", ContractBindingFailure.ResultOutsideEnsures)]
    public void RejectedContractsRetainEarlierClausesAndBodyMutations(string scenario, ContractBindingFailure failure)
    {
        var returnAttribute = scenario == "closed-return" ? "[return: InRange(2, 1)]" : "";
        var parameterAttribute = scenario == "closed-parameter" ? "[InRange(2, 1)]" : "";
        var additionalClause = scenario switch
        {
            "unsupported-conditional" => "Contract.Ensures(x == 0 ? true : Helper(x));",
            "invalid-intrinsic" => "Contract.Requires(Contract.Result<int>() > 0);",
            _ => ""
        };
        var misplacedClause = scenario == "misplaced-clause" ? "Contract.Ensures(x == 0);" : "";
        var subject = Subject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                {{returnAttribute}}
                public static int Target({{parameterAttribute}} int x) {
                    Contract.Requires(x > 0);
                    {{additionalClause}}
                    x = 0;
                    {{misplacedClause}}
                    return x;
                }
                private static bool Helper(int x) => x > 0;
            }
            """);
        var binding = new ContractBinder(subject.Compilation, subject.Factory).BindTotal(subject.Context);
        Assert.That(binding.Failure, Is.EqualTo(failure));
        Assert.That(binding.Clauses, Is.Empty);
        AssertRejectedBodyRetained(subject);
    }

    [Test]
    public void MissingRuntimeContractApiCannotEraseLookalikeCalls()
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            namespace SharpProof.Attributes {
                public static class Contract {
                    public static void Requires(bool condition) { System.Console.WriteLine(condition); }
                }
            }
            public static class Subject {
                public static int Target(int x) {
                    Contract.Requires(x > 0);
                    x = 0;
                    return x;
                }
            }
            """, includeSharpProofReference: false);
        var binding = new ContractBinder(subject.Compilation, subject.Factory).BindTotal(subject.Context);
        Assert.That(binding.Failure, Is.EqualTo(ContractBindingFailure.ContractApiUnavailable));
        Assert.That(binding.Clauses, Is.Empty);
        AssertRejectedBodyRetained(subject);
    }

    [Test]
    public void ForeignFactoryRejectionLeavesTheContextUnmodified()
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x) {
                    Contract.Requires(x > 0);
                    x = 0;
                    return x;
                }
            }
            """);
        var binder = new ContractBinder(subject.Compilation, new IrFactory(IrExecutionSemantics.Total));
        Assert.Throws<ArgumentException>(new Action(() => binder.BindTotal(subject.Context)));
        AssertRejectedBodyRetained(subject);
        Assert.That(subject.Bind().Clauses, Has.Length.EqualTo(1));
        Assert.That(subject.Lower().IsExact, Is.True);
    }

    [Test]
    public void CompanionContractsBindWithoutBorrowingTheCompanionBody()
    {
        var subject = Subject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x) { x = 0; return x; }
            }
            [ContractFor(typeof(Subject))]
            public static class SubjectContracts {
                public static int Target(int x) { Contract.Requires(x > 0); return x; }
            }
            """);
        var legacy = new ContractBinder(subject.Compilation, new IrFactory()).Bind(subject.Context.Target);
        Assert.That(legacy.IsSuccess, Is.True, legacy.Failure.ToString());
        Assert.That(legacy.Contracts!.UsesCompanion, Is.True);
        var binding = new ContractBinder(subject.Compilation, subject.Factory).BindTotal(subject.Context);
        Assert.That(binding.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(binding.Clauses, Has.Length.EqualTo(1));
        Assert.That(binding.Clauses.Single().Evidence, Is.EqualTo(BoundContractEvidence.Companion));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, subject.EntryValues(5));
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(BigInteger.Zero));
    }

    private static void AssertRejectedBodyRetained(Subject subject)
    {
        var method = subject.Compilation.SyntaxTrees.Single().GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Target");
        var invocation = method.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        var mutation = method.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Abstentions.Select(item => subject.Factory.GetOperationInfo(item.Operation).SourceSpan?.Start),
            Does.Contain(invocation.SpanStart), "The earlier specification call must remain when the complete binding fails.");
        Assert.That(lowering.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssignInstruction>()
                .Any(instruction => instruction.Target == subject.Context.Parameters[0].Current &&
                    subject.Factory.GetOperationInfo(instruction.Operation).SourceSpan?.Start == mutation.SpanStart),
            Is.True, "Rejecting contracts must preserve the original body mutation.");
    }

    private sealed class ModelBackend(ImmutableDictionary<IrVarId, IrValue> values) : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BackendCheckResult.Satisfiable(new BackendModel(values.Where(pair => query.ModelVariables.Contains(pair.Key)))));
        }
    }

    private sealed class Subject(Compilation compilation, TotalLoweringContext context, ControlFlowGraph graph)
    {
        internal Compilation Compilation { get; } = compilation;
        internal TotalLoweringContext Context { get; } = context;
        internal IrFactory Factory => Context.Factory;

        internal static Subject Create(string source, bool implementationContext = false, bool constructed = false,
            bool includeSharpProofReference = true)
        {
            var compilation = TestCompilation.Create("TypedContracts", source, includeSharpProofReference: includeSharpProofReference);
            TestCompilation.AssertNoErrors(compilation);
            var methods = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == "Target" &&
                    method.Parent is TypeDeclarationSyntax { Identifier.ValueText: "Subject" }).ToArray();
            var bodySyntax = methods.Single(method => method.Body != null || method.ExpressionBody != null);
            var contextSyntax = implementationContext ? bodySyntax : methods[0];
            var model = compilation.GetSemanticModel(bodySyntax.SyntaxTree);
            var method = (IMethodSymbol)model.GetDeclaredSymbol(contextSyntax)!;
            if (constructed)
            {
                method = method.Construct(compilation.GetSpecialType(SpecialType.System_String));
            }
            var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method);
            return new(compilation, context, ControlFlowGraph.Create(bodySyntax, model)!);
        }

        internal TotalContractBindingResult Bind()
        {
            var result = new ContractBinder(Compilation, Factory).BindTotal(Context);
            if (result.Failure != ContractBindingFailure.UnsupportedTarget)
            {
                Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
            }
            return result;
        }

        internal FrontendProgramLoweringResult Lower()
        {
            return new RoslynProgramLowerer(Factory).LowerCandidate(graph, Context);
        }

        internal Dictionary<IrVarId, IrValue> EntryValues(params int[] inputs)
        {
            return Context.Parameters.ToDictionary(binding => binding.Entry, binding => Value(binding.Entry, inputs[binding.Parameter.Ordinal]));
        }

        internal Dictionary<IrVarId, IrValue> Values(params int[] inputs)
        {
            return Context.Parameters.SelectMany(binding => new[] { binding.Entry, binding.Current, binding.PreState }
                    .Select(variable => KeyValuePair.Create(variable, Value(variable, inputs[binding.Parameter.Ordinal]))))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        internal IrValue Value(IrVarId variable, int value)
        {
            return Factory.CreateIntegerValue(Factory.GetVariableInfo(variable).Type, value);
        }
    }
}
