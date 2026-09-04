using System.Collections.Immutable;
using SharpProof.Host;
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
    string Detail);

public sealed class FiniteDomainSmtDifferentialOracle
{
    private const int MaximumAssignmentCount = 65_536;
    private static readonly bool[] BooleanDomain = [false, true];

    public static ImmutableArray<long> IntegerDomain
    {
        get;
    } =
        [-2, -1, 0, 1, 2];

    public static bool IsDefinedForAllAssignments(
        IrFactory factory,
        IrTerm formula,
        CancellationToken cancellationToken = default)
    {
        ValidateFormula(factory, formula);

        var variables = IrTermAnalysis.CollectVariables(formula)
            .OrderBy(static variable => variable.Value)
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
                evaluated.Status == IrEvaluationStatus.Value &&
                evaluated.Value is { Kind: IrValueKind.Boolean },
            requireMatch: true,
            cancellationToken: cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Oracle methods intentionally share an instance-shaped test API.")]
    public async Task<FiniteDomainDifferentialResult> CompareAsync(
        IrFactory factory,
        IrTerm formula,
        CancellationToken cancellationToken = default)
    {
        ValidateFormula(factory, formula);

        cancellationToken.ThrowIfCancellationRequested();
        var variables = IrTermAnalysis.CollectVariables(formula)
            .OrderBy(static variable => variable.Value)
            .ToImmutableArray();
        if (!TryGetFiniteDomainAssignmentCount(
                factory,
                variables,
                out var assignmentCount))
        {
            return new FiniteDomainDifferentialResult(
                FuzzOracleStatus.Abstained,
                FiniteDomainSatisfiability.Unsatisfiable,
                null,
                0,
                "The generated formula contains a variable outside the " +
                "finite Boolean/integer domain.");
        }

        if (assignmentCount > MaximumAssignmentCount)
        {
            return new FiniteDomainDifferentialResult(
                FuzzOracleStatus.Abstained,
                FiniteDomainSatisfiability.Unsatisfiable,
                null,
                0,
                "The finite Boolean/integer domain exceeds the assignment " +
                "limit of " + MaximumAssignmentCount + ".");
        }

        var assumptions = ImmutableArray.CreateBuilder<Assumption>(
            variables.Length);
        foreach (var variable in variables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryCreateDomainPredicate(
                    factory,
                    variable,
                    out var predicate))
            {
                return new FiniteDomainDifferentialResult(
                    FuzzOracleStatus.Abstained,
                    FiniteDomainSatisfiability.Unsatisfiable,
                    null,
                    assumptions.Count,
                    "The generated formula contains a variable outside the " +
                    "finite Boolean/integer domain.");
            }
            assumptions.Add(
                new Assumption(
                    factory,
                    predicate!,
                    new LoweredJustification(
                        factory.CreateOperation(
                            "finite-domain-v" +
                            variable.Value))));
        }

        var expected = SearchFiniteDomain(
            factory,
            formula,
            variables,
            static evaluated =>
                evaluated.Status == IrEvaluationStatus.Value &&
                evaluated.Value is
                {
                    Kind: IrValueKind.Boolean,
                    Boolean: true
                },
            requireMatch: false,
            cancellationToken: cancellationToken)
            ? FiniteDomainSatisfiability.Satisfiable
            : FiniteDomainSatisfiability.Unsatisfiable;
        var query = new VerificationQuery(
            factory,
            assumptions,
            new Goal(
                factory,
                factory.Unary(IrUnaryOperator.Not, formula),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        ContainerNativeLibrary.InstallZ3ResolverRequired(
            typeof(Microsoft.Z3.Context).Assembly);
        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend)
            .VerifyAsync(query, cancellationToken)
            .ConfigureAwait(false);
        var actual = outcome switch
        {
            RefutedOutcome => FiniteDomainSatisfiability.Satisfiable,
            ProvenOutcome => FiniteDomainSatisfiability.Unsatisfiable,
            _ => (FiniteDomainSatisfiability?)null
        };
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
            var type = factory.GetTypeInfo(
                factory.GetVariableInfo(variable).Type).Kind;
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
                        factory.CreateIntegerValue(value);
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

    private static bool TryCreateDomainPredicate(
        IrFactory factory,
        IrVarId variable,
        out IrTerm? predicate)
    {
        var variableTerm = factory.Variable(variable);
        var type = factory.GetTypeInfo(
            factory.GetVariableInfo(variable).Type).Kind;
        switch (type)
        {
            case IrTypeKind.Boolean:
                predicate = Or(
                    factory,
                    [
                        factory.Binary(
                            IrBinaryOperator.Equal,
                            variableTerm,
                            factory.Boolean(false)),
                        factory.Binary(
                            IrBinaryOperator.Equal,
                            variableTerm,
                            factory.Boolean(true))
                    ]);
                return true;
            case IrTypeKind.Integer:
                predicate = Or(
                    factory,
                    IntegerDomain.Select(
                        value => factory.Binary(
                            IrBinaryOperator.Equal,
                            variableTerm,
                            factory.Integer(value))));
                return true;
            default:
                predicate = null;
                return false;
        }
    }

    private static IrTerm Or(
        IrFactory factory,
        IEnumerable<IrTerm> predicates)
    {
        using var enumerator = predicates.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return factory.Boolean(false);
        }

        var result = enumerator.Current;
        while (enumerator.MoveNext())
        {
            result = factory.Binary(
                IrBinaryOperator.OrElse,
                result,
                enumerator.Current);
        }

        return result;
    }

}

public static class IrStructuralShrinker
{
    public static async Task<IrTerm> MinimizeAsync(
        IrFactory factory,
        IrTerm term,
        Func<IrTerm, CancellationToken, Task<bool>> preservesMismatch,
        CancellationToken cancellationToken = default)
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

        var candidates = new List<IrTerm>();
        var seen = new HashSet<IrId>();
        var originalSize = StructuralSize(term);

        void Add(IrTerm candidate)
        {
            if (candidate.Id == term.Id)
            {
                return;
            }

            if (StructuralSize(candidate) >= originalSize)
            {
                return;
            }

            if (seen.Add(candidate.Id))
            {
                candidates.Add(candidate);
            }
        }

        var children = IrTraversal.GetChildren(term);
        foreach (var child in children)
        {
            if (child.Type == term.Type)
            {
                Add(child);
            }
        }

        if (term.Type == factory.BooleanType)
        {
            Add(factory.Boolean(false));
            Add(factory.Boolean(true));
        }
        else if (term.Type == factory.IntegerType)
        {
            Add(factory.Integer(0));
            Add(factory.Integer(1));
        }

        for (var index = 0; index < children.Length; index++)
        {
            foreach (var childCandidate in GetCandidates(
                         factory,
                         children[index]))
            {
                var rebuilt = TryReplaceChild(
                    factory,
                    term,
                    index,
                    childCandidate);
                if (rebuilt != null)
                {
                    Add(rebuilt);
                }
            }
        }
        return [.. candidates];
    }

    public static int StructuralSize(IrTerm term)
    {
        if (term == null)
        {
            throw new ArgumentNullException(nameof(term));
        }

        var seen = new HashSet<IrId>();
        Visit(term);
        return seen.Count;

        void Visit(IrTerm current)
        {
            if (!seen.Add(current.Id))
            {
                return;
            }

            foreach (var child in IrTraversal.GetChildren(current))
            {
                Visit(child);
            }
        }
    }

    private static IrTerm? TryReplaceChild(
        IrFactory factory,
        IrTerm term,
        int index,
        IrTerm replacement)
    {
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
