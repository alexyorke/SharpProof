using System.Collections.Immutable;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;

namespace SharpProof.Fuzz;

public enum PartialTermSemanticOutcome {
    DefinedTrue,
    DefinedFalse,
    Undefined
}

public sealed record PartialTermSmtDifferentialResult(
    FuzzOracleStatus Status,
    int ScenarioCount,
    int DefinedTrueCount,
    int DefinedFalseCount,
    int UndefinedCount,
    string Detail);

public sealed record PartialTermSmtCase(
    IrTerm Formula,
    ImmutableArray<ImmutableDictionary<IrVarId, IrValue>> Scenarios);

public static class PartialTermSmtCaseGenerator {
    public static PartialTermSmtCase Create(
        IrFactory factory,
        int seed) {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        var guard = factory.CreateVariable(
            "partial-guard",
            factory.BooleanType);
        var divisor = factory.CreateVariable(
            "partial-divisor",
            factory.IntegerType);
        var arithmetic = factory.Binary(
            (seed & 1) == 0
                ? IrBinaryOperator.Divide
                : IrBinaryOperator.Remainder,
            factory.Integer(long.MinValue),
            factory.Variable(divisor));
        var comparison = factory.Binary(
            IrBinaryOperator.Equal,
            arithmetic,
            factory.Integer(0));
        var useOrElse = (seed & 2) != 0;
        var formula = factory.Binary(
            useOrElse
                ? IrBinaryOperator.OrElse
                : IrBinaryOperator.AndAlso,
            factory.Variable(guard),
            comparison);
        var shortCircuitGuard = useOrElse;
        var undefinedGuard = !shortCircuitGuard;
        var undefinedDivisor = (seed & 4) == 0 ? 0L : -1L;

        return new PartialTermSmtCase(
            formula,
            [
                Scenario(
                    factory,
                    guard,
                    shortCircuitGuard,
                    divisor,
                    undefinedDivisor),
                Scenario(
                    factory,
                    guard,
                    undefinedGuard,
                    divisor,
                    undefinedDivisor)
            ]);
    }

    private static ImmutableDictionary<IrVarId, IrValue> Scenario(
        IrFactory factory,
        IrVarId guard,
        bool guardValue,
        IrVarId divisor,
        long divisorValue) =>
        ImmutableDictionary<IrVarId, IrValue>.Empty
            .Add(guard, factory.CreateBooleanValue(guardValue))
            .Add(divisor, factory.CreateIntegerValue(divisorValue));
}

public sealed class PartialTermSmtDifferentialOracle {
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Oracle methods intentionally share an instance-shaped test API.")]
    public async Task<PartialTermSmtDifferentialResult> CompareAsync(
        IrFactory factory,
        PartialTermSmtCase generated,
        CancellationToken cancellationToken = default) {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (generated == null)
            throw new ArgumentNullException(nameof(generated));
        if (generated.Formula.Type != factory.BooleanType)
            throw new ArgumentException(
                "The partial-term formula must be Boolean.",
                nameof(generated));
        if (generated.Scenarios.IsDefaultOrEmpty)
            throw new ArgumentException(
                "At least one concrete partial-term scenario is required.",
                nameof(generated));

        var variables = CollectVariables(generated.Formula);
        var interpreter = new IrInterpreter(factory);
        using var backend = new IrSmtBackend();
        var kernel = new ProofKernel(backend);
        var definedTrue = 0;
        var definedFalse = 0;
        var undefined = 0;

        for (var index = 0; index < generated.Scenarios.Length; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var scenario = generated.Scenarios[index];
            ValidateScenario(factory, variables, scenario);
            var expected = Classify(
                interpreter.Evaluate(generated.Formula, scenario));
            if (expected == null)
                return Result(
                    FuzzOracleStatus.Abstained,
                    index,
                    definedTrue,
                    definedFalse,
                    undefined,
                    "The IR interpreter could not classify partial-term " +
                    $"scenario {index}.");

            Count(
                expected.Value,
                ref definedTrue,
                ref definedFalse,
                ref undefined);
            var assumptions = variables
                .Select((variable, ordinal) =>
                    CreateAssignmentAssumption(
                        factory,
                        variable,
                        scenario[variable],
                        index,
                        ordinal))
                .ToImmutableArray();
            var query = new VerificationQuery(
                factory,
                assumptions,
                new Goal(
                    factory,
                    generated.Formula,
                    ProofDiagnosticKind.InternalConsistency,
                    new SourceLocationId(index)));
            var proof = await kernel.VerifyAsync(
                    query,
                    cancellationToken)
                .ConfigureAwait(false);
            var actual = Classify(proof);
            if (actual == null)
                return Result(
                    FuzzOracleStatus.Abstained,
                    index + 1,
                    definedTrue,
                    definedFalse,
                    undefined,
                    "The backend returned " +
                    Describe(proof) +
                    $" for partial-term scenario {index}.");
            if (actual != expected)
                return Result(
                    FuzzOracleStatus.Mismatch,
                    index + 1,
                    definedTrue,
                    definedFalse,
                    undefined,
                    "The interpreter reported " +
                    expected +
                    " while the backend reported " +
                    actual +
                    $" for partial-term scenario {index}.");
        }

        return Result(
            FuzzOracleStatus.Agreement,
            generated.Scenarios.Length,
            definedTrue,
            definedFalse,
            undefined,
            "");
    }

