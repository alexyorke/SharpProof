namespace SharpProof.Verify;

public enum ApproximationReason
{
    UnsupportedOperation,
    UnresolvedApi,
    AbstractJoin,
    Widening,
    Budget,
    ExternalBoundary
}

public enum ProofDiagnosticKind
{
    EffectContract,
    Precondition,
    Postcondition,
    InternalConsistency
}

public readonly record struct SourceLocationId
{
    public SourceLocationId(int value)
    {
        Value = ArgumentNullGuard.RequireNonnegative(value, nameof(value));
    }

    public int Value
    {
        get;
    }

    public override string ToString()
    {
        return "location" + Value;
    }
}

public abstract class Justification
{
    private protected Justification()
    {
    }
}

public abstract class ProofJustification : Justification
{
    private protected ProofJustification()
    {
    }
}

public sealed class SpecJustification(SpecId spec) : ProofJustification
{
    public SpecId Spec
    {
        get;
    } = !spec.IsDefault
        ? spec
        : throw new ArgumentException("A non-default spec identifier is required.", nameof(spec));
}

public sealed class LoweredJustification(OperationId operation) : ProofJustification
{
    public OperationId Operation
    {
        get;
    } = !operation.IsDefault
        ? operation
        : throw new ArgumentException("A non-default operation identifier is required.", nameof(operation));
}

public sealed class UserAssumedJustification(SourceLocationId location) : ProofJustification
{
    public SourceLocationId Location { get; } = location;
}

public sealed class ApproximatedJustification(ApproximationReason reason) : Justification
{
    public ApproximationReason Reason { get; } = reason;
}

public sealed class Assumption
{
    internal Assumption(
        IrFactory factory,
        IrTerm predicate,
        ProofJustification justification)
    {
        FactoryGuards.RequireBooleanTerm(factory, predicate, nameof(predicate));
        Justification = ArgumentNullGuard.NotNull(justification, nameof(justification));
        Predicate = predicate;
    }

    public IrTerm Predicate
    {
        get;
    }
    public ProofJustification Justification
    {
        get;
    }
}

public sealed partial class Goal
{
    public Goal(
        IrFactory factory,
        IrTerm predicate,
        ProofDiagnosticKind diagnostic,
        SourceLocationId location)
        : this(
            FactoryGuards.RequireBooleanTerm(factory, predicate, nameof(predicate)),
            diagnostic,
            location,
            default)
    {
    }
}

internal static class FactoryGuards
{
    internal static IrTerm RequireBooleanTerm(
        IrFactory factory,
        IrTerm term,
        string parameterName)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        term = ArgumentNullGuard.NotNull(term, parameterName);

        factory.EnsureTerm(term, parameterName);
        if (term.Type != factory.BooleanType)
        {
            throw new ArgumentException("A Boolean IR term is required.", parameterName);
        }

        return term;
    }
}
