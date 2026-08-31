namespace SharpProof.Effects;

internal static class MethodGroupConversionFacts
{
    internal static bool UsesDelegateConstructorNullCheck(
        IMethodReferenceOperation methodReference)
    {
        return methodReference is
        {
            Instance: not null,
            Method:
            {
                IsStatic: false,
                IsVirtual: false,
                IsAbstract: false
            }
        };
    }

    internal static IMethodReferenceOperation?
        GetDelegateConstructorCheckedTarget(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IDelegateCreationOperation creation:
                    operation = creation.Target;
                    continue;
                case IConversionOperation
                {
                    OperatorMethod: null
                } conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                case IMethodReferenceOperation methodReference
                    when UsesDelegateConstructorNullCheck(methodReference):
                    return methodReference;
                default:
                    return null;
            }
        }
    }
}
