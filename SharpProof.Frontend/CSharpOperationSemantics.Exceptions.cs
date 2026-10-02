namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static string ExceptionMetadataName(IrExceptionKind kind)
    {
        return kind switch
        {
            IrExceptionKind.DivideByZero => "System.DivideByZeroException",
            IrExceptionKind.Overflow => "System.OverflowException",
            IrExceptionKind.NullReference => "System.NullReferenceException",
            IrExceptionKind.IndexOutOfRange => "System.IndexOutOfRangeException",
            IrExceptionKind.InvalidCast => "System.InvalidCastException",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    // Resolve the runtime assembly that owns System.Object. A source-defined
    // same-name exception must never impersonate a local scalar fault.
    internal static bool TryCatchKinds(Compilation compilation, ITypeSymbol? catchType,
        out ImmutableArray<IrExceptionKind> kinds)
    {
        var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        var supported = new[]
        {
            // Roslyn represents a bare catch with the canonical System.Object.
            "System.Object", "System.Exception", "System.SystemException", "System.ArithmeticException",
            "System.DivideByZeroException", "System.OverflowException", "System.NullReferenceException",
            "System.IndexOutOfRangeException", "System.InvalidCastException"
        };
        if (catchType != null && !supported.Any(name =>
                SymbolEqualityComparer.Default.Equals(core.GetTypeByMetadataName(name), catchType)))
        {
            kinds = default;
            return false;
        }
        var matched = ImmutableArray.CreateBuilder<IrExceptionKind>();
        foreach (var kind in (IrExceptionKind[])Enum.GetValues(typeof(IrExceptionKind)))
        {
            var runtime = core.GetTypeByMetadataName(ExceptionMetadataName(kind));
            if (runtime == null)
            {
                kinds = default;
                return false;
            }
            for (var ancestor = runtime; ancestor != null; ancestor = ancestor.BaseType)
            {
                if (catchType == null || SymbolEqualityComparer.Default.Equals(ancestor, catchType))
                {
                    matched.Add(kind);
                    break;
                }
            }
        }
        kinds = matched.ToImmutable();
        return true;
    }

    internal static bool IsNullThrow(IOperation? expression)
    {
        var depth = 0;
        while (expression is IConversionOperation { OperatorMethod: null } conversion && depth++ < 256)
        { expression = conversion.Operand; }
        return expression is { ConstantValue.HasValue: true } && expression.ConstantValue.Value == null &&
            expression.Kind is OperationKind.Literal or OperationKind.DefaultValue;
    }
}
