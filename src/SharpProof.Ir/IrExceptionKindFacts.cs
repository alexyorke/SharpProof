namespace SharpProof.Ir;

internal static class IrExceptionKindFacts
{
    // The one table from a modeled fault kind to the runtime exception type.
    // Unknown and Explicit faults are typed only as System.Exception, so only
    // handlers for Exception or Object catch them.
    internal static string MetadataName(IrExceptionKind kind)
    {
        return kind switch
        {
            IrExceptionKind.DivideByZero => "System.DivideByZeroException",
            IrExceptionKind.Overflow => "System.OverflowException",
            IrExceptionKind.NullReference => "System.NullReferenceException",
            IrExceptionKind.IndexOutOfRange => "System.IndexOutOfRangeException",
            IrExceptionKind.ArgumentOutOfRange => "System.ArgumentOutOfRangeException",
            IrExceptionKind.InvalidCast => "System.InvalidCastException",
            IrExceptionKind.Argument => "System.ArgumentException",
            IrExceptionKind.Unknown or IrExceptionKind.Explicit => "System.Exception",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    internal static IrExceptionKind? FromException(Exception exception)
    {
        ArgumentNullGuard.NotNull(exception, nameof(exception));
        return exception switch
        {
            DivideByZeroException => IrExceptionKind.DivideByZero,
            OverflowException => IrExceptionKind.Overflow,
            NullReferenceException => IrExceptionKind.NullReference,
            IndexOutOfRangeException => IrExceptionKind.IndexOutOfRange,
            ArgumentOutOfRangeException when exception.GetType() == typeof(ArgumentOutOfRangeException) => IrExceptionKind.ArgumentOutOfRange,
            InvalidCastException => IrExceptionKind.InvalidCast,
            ArgumentException when exception.GetType() == typeof(ArgumentException) => IrExceptionKind.Argument,
            _ => null
        };
    }
}
