using System.Collections.Immutable;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;

namespace SharpProof.Fuzz;

public enum PartialTermSemanticOutcome
{
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
    IrTerm NormalCompletion,
    ImmutableArray<ImmutableDictionary<IrVarId, IrValue>> Scenarios,
    ImmutableArray<PartialTermSemanticOutcome> ExpectedOutcomes);

public static class PartialTermSmtCaseGenerator
{
    public static PartialTermSmtCase Create(
        IrFactory factory,
        int seed)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (factory.Semantics != IrExecutionSemantics.Total)
        {
            throw new ArgumentException("The guarded campaign requires Total IR semantics.", nameof(factory));
        }
        var integerType = factory.GetOrCreateIntegerType(64, true);
        var guard = factory.CreateVariable(
            "partial-guard",
            factory.BooleanType);
        var divisor = factory.CreateVariable(
            "partial-divisor",
            integerType);
        // Keep the original three control bits for the established scenarios,
        // but use additional seed bits to vary the arithmetic operand.  The
        // old generator only consumed bits 0..2, so large campaigns repeated
        // the same eight semantic cases indefinitely.
        var operand = (seed & 8) != 0 ? -1L : (seed & 16) != 0 ? 1L : long.MinValue;
        var arithmeticOperand = factory.Integer(integerType, operand);
        var arithmetic = factory.Binary(
            (seed & 1) == 0
                ? IrBinaryOperator.Divide
                : IrBinaryOperator.Remainder,
            arithmeticOperand,
            factory.Variable(divisor));
        var comparison = factory.Binary(
            IrBinaryOperator.Equal,
            arithmetic,
            factory.Integer(integerType, 0));
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

        var nonzero = factory.Binary(IrBinaryOperator.NotEqual,
            factory.Variable(divisor), factory.Integer(integerType, 0));
        var noOverflow = factory.Unary(IrUnaryOperator.Not,
            factory.Binary(IrBinaryOperator.AndAlso,
                factory.Binary(IrBinaryOperator.Equal, arithmeticOperand, factory.Integer(integerType, long.MinValue)),
                factory.Binary(IrBinaryOperator.Equal, factory.Variable(divisor), factory.Integer(integerType, -1))));
        var arithmeticSafe = factory.Binary(IrBinaryOperator.AndAlso, nonzero, noOverflow);
        var shortCircuited = useOrElse ? factory.Variable(guard)
            : factory.Unary(IrUnaryOperator.Not, factory.Variable(guard));
        var normalCompletion = factory.Binary(IrBinaryOperator.OrElse, shortCircuited, arithmeticSafe);
        return new PartialTermSmtCase(
            formula, normalCompletion,
            [Scenario(factory, guard, shortCircuitGuard, divisor, undefinedDivisor),
             Scenario(factory, guard, undefinedGuard, divisor, undefinedDivisor)],
            [EvaluateCSharp(shortCircuitGuard), EvaluateCSharp(undefinedGuard)]);

        PartialTermSemanticOutcome EvaluateCSharp(bool guardValue)
        {
            try
            {
                // Execute the C# operators independently of the IR guard and interpreter.
                var result = useOrElse ? guardValue || ArithmeticIsZero() : guardValue && ArithmeticIsZero();
                return result ? PartialTermSemanticOutcome.DefinedTrue : PartialTermSemanticOutcome.DefinedFalse;
            }
            catch (ArithmeticException)
            {
                return PartialTermSemanticOutcome.Undefined;
            }
        }

        bool ArithmeticIsZero()
        {
            return ((seed & 1) == 0
                ? operand / undefinedDivisor : operand % undefinedDivisor) == 0;
        }
    }

    private static ImmutableDictionary<IrVarId, IrValue> Scenario(
        IrFactory factory,
        IrVarId guard,
        bool guardValue,
        IrVarId divisor,
        long divisorValue)
    {
        return ImmutableDictionary<IrVarId, IrValue>.Empty
            .Add(guard, factory.CreateBooleanValue(guardValue))
            .Add(divisor, factory.CreateIntegerValue(factory.GetVariableInfo(divisor).Type, divisorValue));
    }
}

public static class PartialTermSmtDifferentialOracle
{
    public static async Task<PartialTermSmtDifferentialResult> CompareAsync(
        IrFactory factory,
        PartialTermSmtCase generated,
        CancellationToken cancellationToken = default)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (generated == null)
        {
            throw new ArgumentNullException(nameof(generated));
        }

        if (generated.Formula.Type != factory.BooleanType)
        {
            throw new ArgumentException(
                "The partial-term formula must be Boolean.",
                nameof(generated));
        }

