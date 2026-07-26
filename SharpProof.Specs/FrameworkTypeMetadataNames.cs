namespace SharpProof.Specs;

/// <summary>
/// Canonical metadata identities used by semantic analyses.
/// Keep framework type-name declarations in the spec layer so consumers do
/// not grow independent string-based BCL authorities.
/// </summary>
public static class FrameworkTypeMetadataNames {
    public const string ArgumentNullException = "System.ArgumentNullException";
    public const string ArrayTypeMismatchException =
        "System.ArrayTypeMismatchException";
    public const string ConditionalAttribute =
        "System.Diagnostics.ConditionalAttribute";
    public const string DivideByZeroException =
        "System.DivideByZeroException";
    public const string Exception = "System.Exception";
    public const string IndexOutOfRangeException =
        "System.IndexOutOfRangeException";
    public const string InvalidCastException = "System.InvalidCastException";
    public const string InvalidOperationException =
        "System.InvalidOperationException";
    public const string NullReferenceException =
        "System.NullReferenceException";
    public const string OverflowException = "System.OverflowException";
}
