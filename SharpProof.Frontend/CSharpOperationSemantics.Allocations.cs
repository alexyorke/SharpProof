namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsSupportedArrayCreation(IArrayCreationOperation creation)
    {
        return creation is { Type: IArrayTypeSymbol { IsSZArray: true }, DimensionSizes.Length: 1 } &&
            creation.DimensionSizes[0].Type?.SpecialType == SpecialType.System_Int32 && IsReferenceDomain(creation.Type) &&
            (creation.Initializer == null ||
                creation.DimensionSizes[0].ConstantValue is { HasValue: true, Value: int length } &&
                length == creation.Initializer.ElementValues.Length &&
                creation.Initializer.ElementValues.All(IsConstantArrayElement));
    }

    internal static bool IsSupportedConstantArrayCollection(ICollectionExpressionOperation collection)
    {
        return collection.Type is IArrayTypeSymbol { IsSZArray: true } && collection.ConstructMethod == null &&
            !collection.Elements.IsEmpty && IsReferenceDomain(collection.Type) &&
            collection.Elements.All(IsConstantArrayElement);
    }

    private static bool IsConstantArrayElement(IOperation element)
    {
        return element is not ISpreadOperation && element.ConstantValue.HasValue &&
            (IsScalar(element.Type) || element.ConstantValue.Value == null && (element.Type == null || IsReferenceDomain(element.Type)) ||
                element.Type?.SpecialType == SpecialType.System_String && element.ConstantValue.Value is string text &&
                Utf16WellFormedness.IsWellFormed(text));
    }

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

    // Whether the compiler may flatten this expression into an enclosing `+`
    // or `+=` string concatenation. Parentheses, casts and `!` do not stop it.
    internal static bool IsConcatenationOperand(SyntaxNode syntax)
    {
        var parent = syntax.Parent;
        while (parent is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax or
            Microsoft.CodeAnalysis.CSharp.Syntax.CastExpressionSyntax ||
            parent.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression))
        { parent = parent!.Parent; }
        return parent.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddExpression) ||
            parent.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddAssignmentExpression);
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

    // A class constructor that runs exactly its own body: the class derives
    // from object, has no instance member initializers or primary
    // constructor, and the constructor chains to no other constructor.
    internal static bool IsPlainConstructor(IMethodSymbol constructor, CancellationToken cancellationToken)
    {
        if (constructor is not { MethodKind: MethodKind.Constructor, IsStatic: false } ||
            constructor.ContainingType is not
            {
                TypeKind: TypeKind.Class, IsRecord: false, BaseType.SpecialType: SpecialType.System_Object
            } type)
        { return false; }
        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax syntax ||
                syntax.Initializer is { } initializer &&
                (initializer.ThisOrBaseKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThisKeyword) ||
                    initializer.ArgumentList.Arguments.Count != 0))
            { return false; }
        }
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax declaration ||
                declaration.ParameterList != null || declaration.Members.Any(static member => member switch
                {
                    Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax field => !IsStatic(field.Modifiers) &&
                        field.Declaration.Variables.Any(static variable => variable.Initializer != null),
                    Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax property => !IsStatic(property.Modifiers) &&
                        property.Initializer != null,
                    Microsoft.CodeAnalysis.CSharp.Syntax.EventFieldDeclarationSyntax eventField => !IsStatic(eventField.Modifiers) &&
                        eventField.Declaration.Variables.Any(static variable => variable.Initializer != null),
                    _ => false
                }))
            { return false; }
        }
        return true;

        static bool IsStatic(SyntaxTokenList modifiers)
        { return modifiers.Any(static modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)); }
    }

    internal static bool IsObjectConstructorCall(IOperation? operation)
    {
        return operation is IInvocationOperation
        {
            TargetMethod: { MethodKind: MethodKind.Constructor, ContainingType.SpecialType: SpecialType.System_Object },
            Arguments.Length: 0
        };
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

    internal static bool DelegateValueIsErased(IDelegateCreationOperation creation, Compilation? compilation)
    {
        if (compilation == null)
        { return false; }
        var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, creation.Syntax.SyntaxTree);
        SyntaxNode value = creation.Syntax;
        while (value.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax parenthesized)
        { value = parenthesized; }
        ILocalSymbol? local = null;
        if (value.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax assignment &&
            assignment.Right == value && assignment.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionStatementSyntax &&
            model.GetOperation(assignment) is ISimpleAssignmentOperation { IsRef: false } store)
        {
            // An explicit discard omits construction in Debug and Release.
            if (store.Target is IDiscardOperation)
            { return true; }
            local = (store.Target as ILocalReferenceOperation)?.Local;
        }
        else if (value.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.EqualsValueClauseSyntax { Parent: Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax declaration })
        { local = model.GetDeclaredSymbol(declaration) as ILocalSymbol; }

        // Release can discard an unobserved local's standalone stores. A read,
        // ref use, capture or assignment whose value escapes keeps construction.
        // Debug retains user locals even when they are never read.
        if (model.Compilation.Options.OptimizationLevel != OptimizationLevel.Release ||
            local is not { RefKind: RefKind.None, ContainingSymbol: IMethodSymbol owner } ||
            owner.DeclaringSyntaxReferences.Length != 1 ||
            model.GetOperation(owner.DeclaringSyntaxReferences[0].GetSyntax()) is not { } body)
        { return false; }
        foreach (var reference in body.Descendants().OfType<ILocalReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(reference.Local, local))
            { continue; }
            if (!(reference.Parent is ISimpleAssignmentOperation { IsRef: false } write &&
                ReferenceEquals(write.Target, reference) &&
                write.Syntax.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionStatementSyntax))
            { return false; }
            // Even a write-only capture becomes an observable closure-field store.
            for (var ancestor = reference.Parent; ancestor != null && ancestor != body; ancestor = ancestor.Parent)
            {
                if (ancestor is IAnonymousFunctionOperation or ILocalFunctionOperation)
                { return false; }
            }
        }
        return true;
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