    private static Assumption CreateAssignmentAssumption(
        IrFactory factory,
        IrVarId variable,
        IrValue value,
        int scenario,
        int ordinal) =>
        new(
            factory,
            factory.Binary(
                IrBinaryOperator.Equal,
                factory.Variable(variable),
                Literal(factory, value)),
            new LoweredJustification(
                factory.CreateOperation(
                    "partial-scenario-" +
                    scenario +
                    "-assignment-" +
                    ordinal)));

    private static IrTerm Literal(
        IrFactory factory,
        IrValue value) =>
        value.Kind switch {
            IrValueKind.Boolean => factory.Boolean(value.Boolean),
            IrValueKind.Integer => factory.Integer(value.Integer),
            _ => throw new ArgumentException(
                "Partial-term scenarios support only Boolean and integer values.",
                nameof(value))
        };

    private static PartialTermSemanticOutcome? Classify(
        IrEvaluationResult result) =>
        result.Status switch {
            IrEvaluationStatus.Value when
                result.Value is { Kind: IrValueKind.Boolean } value =>
                value.Boolean
                    ? PartialTermSemanticOutcome.DefinedTrue
                    : PartialTermSemanticOutcome.DefinedFalse,
            IrEvaluationStatus.Exception =>
                PartialTermSemanticOutcome.Undefined,
            _ => null
        };

    private static PartialTermSemanticOutcome? Classify(
        ProofOutcome outcome) =>
        outcome switch {
            ProvenOutcome => PartialTermSemanticOutcome.DefinedTrue,
            RefutedOutcome => PartialTermSemanticOutcome.DefinedFalse,
            UnknownOutcome {
                Reason: AbstentionReason.CounterexampleReplayFailed
            } => PartialTermSemanticOutcome.Undefined,
            _ => null
        };

    private static string Describe(ProofOutcome outcome) =>
        outcome is UnknownOutcome unknown
            ? "Unknown(" + unknown.Reason + ")"
            : outcome.Kind.ToString();

    private static void Count(
        PartialTermSemanticOutcome outcome,
        ref int definedTrue,
        ref int definedFalse,
        ref int undefined) {
        switch (outcome) {
            case PartialTermSemanticOutcome.DefinedTrue:
                definedTrue++;
                break;
            case PartialTermSemanticOutcome.DefinedFalse:
                definedFalse++;
                break;
            case PartialTermSemanticOutcome.Undefined:
                undefined++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }

    private static PartialTermSmtDifferentialResult Result(
        FuzzOracleStatus status,
        int scenarioCount,
        int definedTrue,
        int definedFalse,
        int undefined,
        string detail) =>
        new(
            status,
            scenarioCount,
            definedTrue,
            definedFalse,
            undefined,
            detail);

    private static void ValidateScenario(
        IrFactory factory,
        ImmutableArray<IrVarId> variables,
        ImmutableDictionary<IrVarId, IrValue> scenario) {
        foreach (var variable in variables) {
            if (!scenario.TryGetValue(variable, out var value))
                throw new ArgumentException(
                    "A partial-term scenario does not assign every variable.",
                    nameof(scenario));
            if (value == null ||
                value.Type != factory.GetVariableInfo(variable).Type)
                throw new ArgumentException(
                    "A partial-term scenario has a value of the wrong type.",
                    nameof(scenario));
        }
    }

    private static ImmutableArray<IrVarId> CollectVariables(IrTerm root) {
        var variables = new SortedDictionary<int, IrVarId>();
        var seen = new HashSet<IrId>();
        Visit(root);
        return [.. variables.Values];

        void Visit(IrTerm term) {
            if (!seen.Add(term.Id)) return;
            switch (term) {
                case IrVariableTerm variable:
                    variables[variable.Variable.Value] = variable.Variable;
                    break;
                case IrOpaqueTerm opaque:
                    if (opaque.Receiver != null) Visit(opaque.Receiver);
                    foreach (var argument in opaque.Arguments)
                        Visit(argument);
                    break;
                case IrUnaryTerm unary:
                    Visit(unary.Operand);
                    break;
                case IrBinaryTerm binary:
                    Visit(binary.Left);
                    Visit(binary.Right);
                    break;
                case IrConditionalTerm conditional:
                    Visit(conditional.Condition);
                    Visit(conditional.WhenTrue);
                    Visit(conditional.WhenFalse);
                    break;
                case IrCastTerm cast:
                    Visit(cast.Operand);
                    break;
                case IrLengthTerm length:
                    Visit(length.Value);
                    break;
                case IrSequenceAccessTerm access:
                    Visit(access.Sequence);
                    Visit(access.Index);
                    break;
            }
        }
    }
}
