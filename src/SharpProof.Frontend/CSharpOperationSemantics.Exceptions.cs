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
            IrExceptionKind.ArgumentOutOfRange => "System.ArgumentOutOfRangeException",
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
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
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
        thrown = RepositoryParameterGuard.NotNull(thrown, nameof(thrown));
        while (thrown is IConversionOperation { IsImplicit: true, OperatorMethod: null, Conversion.IsReference: true } conversion)
        { thrown = conversion.Operand; }
        return thrown;
    }

    internal static INamedTypeSymbol? ThrownType(Compilation compilation, IOperation operand)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        operand = RepositoryParameterGuard.NotNull(operand, nameof(operand));
        return operand.Type is INamedTypeSymbol { TypeKind: TypeKind.Class } type &&
            DerivesFrom(type, compilation.GetTypeByMetadataName("System.Exception")) ? type : null;
    }

    // Admission is separate from resource-message effects. A framework resource
    // lookup can call user culture overrides, even during exception construction.
    internal static bool IsCoreExceptionCreation(IObjectCreationOperation creation)
    {
        creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
        var exception = CoreException(creation.Type);
        return creation is { Initializer: null, Constructor: { } constructor } && exception != null &&
            (SymbolEqualityComparer.Default.Equals(constructor.ContainingAssembly, exception.ContainingAssembly) ||
                IsAuditedMessageExceptionDeclaration(constructor)) &&
            constructor.Parameters.All(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.RefKind == RefKind.None &&
                (parameter.Type.SpecialType == SpecialType.System_String || SymbolEqualityComparer.Default.Equals(parameter.Type, exception)))) &&
            creation.Arguments.All(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (argument.ArgumentKind is ArgumentKind.Explicit or ArgumentKind.DefaultValue)) &&
            !RejectsPossiblyNullInnerException(creation, exception);
    }

    internal static bool IsAuditedMessageExceptionDeclaration(ISymbol symbol)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        return symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor &&
            IsFrameworkCollectionMember(constructor) && constructor.OriginalDefinition.GetDocumentationCommentId() is
                "M:System.Collections.Generic.KeyNotFoundException.#ctor(System.String)" or
                "M:System.ArgumentException.#ctor(System.String)" or "M:System.ArgumentException.#ctor(System.String,System.String)";
    }

    // -1 is callback-free for the audited inputs; a nonnegative ordinal requires
    // a nonnull message. Unlisted signatures retain full runtime effects.
    // The pinned .NET 10.0.13 profiles are checked against actual CLR culture
    // and inner-exception callbacks by ExceptionConstructorProfileOracleTests.
    internal static int? ExceptionConstructorMessageGuard(IMethodSymbol constructor)
    {
        constructor = RepositoryParameterGuard.NotNull(constructor, nameof(constructor));
        if (constructor.MethodKind != MethodKind.Constructor || !IsFrameworkCollectionMember(constructor))
        { return null; }
        var id = constructor.OriginalDefinition.GetDocumentationCommentId();
        if (id != null && CallbackFreeExceptionConstructors.Contains(id))
        { return -1; }
        return id != null && NullMessageExceptionConstructors.TryGetValue(id, out var ordinal) ? ordinal : null;
    }

    internal static string? ExceptionInitializationKind(IMethodSymbol constructor)
    {
        constructor = RepositoryParameterGuard.NotNull(constructor, nameof(constructor));
        if (constructor.MethodKind != MethodKind.Constructor || !IsFrameworkCollectionMember(constructor))
        { return null; }
        return constructor.OriginalDefinition.GetDocumentationCommentId() switch
        {
            "M:System.ArgumentException.#ctor" => "Argument",
            "M:System.ArgumentNullException.#ctor" => "ArgumentNull",
            "M:System.ArgumentNullException.#ctor(System.String)" => "ArgumentNullName",
            "M:System.ArgumentOutOfRangeException.#ctor" => "ArgumentRange",
            "M:System.ArgumentOutOfRangeException.#ctor(System.String)" => "ArgumentRangeName",
            "M:System.InvalidOperationException.#ctor" => "InvalidOperation",
            "M:System.InvalidOperationException.#ctor(System.String)" => "InvalidOperationNull",
            "M:System.DivideByZeroException.#ctor" => "DivideByZero",
            "M:System.IO.IOException.#ctor" => "IOException",
            "M:System.ObjectDisposedException.#ctor(System.String)" => "ObjectDisposedName",
            "M:System.AggregateException.#ctor" => "Aggregate",
            "M:System.AggregateException.#ctor(System.String)" => "AggregateNull",
            "M:System.Collections.Generic.KeyNotFoundException.#ctor(System.String)" => "KeyNotFoundNull",
            _ => null
        };
    }

    private static readonly HashSet<string> CallbackFreeExceptionConstructors = new(StringComparer.Ordinal)
    {
        "M:System.ApplicationException.#ctor(System.String,System.Exception)",
        "M:System.ApplicationException.#ctor(System.String)",
        "M:System.ArgumentException.#ctor(System.String,System.Exception)",
        "M:System.ArgumentException.#ctor(System.String,System.String,System.Exception)",
        "M:System.ArgumentException.#ctor(System.String,System.String)",
        "M:System.ArgumentException.#ctor(System.String)",
        "M:System.BadImageFormatException.#ctor(System.String,System.Exception)",
        "M:System.BadImageFormatException.#ctor(System.String,System.String,System.Exception)",
        "M:System.BadImageFormatException.#ctor(System.String,System.String)",
        "M:System.BadImageFormatException.#ctor(System.String)",
        "M:System.ComponentModel.Win32Exception.#ctor",
        "M:System.ContextMarshalException.#ctor(System.String,System.Exception)",
        "M:System.ContextMarshalException.#ctor(System.String)",
        "M:System.Exception.#ctor",
        "M:System.Exception.#ctor(System.String,System.Exception)",
        "M:System.Exception.#ctor(System.String)",
        "M:System.InsufficientMemoryException.#ctor",
        "M:System.InsufficientMemoryException.#ctor(System.String,System.Exception)",
        "M:System.InsufficientMemoryException.#ctor(System.String)",
        "M:System.InvalidTimeZoneException.#ctor",
        "M:System.InvalidTimeZoneException.#ctor(System.String,System.Exception)",
        "M:System.InvalidTimeZoneException.#ctor(System.String)",
        "M:System.IO.FileLoadException.#ctor(System.String,System.Exception)",
        "M:System.IO.FileLoadException.#ctor(System.String,System.String,System.Exception)",
        "M:System.IO.FileLoadException.#ctor(System.String,System.String)",
        "M:System.IO.FileLoadException.#ctor(System.String)",
        "M:System.IO.FileNotFoundException.#ctor(System.String,System.Exception)",
        "M:System.IO.FileNotFoundException.#ctor(System.String,System.String,System.Exception)",
        "M:System.IO.FileNotFoundException.#ctor(System.String,System.String)",
        "M:System.IO.FileNotFoundException.#ctor(System.String)",
        "M:System.IO.IOException.#ctor(System.String,System.Exception)",
        "M:System.IO.IOException.#ctor(System.String)",
        "M:System.OutOfMemoryException.#ctor",
        "M:System.OutOfMemoryException.#ctor(System.String,System.Exception)",
        "M:System.OutOfMemoryException.#ctor(System.String)",
        "M:System.Reflection.TargetException.#ctor",
        "M:System.Reflection.TargetException.#ctor(System.String,System.Exception)",
        "M:System.Reflection.TargetException.#ctor(System.String)",
        "M:System.SystemException.#ctor(System.String,System.Exception)",
        "M:System.SystemException.#ctor(System.String)",
        "M:System.Text.DecoderFallbackException.#ctor(System.String,System.Exception)",
        "M:System.Text.DecoderFallbackException.#ctor(System.String)",
        "M:System.Text.EncoderFallbackException.#ctor(System.String,System.Exception)",
        "M:System.Text.EncoderFallbackException.#ctor(System.String)",
        "M:System.Threading.LockRecursionException.#ctor",
        "M:System.Threading.LockRecursionException.#ctor(System.String,System.Exception)",
        "M:System.Threading.LockRecursionException.#ctor(System.String)",
        "M:System.Threading.ThreadInterruptedException.#ctor",
        "M:System.Threading.ThreadInterruptedException.#ctor(System.String,System.Exception)",
        "M:System.Threading.ThreadInterruptedException.#ctor(System.String)",
        "M:System.TimeZoneNotFoundException.#ctor",
        "M:System.TimeZoneNotFoundException.#ctor(System.String,System.Exception)",
        "M:System.TimeZoneNotFoundException.#ctor(System.String)",
        "M:System.TypeLoadException.#ctor(System.String,System.Exception)",
        "M:System.TypeLoadException.#ctor(System.String)",
    };

    private static readonly Dictionary<string, int> NullMessageExceptionConstructors = new(StringComparer.Ordinal)
    {
        ["M:System.AccessViolationException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.AccessViolationException.#ctor(System.String)"] = 0,
        ["M:System.AggregateException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.AggregateException.#ctor(System.String)"] = 0,
        ["M:System.AppDomainUnloadedException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.AppDomainUnloadedException.#ctor(System.String)"] = 0,
        ["M:System.ArgumentNullException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.ArgumentNullException.#ctor(System.String,System.String)"] = 1,
        ["M:System.ArgumentOutOfRangeException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.ArgumentOutOfRangeException.#ctor(System.String,System.String)"] = 1,
        ["M:System.ArithmeticException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.ArithmeticException.#ctor(System.String)"] = 0,
        ["M:System.ArrayTypeMismatchException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.ArrayTypeMismatchException.#ctor(System.String)"] = 0,
        ["M:System.CannotUnloadAppDomainException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.CannotUnloadAppDomainException.#ctor(System.String)"] = 0,
        ["M:System.Collections.Generic.KeyNotFoundException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Collections.Generic.KeyNotFoundException.#ctor(System.String)"] = 0,
        ["M:System.ComponentModel.Win32Exception.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.ComponentModel.Win32Exception.#ctor(System.String)"] = 0,
        ["M:System.DataMisalignedException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.DataMisalignedException.#ctor(System.String)"] = 0,
        ["M:System.Diagnostics.Tracing.EventSourceException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Diagnostics.Tracing.EventSourceException.#ctor(System.String)"] = 0,
        ["M:System.Diagnostics.UnreachableException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Diagnostics.UnreachableException.#ctor(System.String)"] = 0,
        ["M:System.DivideByZeroException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.DivideByZeroException.#ctor(System.String)"] = 0,
        ["M:System.DllNotFoundException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.DllNotFoundException.#ctor(System.String)"] = 0,
        ["M:System.DuplicateWaitObjectException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.DuplicateWaitObjectException.#ctor(System.String,System.String)"] = 1,
        ["M:System.EntryPointNotFoundException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.EntryPointNotFoundException.#ctor(System.String)"] = 0,
        ["M:System.ExecutionEngineException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.ExecutionEngineException.#ctor(System.String)"] = 0,
        ["M:System.FieldAccessException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.FieldAccessException.#ctor(System.String)"] = 0,
        ["M:System.FormatException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.FormatException.#ctor(System.String)"] = 0,
        ["M:System.Globalization.CultureNotFoundException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Globalization.CultureNotFoundException.#ctor(System.String,System.String,System.Exception)"] = 0,
        ["M:System.Globalization.CultureNotFoundException.#ctor(System.String,System.String,System.String)"] = 2,
        ["M:System.Globalization.CultureNotFoundException.#ctor(System.String,System.String)"] = 1,
        ["M:System.Globalization.CultureNotFoundException.#ctor(System.String)"] = 0,
        ["M:System.IndexOutOfRangeException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.IndexOutOfRangeException.#ctor(System.String)"] = 0,
        ["M:System.InsufficientExecutionStackException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.InsufficientExecutionStackException.#ctor(System.String)"] = 0,
        ["M:System.InvalidCastException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.InvalidCastException.#ctor(System.String)"] = 0,
        ["M:System.InvalidOperationException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.InvalidOperationException.#ctor(System.String)"] = 0,
        ["M:System.InvalidProgramException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.InvalidProgramException.#ctor(System.String)"] = 0,
        ["M:System.IO.DirectoryNotFoundException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.IO.DirectoryNotFoundException.#ctor(System.String)"] = 0,
        ["M:System.IO.EndOfStreamException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.IO.EndOfStreamException.#ctor(System.String)"] = 0,
        ["M:System.IO.InvalidDataException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.IO.InvalidDataException.#ctor(System.String)"] = 0,
        ["M:System.IO.PathTooLongException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.IO.PathTooLongException.#ctor(System.String)"] = 0,
        ["M:System.MemberAccessException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.MemberAccessException.#ctor(System.String)"] = 0,
        ["M:System.MethodAccessException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.MethodAccessException.#ctor(System.String)"] = 0,
        ["M:System.MissingFieldException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.MissingFieldException.#ctor(System.String)"] = 0,
        ["M:System.MissingMemberException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.MissingMemberException.#ctor(System.String)"] = 0,
        ["M:System.MissingMethodException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.MissingMethodException.#ctor(System.String)"] = 0,
        ["M:System.MulticastNotSupportedException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.MulticastNotSupportedException.#ctor(System.String)"] = 0,
        ["M:System.NotFiniteNumberException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.NotFiniteNumberException.#ctor(System.String)"] = 0,
        ["M:System.NotImplementedException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.NotImplementedException.#ctor(System.String)"] = 0,
        ["M:System.NotSupportedException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.NotSupportedException.#ctor(System.String)"] = 0,
        ["M:System.NullReferenceException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.NullReferenceException.#ctor(System.String)"] = 0,
        ["M:System.ObjectDisposedException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.ObjectDisposedException.#ctor(System.String,System.String)"] = 1,
        ["M:System.OperationCanceledException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.OperationCanceledException.#ctor(System.String)"] = 0,
        ["M:System.OverflowException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.OverflowException.#ctor(System.String)"] = 0,
        ["M:System.PlatformNotSupportedException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.PlatformNotSupportedException.#ctor(System.String)"] = 0,
        ["M:System.RankException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.RankException.#ctor(System.String)"] = 0,
        ["M:System.Reflection.AmbiguousMatchException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Reflection.AmbiguousMatchException.#ctor(System.String)"] = 0,
        ["M:System.Reflection.CustomAttributeFormatException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Reflection.CustomAttributeFormatException.#ctor(System.String)"] = 0,
        ["M:System.Reflection.InvalidFilterCriteriaException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Reflection.InvalidFilterCriteriaException.#ctor(System.String)"] = 0,
        ["M:System.Reflection.TargetInvocationException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Reflection.TargetParameterCountException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Reflection.TargetParameterCountException.#ctor(System.String)"] = 0,
        ["M:System.Resources.MissingManifestResourceException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Resources.MissingManifestResourceException.#ctor(System.String)"] = 0,
        ["M:System.Resources.MissingSatelliteAssemblyException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Resources.MissingSatelliteAssemblyException.#ctor(System.String,System.String)"] = 0,
        ["M:System.Resources.MissingSatelliteAssemblyException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.AmbiguousImplementationException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.AmbiguousImplementationException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.CompilerServices.SwitchExpressionException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.CompilerServices.SwitchExpressionException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.InteropServices.COMException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.InteropServices.COMException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.InteropServices.ExternalException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.InteropServices.ExternalException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.InteropServices.InvalidComObjectException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.InteropServices.InvalidComObjectException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.InteropServices.InvalidOleVariantTypeException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.InteropServices.InvalidOleVariantTypeException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.InteropServices.MarshalDirectiveException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.InteropServices.MarshalDirectiveException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.InteropServices.SafeArrayRankMismatchException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.InteropServices.SafeArrayRankMismatchException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.InteropServices.SafeArrayTypeMismatchException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.InteropServices.SafeArrayTypeMismatchException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.InteropServices.SEHException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.InteropServices.SEHException.#ctor(System.String)"] = 0,
        ["M:System.Runtime.Serialization.SerializationException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Runtime.Serialization.SerializationException.#ctor(System.String)"] = 0,
        ["M:System.Security.Cryptography.CryptographicException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Security.Cryptography.CryptographicException.#ctor(System.String)"] = 0,
        ["M:System.Security.SecurityException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Security.SecurityException.#ctor(System.String)"] = 0,
        ["M:System.Security.VerificationException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Security.VerificationException.#ctor(System.String)"] = 0,
        ["M:System.StackOverflowException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.StackOverflowException.#ctor(System.String)"] = 0,
        ["M:System.Threading.AbandonedMutexException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Threading.AbandonedMutexException.#ctor(System.String)"] = 0,
        ["M:System.Threading.SemaphoreFullException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Threading.SemaphoreFullException.#ctor(System.String)"] = 0,
        ["M:System.Threading.SynchronizationLockException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Threading.SynchronizationLockException.#ctor(System.String)"] = 0,
        ["M:System.Threading.Tasks.TaskCanceledException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Threading.Tasks.TaskCanceledException.#ctor(System.String)"] = 0,
        ["M:System.Threading.Tasks.TaskSchedulerException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Threading.Tasks.TaskSchedulerException.#ctor(System.String)"] = 0,
        ["M:System.Threading.ThreadStateException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Threading.ThreadStateException.#ctor(System.String)"] = 0,
        ["M:System.Threading.WaitHandleCannotBeOpenedException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.Threading.WaitHandleCannotBeOpenedException.#ctor(System.String)"] = 0,
        ["M:System.TimeoutException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.TimeoutException.#ctor(System.String)"] = 0,
        ["M:System.TypeAccessException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.TypeAccessException.#ctor(System.String)"] = 0,
        ["M:System.TypeUnloadedException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.TypeUnloadedException.#ctor(System.String)"] = 0,
        ["M:System.UnauthorizedAccessException.#ctor(System.String,System.Exception)"] = 0,
        ["M:System.UnauthorizedAccessException.#ctor(System.String)"] = 0,
    };

    private static bool RejectsPossiblyNullInnerException(IObjectCreationOperation creation, INamedTypeSymbol exception)
    {
        if (!SymbolEqualityComparer.Default.Equals(creation.Type,
                exception.ContainingAssembly.GetTypeByMetadataName("System.AggregateException")))
        { return false; }
        // AggregateException(string, Exception) throws ArgumentNullException
        // for a null innerException. Only a new object is known nonnull here;
        // other values require a precise constructor model before admission.
        return creation.Arguments.Any(argument =>
            argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (SymbolEqualityComparer.Default.Equals(argument.Parameter?.Type, exception) &&
            ThrownOperand(argument.Value) is not IObjectCreationOperation));
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
