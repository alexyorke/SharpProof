namespace SharpProof.Specs;
/// <summary>
/// Canonical metadata identities used by semantic analyses.
/// Keep framework type-name declarations in the spec layer so consumers do
/// not grow independent string-based BCL authorities.
/// </summary>
public static class FrameworkTypeMetadataNames
{
    public const string ArgumentNullException = "System.ArgumentNullException";
    public const string ArrayTypeMismatchException =
        "System.ArrayTypeMismatchException";
    public const string ConditionalAttribute =
        "System.Diagnostics.ConditionalAttribute";
    public const string DivideByZeroException =
        "System.DivideByZeroException";
    public const string Exception = "System.Exception";
    public const string ExpressionOfT =
        "System.Linq.Expressions.Expression`1";
    public const string GeneratedCodeAttribute =
        "System.CodeDom.Compiler.GeneratedCodeAttribute";
    public const string IndexOutOfRangeException =
        "System.IndexOutOfRangeException";
    public const string InvalidCastException = "System.InvalidCastException";
    public const string InvalidOperationException =
        "System.InvalidOperationException";
    public const string IDisposable = "System.IDisposable";
    public const string ICriticalNotifyCompletion =
        "System.Runtime.CompilerServices.ICriticalNotifyCompletion";
    public const string INotifyCompletion =
        "System.Runtime.CompilerServices.INotifyCompletion";
    public const string ModuleInitializerAttribute =
        "System.Runtime.CompilerServices.ModuleInitializerAttribute";
    public static readonly string Monitor = "System.Threading.Monitor";
    public const string NullReferenceException =
        "System.NullReferenceException";
    public const string OverflowException = "System.OverflowException";
    public const string ReferenceAssemblyAttribute = "System.Runtime.CompilerServices.ReferenceAssemblyAttribute";
    public const string SwitchExpressionException =
        "System.Runtime.CompilerServices.SwitchExpressionException";
    public const string TypeInitializationException =
        "System.TypeInitializationException";
}
