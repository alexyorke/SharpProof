namespace SharpProof.ContractForGenerator;

internal static class GeneratedDiagnosticDescriptors {
    private const string Category = "SharpProof.ContractFor.Usage";

    internal static readonly DiagnosticDescriptor InvalidTarget = Create(
        "SPCF0001",
        "Invalid ContractFor target",
        "Contract companion '{0}' does not identify one resolvable named target type");

    internal static readonly DiagnosticDescriptor DuplicateCompanion = Create(
        "SPCF0002",
        "Duplicate ContractFor companion",
        "Target type '{0}' has multiple ContractFor companions; exactly one is required");

    internal static readonly DiagnosticDescriptor InvalidCompanionType = Create(
        "SPCF0003",
        "Invalid ContractFor companion type",
        "Contract companion '{0}' must be a static class whose generic arity and constraints exactly match target '{1}'");

    internal static readonly DiagnosticDescriptor MissingMember = Create(
        "SPCF0004",
        "Missing ContractFor member",
        "Target method '{0}' has no exact ordinary companion member in '{1}'");

    internal static readonly DiagnosticDescriptor SignatureMismatch = Create(
        "SPCF0005",
        "ContractFor member signature mismatch",
        "Companion method '{0}' does not exactly match a target overload, including receiver, generic constraints, ref kinds, nullability, and return type");

    internal static readonly DiagnosticDescriptor AmbiguousMember = Create(
        "SPCF0006",
        "Ambiguous ContractFor member",
        "Companion mapping for method '{0}' is ambiguous");

    internal static readonly DiagnosticDescriptor BodyRequired = Create(
        "SPCF0007",
        "ContractFor member body required",
        "Companion method '{0}' must have a compiler-bound source body");

    internal static readonly DiagnosticDescriptor NestedClause = Create(
        "SPCF0008",
        "Contract clause is not directly owned by companion member",
        "Contract.{0} must be directly compiler-bound in companion method '{1}', not inside a nested function");

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string message) =>
        new(
            id,
            title,
            message,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description:
                "Validates compiler-bound ContractFor companion declarations without textual signature reconstruction.");
}