        if (generated.Scenarios.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one concrete partial-term scenario is required.",
                nameof(generated));
        }

        if (generated.ExpectedOutcomes.Length != generated.Scenarios.Length)
        {
            throw new ArgumentException("Every scenario requires a C# reference outcome.", nameof(generated));
        }
        var variables = IrTermAnalysis.CollectVariables(generated.Formula)
            .Concat(IrTermAnalysis.CollectVariables(generated.NormalCompletion)).Distinct()
            .OrderBy(static variable => variable.Value)
            .ToImmutableArray();
        var interpreter = new IrInterpreter(factory);
        using var session = FuzzSmtSession.Create(factory);
        var kernel = session.Kernel;
        var definedTrue = 0;
        var definedFalse = 0;
        var undefined = 0;

        for (var index = 0; index < generated.Scenarios.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scenario = generated.Scenarios[index];
            ValidateScenario(factory, variables, scenario);
            var completion = interpreter.Evaluate(generated.NormalCompletion, scenario, cancellationToken);
            var expected = completion.Value is { Kind: IrValueKind.Boolean, Boolean: false }
                ? PartialTermSemanticOutcome.Undefined
                : Classify(interpreter.Evaluate(generated.Formula, scenario, cancellationToken));
            if (expected != generated.ExpectedOutcomes[index])
            {
                return Result(FuzzOracleStatus.Mismatch, index + 1, definedTrue, definedFalse, undefined,
                    $"The guarded interpreter disagrees with C# execution in scenario {index}.");
            }

            Count(
                generated.ExpectedOutcomes[index],
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
            var completionQuery = CreateQuery(generated.NormalCompletion);
            var completionProof = await kernel.VerifyAsync(completionQuery, cancellationToken).ConfigureAwait(false);
            ProofOutcome proof = completionProof;
            PartialTermSemanticOutcome? actual;
            if (completionProof is RefutedOutcome)
            {
                actual = PartialTermSemanticOutcome.Undefined;
            }
            else if (completionProof is ProvenOutcome)
            {
                proof = await kernel.VerifyAsync(CreateQuery(generated.Formula), cancellationToken).ConfigureAwait(false);
                actual = Classify(proof);
            }
            else
            {
                actual = null;
            }

            VerificationQuery CreateQuery(IrTerm predicate)
            {
                return new(factory, assumptions,
                    new Goal(factory, predicate, ProofDiagnosticKind.InternalConsistency, new SourceLocationId(index)));
            }
            if (actual == null)
            {
                return Result(
                    FuzzOracleStatus.Abstained,
                    index + 1,
                    definedTrue,
                    definedFalse,
                    undefined,
                    "The backend returned " +
                    Describe(proof) +
                    $" for partial-term scenario {index}.");
            }

            if (actual != expected)
            {
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
        int ordinal)
    {
        return new(
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
    }

    private static IrTerm Literal(
        IrFactory factory,
        IrValue value)
    {
        return value.Kind switch
        {
            IrValueKind.Boolean => factory.Boolean(value.Boolean),
            IrValueKind.Integer => factory.Integer(value.Type, value.Integer),
            _ => throw new ArgumentException(
                "Partial-term scenarios support only Boolean and integer values.",
                nameof(value))
        };
    }

    private static PartialTermSemanticOutcome? Classify(
        IrEvaluationResult result)
    {
        return result.Status switch
        {
            IrEvaluationStatus.Value when
                result.Value is { Kind: IrValueKind.Boolean } value =>
                value.Boolean
                    ? PartialTermSemanticOutcome.DefinedTrue
                    : PartialTermSemanticOutcome.DefinedFalse,
            IrEvaluationStatus.Exception =>
                PartialTermSemanticOutcome.Undefined,
            _ => null
        };
    }

    internal static PartialTermSemanticOutcome? Classify(
        ProofOutcome outcome)
    {
        return outcome switch
        {
            ProvenOutcome => PartialTermSemanticOutcome.DefinedTrue,
            RefutedOutcome => PartialTermSemanticOutcome.DefinedFalse,
            _ => null
        };
    }

    private static string Describe(ProofOutcome outcome)
    {
        return outcome switch
        {
            ProvenOutcome => "Proven",
            RefutedOutcome => "Refuted",
            UnknownOutcome unknown =>
                "Unknown(" + unknown.Reason + ")",
            _ => outcome.GetType().Name
        };
    }

    private static void Count(
        PartialTermSemanticOutcome outcome,
        ref int definedTrue,
        ref int definedFalse,
        ref int undefined)
    {
        switch (outcome)
        {
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
        string detail)
    {
        return new(
            status,
            scenarioCount,
            definedTrue,
            definedFalse,
            undefined,
            detail);
    }

    private static void ValidateScenario(
        IrFactory factory,
        ImmutableArray<IrVarId> variables,
        ImmutableDictionary<IrVarId, IrValue> scenario)
    {
        foreach (var variable in variables)
        {
            if (!scenario.TryGetValue(variable, out var value))
            {
                throw new ArgumentException(
                    "A partial-term scenario does not assign every variable.",
                    nameof(scenario));
            }

            if (value == null ||
                value.Type != factory.GetVariableInfo(variable).Type)
            {
                throw new ArgumentException(
                    "A partial-term scenario has a value of the wrong type.",
                    nameof(scenario));
            }
        }
    }

}
