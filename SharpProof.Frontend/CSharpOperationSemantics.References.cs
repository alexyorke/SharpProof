namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static TotalScalarRule ArrayEmpty(IrFactory factory, IrTypeId type)
    {
        return Exact(factory.EmptyArray(type));
    }

    internal static TotalScalarRule StringConcat(IrFactory factory, IrTerm left, IrTerm right)
    {
        return Exact(factory.RewriteBinary(IrBinaryOperator.StringConcat, left, right));
    }

    internal static bool IsReferenceDomain(ITypeSymbol? type)
    {
        return type?.SpecialType is SpecialType.System_Object or SpecialType.System_String ||
            type?.TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.Delegate ||
            // Arrays of arrays stay closed. Reads of non-scalar elements, and of
            // any multidimensional array, are approximations.
            type is IArrayTypeSymbol array && array.ElementType is not IArrayTypeSymbol && IsValueDomain(array.ElementType);
    }

    // An implicit conversion from a class, interface or delegate to a base
    // class or interface (an IEnumerator<T> to IDisposable) keeps the
    // reference and never throws.
    internal static IConversionOperation? ReferenceUpcast(IOperation operation)
    {
        return operation is IConversionOperation
        {
            IsTryCast: false, OperatorMethod: null,
            Conversion: { IsReference: true, IsImplicit: true },
            Type: { TypeKind: TypeKind.Class or TypeKind.Interface, SpecialType: not (SpecialType.System_String or SpecialType.System_Object) },
            Operand.Type: { TypeKind: TypeKind.Class or TypeKind.Interface or TypeKind.Delegate, SpecialType: not SpecialType.System_String }
        } conversion ? conversion : null;
    }

    // `(Derived)value` between class or interface types keeps the reference.
    // A non-null value of another runtime type throws InvalidCastException;
    // whether the value fits is unknown to the IR, so `fits` is approximated.
    internal static IConversionOperation? ReferenceDowncast(IOperation operation)
    {
        return operation is IConversionOperation
        {
            IsImplicit: false, IsTryCast: false, OperatorMethod: null,
            Conversion: { IsReference: true, IsImplicit: false },
            Type: { TypeKind: TypeKind.Class or TypeKind.Interface, SpecialType: not (SpecialType.System_String or SpecialType.System_Object) },
            Operand.Type: { TypeKind: TypeKind.Class or TypeKind.Interface, SpecialType: not SpecialType.System_String }
        } conversion ? conversion : null;
    }

    internal static TotalScalarRule ReferenceDowncast(IrFactory factory, IrTypeId type, IrTerm operand, IrTerm fits)
    {
        var fails = factory.Binary(IrBinaryOperator.AndAlso,
            factory.Binary(IrBinaryOperator.NotEqual, operand, factory.Null(operand.Type)), factory.Unary(IrUnaryOperator.Not, fits));
        return new(factory.Cast(type, operand), [new(IrExceptionKind.InvalidCast, fails)], FrontendSubsetClassification.Exact);
    }

    // A constructed signature type may differ from its declaration (a generic
    // container's type arguments). Class, interface and type-parameter values
    // are all references, so a cast bridges them; any other type must match.
    internal static bool SharesValueDomain(ITypeSymbol constructed, ITypeSymbol declared)
    {
        return SymbolEqualityComparer.Default.Equals(constructed, declared) ||
            IsBridgedReference(constructed) && IsBridgedReference(declared);

        static bool IsBridgedReference(ITypeSymbol type)
        {
            return type.TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.TypeParameter &&
                type.SpecialType is not (SpecialType.System_String or SpecialType.System_Object);
        }
    }

    // An implicit reference conversion keeps the same reference. Boxing is a
    // caller conversion and must remain visible even for an opaque callee.
    internal static IOperation OpaqueArgument(IOperation value)
    {
        if (value is not IConversionOperation { IsImplicit: true, OperatorMethod: null, Conversion.IsImplicit: true } conversion)
        { return value; }
        return conversion.Conversion.IsReference ? conversion.Operand : value;
    }

    internal static bool IsImplicitOpaqueBoxing(IConversionOperation conversion, Compilation? compilation)
    {
        if (conversion is not
            {
                IsImplicit: true, IsTryCast: false, OperatorMethod: null,
                Conversion: { Exists: true, IsIdentity: false, IsImplicit: true, IsReference: false, IsUserDefined: false },
                Type: { IsReferenceType: true, TypeKind: TypeKind.Class or TypeKind.Interface } target,
                Operand.Type: { IsReferenceType: false } source
            } || !IsOpaqueDomain(source) ||
            compilation is not Microsoft.CodeAnalysis.CSharp.CSharpCompilation csharp)
        { return false; }
        return csharp.ClassifyConversion(source, target).IsBoxing;
    }

    internal static bool IsValueDomain(ITypeSymbol? type)
    {
        return IsScalar(type) || IsReferenceDomain(type) || IsOpaqueDomain(type);
    }

    // Type-parameter and non-scalar struct values are opaque: they may be
    // stored, passed, returned and given to opaque calls, but no operator,
    // conversion or field read applies to them. Opaque-enabled body defaults
    // produce approximated values without invoking a constructor. Only opaque calls
    // can observe a struct's contents, so a call that mutates it through
    // `this` changes nothing the IR can read.
    internal static bool IsOpaqueDomain(ITypeSymbol? type)
    {
        return type is ITypeParameterSymbol ||
            type is { IsValueType: true, TypeKind: TypeKind.Struct or TypeKind.Enum } && !IsScalar(type);
    }

    // A type test on a type-parameter value runs no user code and cannot
    // throw. Its result is unknown to the IR. A fixed tested type can avoid
    // boxing, while an open tested type may require a box for a value-type T.
    internal static IOperation? OpaqueTypeTestOperand(IOperation operation)
    {
        return operation switch
        {
            IIsTypeOperation { ValueOperand.Type: ITypeParameterSymbol } test => test.ValueOperand,
            IIsPatternOperation
            {
                Pattern: ITypePatternOperation or IDeclarationPatternOperation { DeclaredSymbol: null },
                Value.Type: ITypeParameterSymbol
            } test => test.Value,
            _ => null
        };
    }

    // Only already-admitted type tests participate. A known-reference value
    // never needs a boxing allocation, regardless of the tested type.
    internal static bool OpaqueTypeTestMayAllocate(IOperation operation, Compilation? compilation, Action? spend = null)
    {
        if (OpaqueTypeTestOperand(operation)?.Type is not ITypeParameterSymbol { IsReferenceType: false } operandType)
        { return false; }
        var testedType = operation switch
        {
            IIsTypeOperation test => test.TypeOperand,
            IIsPatternOperation { Pattern: ITypePatternOperation pattern } => pattern.MatchedType,
            IIsPatternOperation { Pattern: IDeclarationPatternOperation { DeclaredSymbol: null } pattern } => pattern.MatchedType,
            _ => null
        };
        // An inferred/var pattern has no runtime tested type.
        if (testedType == null)
        { return false; }
        // When the value converts implicitly to the tested type, the compiler
        // emits only `box; ldnull; cgt.un`. A Debug build's JIT does not fold
        // that box, so the test allocates for a value-type T.
        if (compilation is not Microsoft.CodeAnalysis.CSharp.CSharpCompilation csharp ||
            csharp.Options.OptimizationLevel != OptimizationLevel.Release &&
                csharp.ClassifyConversion(operandType, testedType) is { IsImplicit: true, IsBoxing: true })
        { return true; }
        var remainingWork = 4096;
        var visited = new Dictionary<ITypeSymbol, bool>(SymbolEqualityComparer.Default);
        bool ContainsOpenType(ITypeSymbol current, int depth)
        {
            spend?.Invoke();
            if (current is ITypeParameterSymbol || current.TypeKind == TypeKind.Error || depth > 64 || --remainingWork < 0)
            { return true; }
            if (visited.TryGetValue(current, out var open))
            { return open; }
            // An unexpected cycle or unclassified type must not prove that
            // boxing is absent. Completed closed nodes replace this sentinel.
            visited[current] = true;
            open = current switch
            {
                IArrayTypeSymbol array => ContainsOpenType(array.ElementType, depth + 1),
                INamedTypeSymbol named => named.IsUnboundGenericType ||
                    named.TypeArguments.Any(argument => ContainsOpenType(argument, depth + 1)) ||
                    named.ContainingType != null && ContainsOpenType(named.ContainingType, depth + 1),
                { TypeKind: TypeKind.Dynamic } => false,
                _ => true
            };
            visited[current] = open;
            return open;
        }
        // `isinst Nullable<X>` consumes the boxed value, so no JIT folds the
        // box: a Nullable T with a value allocates even in Release.
        return testedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ||
            ContainsOpenType(testedType, 0);
    }

    internal static IrTerm DefaultValue(IrFactory factory, IrTypeId type)
    {
        return factory.GetTypeInfo(type).Kind switch
        {
            IrTypeKind.Boolean => factory.Boolean(false),
            IrTypeKind.Integer => factory.Integer(type, 0),
            IrTypeKind.Reference or IrTypeKind.String or IrTypeKind.Sequence => factory.Null(type),
            _ => throw new ArgumentException("The type has no supported Total default value.", nameof(type))
        };
    }

    // Total IR models a string or single-dimensional array as a sequence; a
    // multidimensional array stays an opaque reference without one.
    internal static bool IsLength(IPropertyReferenceOperation property)
    {
        return IsReferenceDomain(property.Instance?.Type) && property.Property.MetadataName == "Length" &&
            property.Instance?.Type is not IArrayTypeSymbol { IsSZArray: false } &&
            property.Type?.SpecialType == SpecialType.System_Int32 && CompilerIdentityBridge.IsIntrinsicSequenceLength(property);
    }

    // The framework's static string.Equals(string, string): content equality.
    internal static bool IsStringEqualsCall(IMethodSymbol method)
    {
        return method.IsStatic && method.Name == "Equals" &&
            method.ReturnType.SpecialType == SpecialType.System_Boolean &&
            method.ContainingType.SpecialType == SpecialType.System_String &&
            method.Parameters.Length == 2 &&
            method.Parameters.All(static parameter => parameter.RefKind == RefKind.None &&
                parameter.Type.SpecialType == SpecialType.System_String);
    }

    // The framework's instance string.Equals(string): content equality, with a
    // NullReference fault on a null receiver once the argument is evaluated.
    internal static bool IsStringInstanceEqualsCall(IMethodSymbol method)
    {
        return !method.IsStatic && method.Name == "Equals" &&
            method.ReturnType.SpecialType == SpecialType.System_Boolean &&
            method.ContainingType.SpecialType == SpecialType.System_String &&
            method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.None &&
            method.Parameters[0].Type.SpecialType == SpecialType.System_String;
    }

    internal static TotalScalarRule StringInstanceEquals(IrFactory factory, IrTerm receiver, IrTerm argument)
    {
        return new(factory.Binary(IrBinaryOperator.StringEquals, receiver, argument),
            [new(IrExceptionKind.NullReference, factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type)))],
            FrontendSubsetClassification.Exact);
    }

    internal static (IOperation Left, IOperation Right) EqualityOperands(IBinaryOperation operation)
    {
        if (operation.OperatorMethod != null || operation.IsLifted ||
            operation.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        { return (operation.LeftOperand, operation.RightOperand); }
        IOperation Unwrap(IOperation operand)
        {
            return operand is IConversionOperation { IsImplicit: true, OperatorMethod: null } conversion &&
                conversion.Type?.SpecialType == SpecialType.System_Object && conversion.Conversion.IsReference &&
                (IsReferenceDomain(conversion.Operand.Type) || conversion.Operand.ConstantValue is { HasValue: true, Value: null })
                ? conversion.Operand : operand;
        }
        var left = Unwrap(operation.LeftOperand);
        var right = Unwrap(operation.RightOperand);
        var leftReference = IsReferenceDomain(left.Type);
        var rightReference = IsReferenceDomain(right.Type);
        var sameReferences = leftReference && rightReference && SymbolEqualityComparer.Default.Equals(left.Type, right.Type);
        var referenceAndNull = leftReference && right.ConstantValue is { HasValue: true, Value: null } ||
            rightReference && left.ConstantValue is { HasValue: true, Value: null };
        return sameReferences || referenceAndNull ? (left, right) : (operation.LeftOperand, operation.RightOperand);
    }

    // A target-typed `new(arguments)` converts the object it creates to the
    // type it creates: the conversion keeps the reference.
    private static bool IsTargetTypedCreation(IConversionOperation conversion)
    {
        return conversion.Operand is IObjectCreationOperation &&
            Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetConversion(conversion).IsObjectCreation &&
            SymbolEqualityComparer.Default.Equals(conversion.Type, conversion.Operand.Type);
    }

    private static TotalScalarRule? ReferenceRule(IrFactory factory, IOperation operation, ImmutableArray<IrTerm> operands)
    {
        if (operation is IArrayElementReferenceOperation access)
        { return ArrayReadRule(factory, access, operands); }
        if (operation is IPropertyReferenceOperation property && IsLength(property))
        {
            var receiver = operands[0];
            return new(factory.Length(receiver),
                [new(IrExceptionKind.NullReference, factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type)))],
                FrontendSubsetClassification.Exact);
        }
        if (operation is IConversionOperation conversion && IsReferenceDomain(operation.Type))
        {
            var target = new RoslynTypeMapper(factory).GetTypeId(operation.Type);
            if (conversion.OperatorMethod == null && (operands[0] is IrNullTerm ||
                    (conversion.Conversion.IsIdentity || IsTargetTypedCreation(conversion)) && operands[0].Type == target))
            {
                return Exact(operands[0] is IrNullTerm ? factory.Null(target) : operands[0]);
            }
            return Fail(factory, FrontendAbstention.UnsupportedOperationKind);
        }
        if (operation is IBinaryOperation binary && operands.Length == 2 &&
            factory.GetTypeInfo(operands[0].Type).Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String)
        {
            var left = operands[0];
            var right = operands[1];
            if (left is IrNullTerm && left.Type != right.Type)
            { left = factory.Null(right.Type); }
            if (right is IrNullTerm && left.Type != right.Type)
            { right = factory.Null(left.Type); }
            // `string == string` compares content; operands typed object
            // compare references.
            if (!binary.IsLifted && left is not IrNullTerm && right is not IrNullTerm &&
                binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals &&
                binary.LeftOperand.Type?.SpecialType == SpecialType.System_String &&
                binary.RightOperand.Type?.SpecialType == SpecialType.System_String &&
                factory.GetTypeInfo(left.Type).Kind == IrTypeKind.String && left.Type == right.Type)
            {
                var equal = factory.Binary(IrBinaryOperator.StringEquals, left, right);
                return Exact(binary.OperatorKind == BinaryOperatorKind.Equals ? equal : factory.Unary(IrUnaryOperator.Not, equal));
            }
            if (binary.IsLifted || left.Type != right.Type ||
                binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals) ||
                factory.GetTypeInfo(left.Type).Kind == IrTypeKind.String &&
                    left is not IrNullTerm && right is not IrNullTerm ||
                binary.OperatorMethod != null && binary.OperatorMethod.ContainingType.SpecialType != SpecialType.System_String)
            {
                return Fail(factory, FrontendAbstention.UnsupportedOperationKind);
            }
            if (binary.LeftOperand.Type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double ||
                binary.RightOperand.Type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double)
            { return Fail(factory, FrontendAbstention.UnsupportedType); }
            return Exact(factory.Binary(binary.OperatorKind == BinaryOperatorKind.Equals ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
                left, right));
        }
        return null;
    }
}
