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

public readonly struct SourceLocationId : IEquatable<SourceLocationId> {
    public SourceLocationId(int value) {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public int Value { get; }

    public bool Equals(SourceLocationId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is SourceLocationId other && Equals(other);
    public override int GetHashCode() => Value;
    public override string ToString() => "location" + Value;
    public static bool operator ==(SourceLocationId left, SourceLocationId right) => left.Equals(right);
    public static bool operator !=(SourceLocationId left, SourceLocationId right) => !left.Equals(right);
}

public abstract class Justification {
    private protected Justification() { }
}

public abstract class ProofJustification : Justification {
    private protected ProofJustification() { }
}

public sealed class SpecJustification : ProofJustification {
    public SpecJustification(SpecId spec) {
        if (spec.IsDefault) throw new ArgumentException("A non-default spec identifier is required.", nameof(spec));
        Spec = spec;
    }

    public SpecId Spec { get; }
}

public sealed class LoweredJustification : ProofJustification {
    public LoweredJustification(OperationId operation) {
        if (operation.IsDefault)
            throw new ArgumentException("A non-default operation identifier is required.", nameof(operation));
        Operation = operation;
    }

    public OperationId Operation { get; }
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

public sealed class Goal {
    public Goal(
        IrFactory factory,
        IrTerm predicate,
        ProofDiagnosticKind diagnostic,
        SourceLocationId location) {
        FactoryGuards.RequireBooleanTerm(factory, predicate, nameof(predicate));
        Predicate = predicate;
        Diagnostic = diagnostic;
        Location = location;
    }

    public IrTerm Predicate { get; }
    public ProofDiagnosticKind Diagnostic { get; }
    public SourceLocationId Location { get; }
}

internal static class FactoryGuards {
    internal static void RequireBooleanTerm(
        IrFactory factory,
        IrTerm term,
        string parameterName) {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (term == null) throw new ArgumentNullException(parameterName);
        IrTerm interned;
        try {
            interned = factory.GetTerm(term.Id);
        }
        catch (ArgumentException exception) {
            throw new ArgumentException(
                "The term belongs to a different IR factory.",
                parameterName,
                exception);
        }
        if (!ReferenceEquals(interned, term))
            throw new ArgumentException("The term is not interned by the supplied factory.", parameterName);
        if (term.Type != factory.BooleanType)
            throw new ArgumentException("A Boolean IR term is required.", parameterName);
    }
}
