namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsCoreObjectCreation(IObjectCreationOperation creation)
    {
        return creation is
        {
            Type.SpecialType: SpecialType.System_Object,
            Constructor.Parameters.Length: 0, Arguments.Length: 0, Initializer: null
        } &&
            creation.Constructor.DeclaringSyntaxReferences.Length == 0;
    }

    internal static bool IsScalarBoxing(IConversionOperation conversion)
    {
        return conversion.Type?.SpecialType == SpecialType.System_Object && IsScalar(conversion.Operand.Type) &&
            conversion.OperatorMethod == null && conversion.Conversion.Exists && !conversion.IsTryCast;
    }
}
