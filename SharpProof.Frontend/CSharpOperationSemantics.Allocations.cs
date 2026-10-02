namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsStringConcatenation(IOperation operation)
    {
        return operation is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null, IsLifted: false,
            Type.SpecialType: SpecialType.System_String,
            LeftOperand.Type.SpecialType: SpecialType.System_String,
            RightOperand.Type.SpecialType: SpecialType.System_String
        };
    }

    internal static IrTerm StringConcatenationAllocates(IrFactory factory, ImmutableArray<IrTerm> operands)
    {
        IrTerm seen = factory.Boolean(false);
        IrTerm allocates = factory.Boolean(false);
        foreach (var operand in operands)
        {
            // Total Length(null) is zero, matching String.Concat's treatment.
            var nonempty = factory.Binary(IrBinaryOperator.GreaterThan, factory.Length(operand),
                factory.Integer(factory.IntegerType, 0));
            allocates = factory.Binary(IrBinaryOperator.OrElse, allocates,
                factory.Binary(IrBinaryOperator.AndAlso, seen, nonempty));
            seen = factory.Binary(IrBinaryOperator.OrElse, seen, nonempty);
        }
        return allocates;
    }

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
                target is { Instance.Type.IsReferenceType: true, Method: { IsStatic: false, IsVirtual: false, IsOverride: false, IsAbstract: false } });
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
