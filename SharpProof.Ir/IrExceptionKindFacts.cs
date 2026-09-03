namespace SharpProof.Ir;

internal static class IrExceptionKindFacts
{
    internal static IrExceptionKind? FromException(Exception exception)
    {
        return exception switch
        {
            DivideByZeroException => IrExceptionKind.DivideByZero,
            OverflowException => IrExceptionKind.Overflow,
            NullReferenceException => IrExceptionKind.NullReference,
            IndexOutOfRangeException => IrExceptionKind.IndexOutOfRange,
            InvalidCastException => IrExceptionKind.InvalidCast,
            _ => null
        };
    }
}
