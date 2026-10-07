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
            IrExceptionKind.Argument => "System.ArgumentException",
            // Its hierarchy is just Exception and Object, so only those catch it.
            IrExceptionKind.Unknown or IrExceptionKind.Explicit => "System.Exception",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    // Resolve the runtime assembly that owns System.Object. A source-defined
    // same-name exception must never impersonate a local scalar fault.
    internal static bool TryCatchKinds(Compilation compilation, ITypeSymbol? catchType,
        out ImmutableArray<IrExceptionKind> kinds)
    {
        var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        // Roslyn represents a bare catch with the canonical System.Object.
        if (catchType != null && catchType.SpecialType != SpecialType.System_Object &&
            !DerivesFrom(catchType, core.GetTypeByMetadataName("System.Exception")))
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

    internal static bool DerivesFrom(ITypeSymbol? type, ITypeSymbol? ancestor)
    {
        for (var current = type; current != null && ancestor != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, ancestor))
            { return true; }
        }
        return false;
    }

    // The thrown expression below any implicit conversion to System.Exception,
    // which keeps the reference.
    internal static IOperation ThrownOperand(IOperation thrown)
    {
        while (thrown is IConversionOperation { IsImplicit: true, OperatorMethod: null, Conversion.IsReference: true } conversion)
        { thrown = conversion.Operand; }
        return thrown;
    }

    internal static INamedTypeSymbol? ThrownType(Compilation compilation, IOperation operand)
    {
        return operand.Type is INamedTypeSymbol { TypeKind: TypeKind.Class } type &&
            DerivesFrom(type, compilation.GetTypeByMetadataName("System.Exception")) ? type : null;
    }

    // The allocation-only model admits core exception constructors with string
    // and Exception parameters only when their supported input checks cannot fail.
    internal static bool IsCoreExceptionCreation(IObjectCreationOperation creation)
    {
        var exception = CoreException(creation.Type);
        return creation is { Initializer: null, Constructor: { } constructor } && exception != null &&
            SymbolEqualityComparer.Default.Equals(constructor.ContainingAssembly, exception.ContainingAssembly) &&
            constructor.Parameters.All(parameter => parameter.RefKind == RefKind.None &&
                (parameter.Type.SpecialType == SpecialType.System_String || SymbolEqualityComparer.Default.Equals(parameter.Type, exception))) &&
            creation.Arguments.All(argument => argument.ArgumentKind is ArgumentKind.Explicit or ArgumentKind.DefaultValue) &&
            !RejectsPossiblyNullInnerException(creation, exception);
    }

    private static bool RejectsPossiblyNullInnerException(IObjectCreationOperation creation, INamedTypeSymbol exception)
    {
        if (!SymbolEqualityComparer.Default.Equals(creation.Type,
                exception.ContainingAssembly.GetTypeByMetadataName("System.AggregateException")))
        { return false; }
        // AggregateException(string, Exception) throws ArgumentNullException
        // for a null innerException. Only a new object is known nonnull here;
        // other values require a precise constructor model before admission.
        return creation.Arguments.Any(argument =>
            SymbolEqualityComparer.Default.Equals(argument.Parameter?.Type, exception) &&
            ThrownOperand(argument.Value) is not IObjectCreationOperation);
    }

    // The core library's System.Exception among type's base classes, found
    // through System.Object's assembly.
    private static INamedTypeSymbol? CoreException(ITypeSymbol? type)
    {
        INamedTypeSymbol? exception = null;
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current.SpecialType == SpecialType.System_Object)
            {
                return exception != null && SymbolEqualityComparer.Default.Equals(exception.ContainingAssembly, current.ContainingAssembly)
                    ? exception : null;
            }
            if (current is { Name: "Exception", Arity: 0, ContainingType: null, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } })
            { exception = current; }
        }
        return null;
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
