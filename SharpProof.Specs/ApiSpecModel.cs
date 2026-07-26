namespace SharpProof.Specs;

public enum SpecEvidenceKind {
    Documented,
    Observed
}

[Flags]
public enum SpecEffect {
    None = 0,
    Unknown = 1 << 0,
    ReadsReceiverState = 1 << 1,
    ReadsArgumentState = 1 << 2,
    WritesReceiverState = 1 << 3,
    WritesArgumentState = 1 << 4,
    ReadsAmbientState = 1 << 5,
    WritesAmbientState = 1 << 6,
    InputOutput = 1 << 7,
    Synchronization = 1 << 8,
    NativeCode = 1 << 9,
    Reflection = 1 << 10,
    Nondeterminism = 1 << 11
}

public enum SpecAllocationBehavior {
    None,
    MayAllocate,
    Unknown
}

public enum SpecThrowBehavior {
    DoesNotThrow,
    MayThrow,
    Unknown
}

public enum SpecNullness {
    NotApplicable,
    NonNull,
    MaybeNull,
    Null,
    Unknown
}

public enum SpecCardinality {
    NotApplicable,
    Empty,
    NonEmpty,
    Exact,
    Unknown
}

public enum SpecTargetMemberKind {
    Constructor,
    Method,
    PropertyGet
}

public enum SpecValueType {
    Boolean,
    Integer,
    String,
    Reference,
    Sequence
}

public enum SpecVariableRole {
    Receiver,
    Parameter,
    Result
}

public enum SpecUnaryOperator {
    Not,
    Negate
}

public enum SpecBinaryOperator {
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    AndAlso,
    OrElse,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    StringConcat
}

public sealed record SpecEvidence(SpecEvidenceKind Kind, string Source);

public sealed record SpecEffectFacet(SpecEffect Effects, SpecEvidence Evidence);

public sealed record SpecAllocationFacet(
    SpecAllocationBehavior Behavior,
    SpecEvidence Evidence);

public sealed record SpecThrowFacet(
    SpecThrowBehavior Behavior,
    ImmutableArray<string> ExceptionMetadataNames,
    SpecEvidence Evidence);

public sealed record SpecNullnessFacet(SpecNullness Result, SpecEvidence Evidence);

public sealed record SpecCardinalityFacet(
    SpecCardinality Result,
    int? ExactCount,
    SpecEvidence Evidence);

public sealed record ApiSpecFacets(
    SpecEffectFacet Effects,
    SpecAllocationFacet Allocation,
    SpecThrowFacet Throws,
    SpecNullnessFacet Nullness,
    SpecCardinalityFacet Cardinality);

public sealed record ApiSpecTarget(
    string WitnessIdentifier,
    string DocumentationCommentId,
    string ContainingTypeMetadataName,
    SpecTargetMemberKind MemberKind,
    string MemberName,
    bool IsStatic,
    int GenericArity,
    SpecValueType? ReceiverType,
    ImmutableArray<SpecValueType> ParameterTypes,
    SpecValueType? ResultType);

public abstract record SpecTermDeclaration(SpecValueType Type);

public sealed record SpecVariableDeclaration(
    SpecVariableRole Role,
    int Ordinal,
    SpecValueType Type)
    : SpecTermDeclaration(Type);

public sealed record SpecBooleanDeclaration(bool Value)
    : SpecTermDeclaration(SpecValueType.Boolean);

public sealed record SpecIntegerDeclaration(long Value)
    : SpecTermDeclaration(SpecValueType.Integer);

public sealed record SpecStringDeclaration(string Value)
    : SpecTermDeclaration(SpecValueType.String);

public sealed record SpecNullDeclaration(SpecValueType Type)
    : SpecTermDeclaration(Type);

public sealed record SpecUnaryDeclaration(
    SpecUnaryOperator Operator,
    SpecTermDeclaration Operand,
    SpecValueType Type)
    : SpecTermDeclaration(Type);

public sealed record SpecBinaryDeclaration(
    SpecBinaryOperator Operator,
    SpecTermDeclaration Left,
    SpecTermDeclaration Right,
    SpecValueType Type)
    : SpecTermDeclaration(Type);

public sealed record SpecConditionalDeclaration(
    SpecTermDeclaration Condition,
    SpecTermDeclaration WhenTrue,
    SpecTermDeclaration WhenFalse,
    SpecValueType Type)
    : SpecTermDeclaration(Type);

public sealed record SpecLengthDeclaration(SpecTermDeclaration Value)
    : SpecTermDeclaration(SpecValueType.Integer);

public sealed record SpecPostconditionDeclaration(
    SpecTermDeclaration Condition,
    SpecEvidence Evidence);

public sealed record ApiSpecDeclaration(
    ApiSpecTarget Target,
    ApiSpecFacets Facets,
    ImmutableArray<SpecPostconditionDeclaration> Postconditions);

public sealed record SpecVariableInfo(
    SpecVarId Id,
    SpecVariableRole Role,
    int Ordinal,
    SpecValueType Type);

public abstract record SpecTerm(SpecValueType Type);

public sealed record SpecVariableTerm(SpecVarId Variable, SpecValueType Type)
    : SpecTerm(Type);

public sealed record SpecBooleanTerm(bool Value)
    : SpecTerm(SpecValueType.Boolean);

public sealed record SpecIntegerTerm(long Value)
    : SpecTerm(SpecValueType.Integer);

public sealed record SpecStringTerm(string Value)
    : SpecTerm(SpecValueType.String);

public sealed record SpecNullTerm(SpecValueType Type)
    : SpecTerm(Type);

public sealed record SpecUnaryTerm(
    SpecUnaryOperator Operator,
    SpecTerm Operand,
    SpecValueType Type)
    : SpecTerm(Type);

public sealed record SpecBinaryTerm(
    SpecBinaryOperator Operator,
    SpecTerm Left,
    SpecTerm Right,
    SpecValueType Type)
    : SpecTerm(Type);

public sealed record SpecConditionalTerm(
    SpecTerm Condition,
    SpecTerm WhenTrue,
    SpecTerm WhenFalse,
    SpecValueType Type)
    : SpecTerm(Type);

public sealed record SpecLengthTerm(SpecTerm Value)
    : SpecTerm(SpecValueType.Integer);

public sealed record SpecPostcondition(SpecTerm Condition, SpecEvidence Evidence);

public sealed class ApiSpecTemplate {
    internal ApiSpecTemplate(
        SpecId id,
        ApiSpecTarget target,
        ApiSpecFacets facets,
        ImmutableArray<SpecVariableInfo> variables,
        SpecVarId? receiver,
        ImmutableArray<SpecVarId> parameters,
        SpecVarId? result,
        ImmutableArray<SpecPostcondition> postconditions) {
        Id = id;
        Target = target;
        Facets = facets;
        Variables = variables;
        Receiver = receiver;
        Parameters = parameters;
        Result = result;
        Postconditions = postconditions;
    }

    public SpecId Id { get; }
    public ApiSpecTarget Target { get; }
    public ApiSpecFacets Facets { get; }
    public ImmutableArray<SpecVariableInfo> Variables { get; }
    public SpecVarId? Receiver { get; }
    public ImmutableArray<SpecVarId> Parameters { get; }
    public SpecVarId? Result { get; }
    public ImmutableArray<SpecPostcondition> Postconditions { get; }
}
