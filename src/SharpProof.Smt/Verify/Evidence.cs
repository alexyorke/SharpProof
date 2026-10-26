namespace SharpProof.Verify;

internal enum ProofDiagnosticKind
{
    EffectContract,
    Precondition,
    Postcondition,
    InternalConsistency
}

internal readonly record struct SourceLocationId
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

internal abstract class ProofJustification
{
    private protected ProofJustification()
    {
    }
}

internal sealed class LoweredJustification(OperationId operation) : ProofJustification
{
    public OperationId Operation
    {
        get;
    } = !operation.IsDefault
        ? operation
        : throw new ArgumentException("A non-default operation identifier is required.", nameof(operation));
}

internal sealed class UserAssumedJustification(SourceLocationId location /* SPINT001: all values valid - every location index, including zero, is valid */) : ProofJustification
{
    public SourceLocationId Location { get; } = location;
}

internal sealed class Assumption
{
    public Assumption(
        IrFactory factory,
        IrTerm predicate,
        ProofJustification justification)
    {
        IrFactory.RequireBooleanTerm(factory, predicate, nameof(predicate));
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

internal sealed partial class Goal
{
    public static Goal CreateInternalConsistency(IrFactory factory)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        return new(
            factory,
            factory.Boolean(false),
            ProofDiagnosticKind.InternalConsistency,
            new SourceLocationId(0));
    }

    public Goal(
        IrFactory factory,
        IrTerm predicate,
        ProofDiagnosticKind diagnostic,
        SourceLocationId location /* SPINT001: all values valid - every location index, including zero, is valid */)
        : this(
            IrFactory.RequireBooleanTerm(factory, predicate, nameof(predicate)),
            diagnostic,
            location,
            default)
    {
    }
}
