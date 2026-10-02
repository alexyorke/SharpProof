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

    internal static bool IsExplicitDelegateCreation(IDelegateCreationOperation creation)
    {
        // Explicit construction creates a fresh delegate. Method-group and
        // lambda conversions can reuse compiler-generated cached instances.
        return !creation.IsImplicit && creation.Type?.TypeKind == TypeKind.Delegate &&
            creation.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax &&
            creation.Target is IMethodReferenceOperation
            {
                Method: { MethodKind: MethodKind.Ordinary, Arity: 0, ContainingType.Arity: 0 }
            } target &&
            (target is { Instance: null, Method.IsStatic: true } ||
                target is { Instance.Type.IsReferenceType: true, Method: { IsStatic: false, IsVirtual: false, IsAbstract: false } });
    }

    internal static TotalScalarRule DelegateReceiver(IrFactory factory, IrTerm receiver, IrTerm? mayCheckNull = null)
    {
        var isNull = factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type));
        var fails = mayCheckNull == null ? isNull : factory.Binary(IrBinaryOperator.AndAlso, isNull, mayCheckNull);
        return new(receiver,
            [new(IrExceptionKind.Argument, fails)],
            FrontendSubsetClassification.Exact);
    }

    internal static bool DelegateValueEscapesDirectly(IDelegateCreationOperation creation)
    {
        return creation.Syntax.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax or
            Microsoft.CodeAnalysis.CSharp.Syntax.ArrowExpressionClauseSyntax;
    }

    internal static bool IsScalarBoxing(IConversionOperation conversion)
    {
        return conversion.Type?.SpecialType == SpecialType.System_Object && IsScalar(conversion.Operand.Type) &&
            conversion.OperatorMethod == null && conversion.Conversion.Exists && !conversion.IsTryCast;
    }
}
