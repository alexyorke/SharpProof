using System.Collections.Immutable;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;

namespace SharpProof.Fuzz;

public enum FiniteDomainSatisfiability
{
    Satisfiable,
    Unsatisfiable
}

public sealed record FiniteDomainDifferentialResult(
    FuzzOracleStatus Status,
    FiniteDomainSatisfiability Expected,
    FiniteDomainSatisfiability? Actual,
    int FiniteDomainAssumptions,
    string Detail)
{
    public string Detail { get; init; } = Detail ?? throw new System.ArgumentNullException(nameof(Detail));
}

public static class FiniteDomainSmtDifferentialOracle
{
    private const int MaximumAssignmentCount = 65_536;
    private static readonly ImmutableArray<bool> BooleanDomain = [false, true];

    public static ImmutableArray<long> IntegerDomain
    {
        get;
    } =
        [-2, -1, 0, 1, 2];

    public static bool IsDefinedForAllAssignments(
        IrFactory factory,
        IrTerm formula,
        CancellationToken cancellationToken)
    {
        ValidateFormula(factory, formula);

        var variables = IrTermAnalysis.CollectVariables(formula)
            .OrderBy(static variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable.Value))
            .ToImmutableArray();
        if (!TryGetFiniteDomainAssignmentCount(
                factory,
                variables,
                out var assignmentCount) ||
            assignmentCount > MaximumAssignmentCount)
        {
            return false;
        }

