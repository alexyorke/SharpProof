using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor NullableReturnContractViolationRule = CreateDescriptor(
        NullableReturnContractViolationId,
        "Nullable return contract violated",
        "Method '{0}' can return null despite contract '{1}'",
        "Nullability",
        DiagnosticSeverity.Warning,
        "Reports a reachable normal return that violates the declared nullable return contract.");

    public static readonly DiagnosticDescriptor NullableParameterPostconditionViolationRule = CreateDescriptor(
        NullableParameterPostconditionViolationId,
        "Nullable parameter postcondition violated",
        "Method '{0}' can complete with parameter '{1}' null despite contract '{2}'",
        "Nullability",
        DiagnosticSeverity.Warning,
        "Reports a reachable normal completion that violates a nullable parameter postcondition.");

    public static readonly DiagnosticDescriptor NullableMemberContractViolationRule = CreateDescriptor(
        NullableMemberContractViolationId,
        "Nullable member contract violated",
        "Method '{0}' can complete with member '{1}' null despite contract '{2}'",
        "Nullability",
        DiagnosticSeverity.Warning,
        "Reports a reachable normal completion that violates a member-not-null contract.");

    public static readonly DiagnosticDescriptor UnsafeNullForgivingOperatorRule = CreateDescriptor(
        UnsafeNullForgivingOperatorId,
        "Null-forgiving operator is unsafe",
        "Null-forgiving operator can suppress a feasible null value for '{0}'",
        "Nullability",
        DiagnosticSeverity.Warning,
        "Reports a null-forgiving operator reached by a proven null execution.");

    public static readonly DiagnosticDescriptor UnnecessaryNullForgivingOperatorRule = CreateDescriptor(
        UnnecessaryNullForgivingOperatorId,
        "Null-forgiving operator is unnecessary",
        "Null-forgiving operator is unnecessary because '{0}' is proven non-null",
        "Nullability",
        DiagnosticSeverity.Info,
        "Reports a null-forgiving operator whose operand is already proven non-null.");

    public static readonly DiagnosticDescriptor SuggestNullableContractRule = CreateDescriptor(
        SuggestNullableContractId,
        "Nullable contract can be declared",
        "Method '{0}' satisfies nullable contract '{1}'",
        "Nullability",
        DiagnosticSeverity.Info,
        "Suggests a nullable contract proved by every relevant completion path.");

    public static readonly DiagnosticDescriptor NullableVerificationInconclusiveRule = CreateDescriptor(
        NullableVerificationInconclusiveId,
        "Nullable verification was inconclusive",
        "Nullable contract '{1}' on '{0}' could not be verified: {2}",
        "Nullability",
        DiagnosticSeverity.Info,
        "Reports bounded nullable proofs that ended as unsupported or unknown.");
}
