namespace SharpProof.Verify;

public enum ApproximationReason {
    UnsupportedOperation,
    UnresolvedApi,
    AbstractJoin,
    Widening,
    Budget,
    ExternalBoundary
}

public enum ProofDiagnosticKind {
    EffectContract,
    Precondition,
    Postcondition,
    InternalConsistency
}

public readonly record struct SourceLocationId {
    public SourceLocationId(int value) {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public int Value { get; }

    public override string ToString() => "location" + Value;
}

public abstract class Justification {
    private protected Justification() { }
}

public abstract class ProofJustification : Justification {
    private protected ProofJustification() { }
}

public sealed class SpecJustification(SpecId spec) : ProofJustification {
    public SpecId Spec { get; } = !spec.IsDefault
        ? spec
        : throw new ArgumentException("A non-default spec identifier is required.", nameof(spec));
}

public sealed class LoweredJustification(OperationId operation) : ProofJustification {
    public OperationId Operation { get; } = !operation.IsDefault
        ? operation
        : throw new ArgumentException("A non-default operation identifier is required.", nameof(operation));
}

public sealed class UserAssumedJustification(SourceLocationId location) : ProofJustification {
    public SourceLocationId Location { get; } = location;
}

public sealed class ApproximatedJustification(ApproximationReason reason) : Justification {
    public ApproximationReason Reason { get; } = reason;
}

public sealed class Assumption {
    internal Assumption(
        IrFactory factory,
        IrTerm predicate,
        ProofJustification justification) {
        FactoryGuards.RequireBooleanTerm(factory, predicate, nameof(predicate));
        Justification = justification ?? throw new ArgumentNullException(nameof(justification));
        Predicate = predicate;
    }

    public IrTerm Predicate { get; }
    public ProofJustification Justification { get; }
}

public sealed class Goal(IrFactory factory, IrTerm predicate,
    ProofDiagnosticKind diagnostic, SourceLocationId location) {
    public IrTerm Predicate { get; } = FactoryGuards.RequireBooleanTerm(factory, predicate, nameof(predicate));
    public ProofDiagnosticKind Diagnostic { get; } = diagnostic;
    public SourceLocationId Location { get; } = location;
}

internal static class FactoryGuards {
    internal static IrTerm RequireBooleanTerm(
        IrFactory factory,
        IrTerm term,
        string parameterName) {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (term == null) throw new ArgumentNullException(parameterName);
        factory.EnsureTerm(term, parameterName);
        if (term.Type != factory.BooleanType)
            throw new ArgumentException("A Boolean IR term is required.", parameterName);
        return term;
    }
}