        return SearchFiniteDomain(
            factory,
            formula,
            variables,
            static evaluated =>
                evaluated is null ? throw new System.ArgumentNullException(nameof(evaluated)) : (evaluated.Status == IrEvaluationStatus.Value &&
                evaluated.Value is { Kind: IrValueKind.Boolean }),
            requireMatch: true,
            cancellationToken: cancellationToken);
    }


    internal static bool IsSafeForAllAssignments(IrFactory factory, IrTerm formula,
        IrTerm safety, CancellationToken cancellationToken)
    {
        ValidateFormula(factory, formula);
        ValidateFormula(factory, safety);
        if (!ValidatedFiniteDomain.TryCreate(factory, formula, out var domain, out _, safety))
        {
            return false;
        }
        return SearchFiniteDomain(factory, safety, domain!.Variables,
            static evaluated => evaluated is null ? throw new System.ArgumentNullException(nameof(evaluated)) : (evaluated.Status == IrEvaluationStatus.Value &&
                evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: true }),
            requireMatch: true, cancellationToken);
    }
    internal sealed class ValidatedFiniteDomain
    {
        private ValidatedFiniteDomain(IrFactory factory, ImmutableArray<IrVarId> variables,
            int assignmentCount)
        {

            factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
            RepositoryParameterGuard.RequireInitialized(variables.IsDefault, nameof(variables), "Array must be initialized.");
            Factory = factory;
            Variables = variables;
            AssignmentCount = assignmentCount;
        }

        internal IrFactory Factory { get; }
        internal ImmutableArray<IrVarId> Variables { get; }
        internal int AssignmentCount { get; }

        internal static bool TryCreate(IrFactory factory, IrTerm formula,
            out ValidatedFiniteDomain? domain, out FiniteDomainDifferentialResult? rejection,
            IrTerm? safety = null)
        {
            formula = RepositoryParameterGuard.NotNull(formula, nameof(formula));
            var variables = IrTermAnalysis.CollectVariables(formula)
                .Concat(safety == null ? Enumerable.Empty<IrVarId>() : IrTermAnalysis.CollectVariables(safety))
                .Distinct().OrderBy(static variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable.Value)).ToImmutableArray();
            if (!TryGetFiniteDomainAssignmentCount(factory, variables, out var assignmentCount))
            {
                domain = null;
                rejection = new FiniteDomainDifferentialResult(
                    FuzzOracleStatus.Abstained,
                    FiniteDomainSatisfiability.Unsatisfiable,
                    null,
                    0,
                    "The generated formula contains a variable outside the " +
                    "finite Boolean/integer domain.");
                return false;
            }
            if (assignmentCount > MaximumAssignmentCount)
            {
                domain = null;
                rejection = new FiniteDomainDifferentialResult(
                    FuzzOracleStatus.Abstained,
                    FiniteDomainSatisfiability.Unsatisfiable,
                    null,
                    0,
                    "The finite Boolean/integer domain exceeds the assignment " +
                    "limit of " + MaximumAssignmentCount + ".");
                return false;
            }
            domain = new ValidatedFiniteDomain(factory, variables, assignmentCount);
            rejection = null;
            return true;
        }
    }

    internal sealed class FiniteDomainPreparedFormula
    {
        private FiniteDomainPreparedFormula(IrTerm formula, ValidatedFiniteDomain domain,
            bool anyTrue)
        {
            domain = RepositoryParameterGuard.NotNull(domain, nameof(domain));
            formula = RepositoryParameterGuard.NotNull(formula, nameof(formula));
            Formula = formula;
            Domain = domain;
            AnyTrue = anyTrue;
        }

        internal IrTerm Formula { get; }
        internal ValidatedFiniteDomain Domain { get; }
        internal ImmutableArray<IrVarId> Variables => Domain.Variables;
        internal int AssignmentCount => Domain.AssignmentCount;
        internal bool AnyTrue { get; }

        internal static bool TryCreate(
            IrFactory factory,
            IrTerm formula,
            CancellationToken cancellationToken,
            out FiniteDomainPreparedFormula? prepared)
        {
            ValidateFormula(factory, formula);

            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidatedFiniteDomain.TryCreate(factory, formula, out var domain, out _))
            {
                prepared = null;
                return false;
            }
            var validatedDomain = domain!;
            var variables = validatedDomain.Variables;

            var anyTrue = false;
            if (!SearchFiniteDomain(
                    factory,
                    formula,
                    variables,
                    evaluated =>
                    {
                        if (evaluated.Status != IrEvaluationStatus.Value ||
                            evaluated.Value is not
                            {
                                Kind: IrValueKind.Boolean
                            } value)
                        {
                            return false;
                        }

                        anyTrue |= value.Boolean;
                        return true;
                    },
                    requireMatch: true,
                    cancellationToken: cancellationToken))
            {
                prepared = null;
                return false;
            }

            prepared = new FiniteDomainPreparedFormula(
                formula,
                validatedDomain,
                anyTrue);
            return true;
        }
    }

    internal static bool TryPrepareForCampaign(IrFactory factory, IrTerm formula,
        CancellationToken cancellationToken, out FiniteDomainPreparedFormula? prepared)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        formula = RepositoryParameterGuard.NotNull(formula, nameof(formula));
        return FiniteDomainPreparedFormula.TryCreate(factory, formula, cancellationToken,
            out prepared);
    }

    internal static async Task<FiniteDomainDifferentialResult>
        ComparePreparedAsync(
            IrFactory factory,
            FiniteDomainPreparedFormula prepared,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ValidateFormula(factory, prepared.Formula);
        cancellationToken.ThrowIfCancellationRequested();
        return await ComparePreparedCoreAsync(
                prepared.Domain,
                prepared.Formula,
                prepared.AnyTrue
                    ? FiniteDomainSatisfiability.Satisfiable
                    : FiniteDomainSatisfiability.Unsatisfiable,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<FiniteDomainDifferentialResult> CompareAsync(
        IrFactory factory,
        IrTerm formula,
        CancellationToken cancellationToken)
    {
        ValidateFormula(factory, formula);

        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidatedFiniteDomain.TryCreate(factory, formula, out var domain, out var rejection))
        {
            return rejection!;
        }
        var validatedDomain = domain!;
        var variables = validatedDomain.Variables;

        var expected = SearchFiniteDomain(
                factory,
                formula,
                variables,
                static evaluated =>
                    evaluated is null ? throw new System.ArgumentNullException(nameof(evaluated)) : (evaluated.Status == IrEvaluationStatus.Value &&
                    evaluated.Value is
                    {
                        Kind: IrValueKind.Boolean,
                        Boolean: true
                    }),
                requireMatch: false,
                cancellationToken: cancellationToken)
            ? FiniteDomainSatisfiability.Satisfiable
            : FiniteDomainSatisfiability.Unsatisfiable;
        return await ComparePreparedCoreAsync(
                validatedDomain,
                formula,
                expected,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<FiniteDomainDifferentialResult>
        ComparePreparedCoreAsync(
            ValidatedFiniteDomain domain,
            IrTerm formula,
            FiniteDomainSatisfiability expected,
            CancellationToken cancellationToken)
    {
        var factory = domain.Factory;
        var variables = domain.Variables;
        var assumptions = ImmutableArray.CreateBuilder<Assumption>(
            variables.Length);
        foreach (var variable in variables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var predicate = CreateDomainPredicate(factory, variable);
            assumptions.Add(
                new Assumption(
                    factory,
                    predicate,
                    new LoweredJustification(
                        factory.CreateOperation(
                            "finite-domain-v" +
                            variable.Value))));
        }
        var query = new VerificationQuery(
            factory,
            assumptions,
            new Goal(
                factory,
                factory.Unary(IrUnaryOperator.Not, formula),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        using var session = FuzzSmtSession.Create(factory);
        var kernel = new ProofKernel(session);
        var outcome = await kernel
            .VerifyAsync(query, cancellationToken)
            .ConfigureAwait(false);
        var actual = outcome switch
        {
            RefutedOutcome => FiniteDomainSatisfiability.Satisfiable,
            ProvenOutcome => FiniteDomainSatisfiability.Unsatisfiable,
            _ => (FiniteDomainSatisfiability?)null
        };
        if (outcome is UnknownOutcome { Reason: AbstentionReason.ResourceLimit })
        {
            actual = await CheckConcreteDomainAsync(domain, formula, cancellationToken).ConfigureAwait(false);
        }
        if (actual == null)
        {
            var detail = outcome is UnknownOutcome unknown
                ? "SMT abstained: " + unknown.Reason + "."
                : "SMT returned an unrecognized proof outcome.";
            return new FiniteDomainDifferentialResult(
                FuzzOracleStatus.Abstained,
                expected,
                null,
                assumptions.Count,
                detail);
        }
        return new FiniteDomainDifferentialResult(
            actual == expected
                ? FuzzOracleStatus.Agreement
                : FuzzOracleStatus.Mismatch,
            expected,
            actual,
            assumptions.Count,
            actual == expected
                ? ""
                : "Finite enumeration reported " +
                  expected +
                  " while SMT reported " +
                  actual +
                  ".");
    }

    internal static async Task<FiniteDomainSatisfiability?> CheckConcreteDomainAsync(
        ValidatedFiniteDomain domain, IrTerm formula, CancellationToken cancellationToken)
    {
        domain = RepositoryParameterGuard.NotNull(domain, nameof(domain));
        formula = RepositoryParameterGuard.NotNull(formula, nameof(formula));
        var factory = domain.Factory;
        var variables = domain.Variables;
        var assignmentCount = domain.AssignmentCount;
        // Nested bitvector division can exhaust a symbolic domain check even
        // with few inputs. Partition that same bounded domain into exact input
        // assignments; every partition must be UNSAT before reporting UNSAT.
        using var session = FuzzSmtSession.Create(factory);
        var kernel = new ProofKernel(session);
        var goal = new Goal(factory, factory.Unary(IrUnaryOperator.Not, formula),
            ProofDiagnosticKind.InternalConsistency, new SourceLocationId(0));
        for (var assignment = 0; assignment < assignmentCount; assignment++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = assignment;
            var assumptions = ImmutableArray.CreateBuilder<Assumption>(variables.Length);
            foreach (var variable in variables)
            {
                var type = factory.GetVariableInfo(variable).Type;
                var boolean = type == factory.BooleanType;
                var domainSize = boolean ? BooleanDomain.Length : IntegerDomain.Length;
                var ordinal = remaining % domainSize;
                remaining /= domainSize;
                var literal = CreateDomainLiteral(factory, type, ordinal);
                assumptions.Add(new Assumption(factory,
                    factory.Binary(IrBinaryOperator.Equal, factory.Variable(variable), literal),
                    new LoweredJustification(factory.CreateOperation("finite-domain-assignment"))));
            }
            var proof = await kernel.VerifyAsync(new VerificationQuery(factory, assumptions, goal),
                cancellationToken).ConfigureAwait(false);
            if (proof is RefutedOutcome)
            {
                return FiniteDomainSatisfiability.Satisfiable;
            }
            if (proof is not ProvenOutcome)
            {
                return null;
            }
        }
        return FiniteDomainSatisfiability.Unsatisfiable;
    }

    private static void ValidateFormula(IrFactory factory, IrTerm formula)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (formula == null)
        {
            throw new ArgumentNullException(nameof(formula));
        }

        if (formula.Type != factory.BooleanType)
        {
            throw new ArgumentException(
                "The finite-domain formula must be Boolean.",
                nameof(formula));
        }
    }

    private static bool SearchFiniteDomain(
        IrFactory factory,
        IrTerm formula,
        ImmutableArray<IrVarId> variables,
        Func<IrEvaluationResult, bool> matches,
        bool requireMatch,
        CancellationToken cancellationToken)
    {

        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        formula = RepositoryParameterGuard.NotNull(formula, nameof(formula));
        matches = RepositoryParameterGuard.NotNull(matches, nameof(matches));
        RepositoryParameterGuard.RequireInitialized(variables.IsDefault, nameof(variables), "Array must be initialized.");
        var interpreter = new IrInterpreter(factory);
        var environment = new Dictionary<IrVarId, IrValue>();
        return Search(0);

        bool Search(int index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index == variables.Length)
            {
                var evaluated = interpreter.Evaluate(
                    formula,
                    environment,
                    cancellationToken);
                return matches(evaluated);
            }

            var variable = variables[index];
            var variableType = factory.GetVariableInfo(variable).Type;
            var type = factory.GetTypeInfo(
                variableType).Kind;
            if (type == IrTypeKind.Boolean)
            {
                foreach (var value in BooleanDomain)
                {
                    environment[variable] =
                        factory.CreateBooleanValue(value);
                    var child = Search(index + 1);
                    if (requireMatch ? !child : child)
                    {
                        environment.Remove(variable);
                        return requireMatch ? false : true;
                    }
                }
            }
            else if (type == IrTypeKind.Integer)
            {
                foreach (var value in IntegerDomain)
                {
                    environment[variable] =
                        factory.CreateIntegerValue(variableType, value);
                    var child = Search(index + 1);
                    if (requireMatch ? !child : child)
                    {
                        environment.Remove(variable);
                        return requireMatch ? false : true;
                    }
                }
            }
            else
            {
                return false;
            }
            environment.Remove(variable);
            return requireMatch;
        }
    }

    private static bool TryGetFiniteDomainAssignmentCount(
        IrFactory factory,
        ImmutableArray<IrVarId> variables,
        out int assignmentCount)
    {

        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.RequireInitialized(variables.IsDefault, nameof(variables), "Array must be initialized.");
        assignmentCount = 1;
        foreach (var variable in variables)
        {
            var type = factory.GetTypeInfo(
                factory.GetVariableInfo(variable).Type).Kind;
            var domainSize = type switch
            {
                IrTypeKind.Boolean => 2,
                IrTypeKind.Integer => IntegerDomain.Length,
                _ => 0
            };
            if (domainSize == 0)
            {
                return false;
            }

            if (assignmentCount > MaximumAssignmentCount / domainSize)
            {
                assignmentCount = MaximumAssignmentCount + 1;
                return true;
            }

            assignmentCount *= domainSize;
        }

        return true;
    }

    private static IrTerm CreateDomainLiteral(
        IrFactory factory,
        IrTypeId variableType,
        int ordinal)
    {
        RepositoryParameterGuard.RequireInitialized(ordinal < 0, nameof(ordinal), "Value must be initialized.");

        if (variableType == factory.BooleanType)
        {
            return factory.Boolean(BooleanDomain[ordinal]);
        }

        return factory.Integer(variableType, IntegerDomain[ordinal]);
    }

    private static IrTerm CreateDomainPredicate(IrFactory factory, IrVarId variable)
    {
        RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        var variableTerm = factory.Variable(variable);
        var variableType = factory.GetVariableInfo(variable).Type;
        var boolean = factory.GetTypeInfo(variableType).Kind == IrTypeKind.Boolean;
        var domainSize = boolean ? BooleanDomain.Length : IntegerDomain.Length;
        var result = Predicate(0);
        for (var index = 1; index < domainSize; index++)
        {
            result = factory.Binary(IrBinaryOperator.OrElse, result, Predicate(index));
        }
        return result;

        IrTerm Predicate(int index)
        {
            var literal = CreateDomainLiteral(factory, variableType, index);
            return factory.Binary(IrBinaryOperator.Equal, variableTerm, literal);
        }
    }

}

public static class IrStructuralShrinker
{
    public static async Task<IrTerm> MinimizeAsync(
        IrFactory factory,
        IrTerm term,
        Func<IrTerm, CancellationToken, Task<bool>> preservesMismatch,
        CancellationToken cancellationToken)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (term == null)
        {
            throw new ArgumentNullException(nameof(term));
        }

        if (preservesMismatch == null)
        {
            throw new ArgumentNullException(nameof(preservesMismatch));
        }

        var current = term;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = false;
            foreach (var candidate in GetCandidates(factory, current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await preservesMismatch(candidate, cancellationToken)
                        .ConfigureAwait(false))
                {
                    continue;
                }

                current = candidate;
                changed = true;
                break;
            }
            if (!changed)
            {
                return current;
            }
        }
    }

    public static ImmutableArray<IrTerm> GetCandidates(
        IrFactory factory,
        IrTerm term)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (term == null)
        {
            throw new ArgumentNullException(nameof(term));
        }

        return StructuralShrinkCandidates.GetCandidates(
            term,
            IrTraversal.GetChildren,
            static (parent, child) => child is null ? throw new System.ArgumentNullException(nameof(child)) : parent is null ? throw new System.ArgumentNullException(nameof(parent)) : (parent.Type == child.Type),
            StructuralSize,
            static candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Id),
            value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (GetDomainCandidates(factory, value)),
            (value, index, replacement) => replacement is null ? throw new System.ArgumentNullException(nameof(replacement)) : value is null ? throw new System.ArgumentNullException(nameof(value)) : (TryReplaceChild(
                factory,
                value,
                index,
                replacement)),
            EqualityComparer<IrId>.Default);
    }

    private static IEnumerable<IrTerm> GetDomainCandidates(
        IrFactory factory,
        IrTerm term)
    {
        if (term.Type == factory.BooleanType)
        {
            return [factory.Boolean(false), factory.Boolean(true)];
        }

        return term.Type == factory.IntegerType
            ? [factory.Integer(0), factory.Integer(1)]
            : [];
    }

    public static int StructuralSize(IrTerm term)
    {
        if (term == null)
        {
            throw new ArgumentNullException(nameof(term));
        }

        var seen = new HashSet<IrId>();
        var pending = new Stack<IrTerm>();
        pending.Push(term);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current.Id))
            {
                continue;
            }

            IrTraversal.PushChildren(current, pending);
        }
        return seen.Count;
    }

    private static IrTerm? TryReplaceChild(
        IrFactory factory,
        IrTerm term,
        int index,
        IrTerm replacement)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        replacement = RepositoryParameterGuard.NotNull(replacement, nameof(replacement));
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        try
        {
            return term switch
            {
                IrUnaryTerm unary when index == 0 =>
                    factory.Unary(unary.Operator, replacement),
                IrBinaryTerm binary =>
                    factory.Binary(
                        binary.Operator,
                        index == 0 ? replacement : binary.Left,
                        index == 1 ? replacement : binary.Right),
                IrConditionalTerm conditional =>
                    factory.Conditional(
                        index == 0 ? replacement : conditional.Condition,
                        index == 1 ? replacement : conditional.WhenTrue,
                        index == 2 ? replacement : conditional.WhenFalse),
                IrCastTerm cast when index == 0 =>
                    factory.Cast(cast.Type, replacement),
                IrLengthTerm when index == 0 =>
                    factory.Length(replacement),
                IrSequenceAccessTerm access =>
                    factory.SequenceAccess(
                        index == 0 ? replacement : access.Sequence,
                        index == 1 ? replacement : access.Index),
                _ => null
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
