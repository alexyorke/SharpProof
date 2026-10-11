namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    // An element access on a single-dimensional array faults on a null array,
    // then on an index outside it. A multidimensional array's bounds are not
    // modeled; `outside` is then an approximated flag.
    internal static TotalScalarRule ElementGuard(IrFactory factory, IrTerm receiver, IrTerm? index, IrTerm? outside)
    {
        var isNull = factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type));
        if (index == null)
        {
            return new(factory.Boolean(true), [new(IrExceptionKind.NullReference, isNull),
                new(IrExceptionKind.IndexOutOfRange, outside!)], FrontendSubsetClassification.Exact);
        }
        var info = factory.GetTypeInfo(index.Type);
        if (info.Kind != IrTypeKind.Integer || info.Width is not (8 or 16 or 32))
        { return Fail(factory, FrontendAbstention.UnsupportedType); }
        if (info.Width < 32)
        { index = factory.Cast(factory.IntegerType, index); }
        var length = factory.Length(receiver);
        var limit = index.Type == length.Type ? length : factory.Cast(index.Type, length);
        var beyond = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, index, limit);
        if (factory.GetTypeInfo(index.Type).Signed)
        {
            beyond = factory.Binary(IrBinaryOperator.OrElse,
                factory.Binary(IrBinaryOperator.LessThan, index, factory.Integer(index.Type, 0L)), beyond);
        }
        return new(factory.Boolean(true), [new(IrExceptionKind.NullReference, isNull),
            new(IrExceptionKind.IndexOutOfRange, beyond)], FrontendSubsetClassification.Exact);
    }

    // Only the intrinsic string indexer with a literal ^1 has this exact
    // lowering: read Length, subtract one, then read Chars. Other Index and
    // Range operations remain outside the supported subset.
    internal static bool IsLastStringIndex(IImplicitIndexerReferenceOperation indexer)
    {
        return indexer.Instance.Type?.SpecialType == SpecialType.System_String &&
            indexer.Type?.SpecialType == SpecialType.System_Char &&
            indexer.Argument is IUnaryOperation
            {
                OperatorKind: UnaryOperatorKind.Hat, OperatorMethod: null,
                Operand.ConstantValue: { HasValue: true, Value: 1 }
            } &&
            indexer.LengthSymbol is IPropertySymbol
            {
                IsStatic: false, MetadataName: "Length", Parameters.Length: 0,
                ContainingType.SpecialType: SpecialType.System_String,
                Type.SpecialType: SpecialType.System_Int32
            } &&
            indexer.IndexerSymbol is IPropertySymbol
            {
                IsStatic: false, IsIndexer: true, MetadataName: "Chars", Parameters.Length: 1,
                ContainingType.SpecialType: SpecialType.System_String,
                Type.SpecialType: SpecialType.System_Char
            } chars && chars.Parameters[0].Type.SpecialType == SpecialType.System_Int32;
    }

    // Stores and approximated reads cover single- and multidimensional arrays
    // of value-domain elements with 8- to 32-bit integer indexes.
    internal static bool IsModeledElementAccess(IArrayElementReferenceOperation access)
    {
        return access.ArrayReference.Type is IArrayTypeSymbol array && array.Rank == access.Indices.Length &&
            IsValueDomain(array.ElementType) && access.Indices.All(index =>
                index.Type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_SByte or
                    SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_Char or SpecialType.System_UInt32);
    }

    internal static bool ArrayStoreNeedsCompatibility(ITypeSymbol element)
    {
        if (element.IsValueType)
        { return false; }
        if (!element.IsSealed)
        { return true; }
        if (element is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } named)
        { return false; }
        for (INamedTypeSymbol? current = named; current != null; current = current.ContainingType)
        {
            if (current.OriginalDefinition.TypeParameters.Any(parameter => parameter.Variance != VarianceKind.None))
            { return true; }
        }
        return false;
    }

    private static TotalScalarRule ArrayReadRule(IrFactory factory, IArrayElementReferenceOperation access, ImmutableArray<IrTerm> operands)
    {
        if (access.Indices.Length != 1 || access.ArrayReference.Type is not IArrayTypeSymbol { IsSZArray: true } array ||
            !IsScalar(array.ElementType) || operands.Length != 2)
        { return Fail(factory, FrontendAbstention.UnsupportedOperationKind); }
        var receiver = operands[0];
        var index = operands[1];
        var info = factory.GetTypeInfo(index.Type);
        if (info.Kind != IrTypeKind.Integer || info.Width is not (8 or 16 or 32 or 64))
        { return Fail(factory, FrontendAbstention.UnsupportedType); }
        // Long/ulong indexing inserts native-width conversions whose overflow
        // can precede even the null check. Source evidence does not bind that
        // execution architecture, so keep those indexes closed.
        if (info.Width == 64)
        { return Fail(factory, FrontendAbstention.UnsupportedOperationKind); }
        if (info.Width < 32)
        { index = factory.Cast(factory.IntegerType, index); }
        var length = factory.Length(receiver);
        var limit = index.Type == length.Type ? length : factory.Cast(index.Type, length);
        var outside = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, index, limit);
        if (factory.GetTypeInfo(index.Type).Signed)
        {
            outside = factory.Binary(IrBinaryOperator.OrElse,
                factory.Binary(IrBinaryOperator.LessThan, index, factory.Integer(index.Type, 0L)), outside);
        }
        // The bounds guard precedes the unsigned-to-signed reinterpretation.
        var narrow = index.Type == factory.IntegerType ? index : factory.Cast(factory.IntegerType, index);
        return new(factory.SequenceAccess(receiver, narrow),
            [new(IrExceptionKind.NullReference, factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type))),
             new(IrExceptionKind.IndexOutOfRange, outside)], FrontendSubsetClassification.Exact);
    }

    // Whether the receiver of a field or element store is state this body
    // created and still holds alone: a new object or array, or a local that
    // only ever holds them and is only dereferenced. A constructor that lets
    // `this` escape makes its object observable. The control flow graph may
    // capture the receiver and carries no semantic model, so the receiver is
    // found from its syntax.
    internal static bool IsFreshReceiver(SyntaxNode receiver, Compilation? compilation)
    {
        while (receiver is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax parenthesized)
        { receiver = parenthesized.Expression; }
        if (compilation == null || !compilation.SyntaxTrees.Contains(receiver.SyntaxTree))
        { return false; }
        var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, receiver.SyntaxTree);
        if (receiver is Microsoft.CodeAnalysis.CSharp.Syntax.BaseObjectCreationExpressionSyntax or
            Microsoft.CodeAnalysis.CSharp.Syntax.ArrayCreationExpressionSyntax)
        { return IsFreshCreation(model.GetOperation(receiver), model); }
        if (receiver is not Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax name ||
            model.GetSymbolInfo(name).Symbol is not ILocalSymbol { RefKind: RefKind.None } local ||
            local.ContainingSymbol is not IMethodSymbol owner ||
            owner.DeclaringSyntaxReferences.Length != 1 ||
            model.GetOperation(owner.DeclaringSyntaxReferences[0].GetSyntax()) is not { } root)
        { return false; }
        foreach (var operation in root.DescendantsAndSelf())
        {
            switch (operation)
            {
                case IVariableDeclaratorOperation declarator when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local):
                    if (declarator.Initializer != null && !IsFreshCreation(declarator.Initializer.Value, model))
                    { return false; }
                    break;
                case ILocalReferenceOperation use when SymbolEqualityComparer.Default.Equals(use.Local, local):
                    if (!IsContainedUse(use, model) || InNestedFunction(use, root))
                    { return false; }
                    break;
            }
        }
        return true;

        static bool IsContainedUse(ILocalReferenceOperation use, SemanticModel model)
        {
            return use.Parent switch
            {
                IArrayElementReferenceOperation element => element.ArrayReference == use,
                IFieldReferenceOperation field => field.Instance == use,
                IPropertyReferenceOperation { Property.Name: "Length" } length => length.Instance == use,
                ISimpleAssignmentOperation assignment => assignment.Target == use && IsFreshCreation(assignment.Value, model),
                _ => false
            };
        }

        static bool InNestedFunction(IOperation use, IOperation root)
        {
            for (var current = use.Parent; current != null && current != root; current = current.Parent)
            {
                if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                { return true; }
            }
            return false;
        }
    }

    private static bool IsFreshCreation(IOperation? value, SemanticModel model)
    {
        return value switch
        {
            IArrayCreationOperation => true,
            IObjectCreationOperation { Constructor: { } constructor } => constructor.IsImplicitlyDeclared ||
                constructor.DeclaringSyntaxReferences.Length == 1 &&
                constructor.DeclaringSyntaxReferences[0].SyntaxTree == model.SyntaxTree &&
                model.GetOperation(constructor.DeclaringSyntaxReferences[0].GetSyntax()) is { } body &&
                body.Descendants().OfType<IInstanceReferenceOperation>().All(static receiver =>
                    receiver.Parent is IFieldReferenceOperation field && field.Instance == receiver ||
                    IsObjectConstructorCall(receiver.Parent)),
            _ => false
        };
    }
}
