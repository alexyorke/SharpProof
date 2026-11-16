using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using SharpProof.Ir;

namespace SharpProof.Frontend;

// Runtime types the frontend allocates and the types it tests them against,
// per factory, with the answer to each pair registered on the factory. A
// pair whose answer the CLR and C# could disagree on is left unregistered,
// which the encoder and replay treat as unknown.
internal static class RuntimeTypeRelations
{
    internal enum TestKind
    {
        // An object or box converts to the type, as isinst decides.
        Reference = 0,
        // A box holds exactly this value type.
        Box = 1,
        // An unboxing to this value type succeeds.
        Unbox = 2
    }

    private sealed class State
    {
        internal Compilation? Compilation { get; set; }
        internal Dictionary<IrTypeId, (ITypeSymbol Symbol, bool Box)> Runtimes { get; } = [];
        internal Dictionary<IrTypeId, (ITypeSymbol Symbol, TestKind Kind)> Tested { get; } = [];
    }

    private static readonly ConditionalWeakTable<IrFactory, State> States = new();

    // The reference type of a box of a value type, and the tested type of an
    // unboxing to it. Neither is a C# type; each is distinct per value type.
    internal static IrTypeId BoxType(IrFactory factory, ITypeSymbol valueType, TestKind kind = TestKind.Box)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        valueType = RepositoryParameterGuard.NotNull(valueType, nameof(valueType));
        var display = CompilerIdentityBridge.CreateTypeDisplay(valueType);
        var prefix = kind == TestKind.Unbox ? "unbox:" : "box:";
        var identity = factory.InternExternalIdentity((prefix, display), EqualityComparer<(string, string)>.Default);
        return factory.GetOrCreateReferenceType(identity, prefix + display);
    }

    // The field a box keeps its value in, by the value's IR type, so that an
    // enum box and its underlying type read the same slot.
    internal static IrMemberId BoxValueField(IrFactory factory, IrTypeId value)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.ThrowIfInvalid(value.IsDefault, nameof(value));
        var identity = factory.InternExternalIdentity(("box-value", value), EqualityComparer<(string, IrTypeId)>.Default);
        return factory.GetOrCreateMember(identity, factory.ObjectType,
            IrFieldSites.Prefix + "box:" + factory.GetString(factory.GetTypeInfo(value).Name), value, false);
    }

    // An object of exactly this type: a class, delegate or array type, or a
    // box of a value type.
    internal static void RegisterRuntime(IrFactory factory, Compilation? compilation, IrTypeId runtime, ITypeSymbol symbol, bool box)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        var state = States.GetOrCreateValue(factory);
        lock (state)
        {
            state.Compilation ??= compilation;
            factory.RegisterRuntimeType(runtime);
            if (state.Runtimes.ContainsKey(runtime))
            { return; }
            state.Runtimes.Add(runtime, (symbol, box));
            foreach (var pair in state.Tested)
            { Register(factory, state, runtime, (symbol, box), pair.Key, pair.Value); }
        }
    }

    internal static void RegisterTested(IrFactory factory, Compilation? compilation, IrTypeId tested, ITypeSymbol symbol, TestKind kind)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        var state = States.GetOrCreateValue(factory);
        lock (state)
        {
            state.Compilation ??= compilation;
            if (state.Tested.ContainsKey(tested))
            { return; }
            state.Tested.Add(tested, (symbol, kind));
            foreach (var pair in state.Runtimes)
            { Register(factory, state, pair.Key, pair.Value, tested, (symbol, kind)); }
        }
    }

    private static void Register(IrFactory factory, State state, IrTypeId runtime, (ITypeSymbol Symbol, bool Box) source,
        IrTypeId tested, (ITypeSymbol Symbol, TestKind Kind) target)
    {
        if (Converts(state.Compilation, source.Symbol, source.Box, target.Symbol, target.Kind) is { } converts)
        { factory.RegisterRuntimeConversion(runtime, tested, converts); }
    }

    internal static bool? Converts(Compilation? compilation, ITypeSymbol runtime, bool box, ITypeSymbol target, TestKind kind)
    {
        runtime = RepositoryParameterGuard.NotNull(runtime, nameof(runtime));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        if (IsOpen(runtime) || IsOpen(target))
        { return null; }
        switch (kind)
        {
            case TestKind.Box:
                return box ? Same(runtime, target) : false;
            case TestKind.Unbox:
                if (!box)
                { return false; }
                if (Same(runtime, target) is { } same && same)
                { return true; }
                // The CLR unboxes an enum and its underlying integer, and two
                // enums over one integer, into each other.
                if ((CSharpOperationSemantics.IsEnum(runtime) || CSharpOperationSemantics.IsEnum(target)) &&
                    CSharpOperationSemantics.IsScalar(runtime) && CSharpOperationSemantics.IsScalar(target))
                { return CSharpOperationSemantics.ScalarSpecialType(runtime) == CSharpOperationSemantics.ScalarSpecialType(target); }
                // Primitives of one width (int and uint, char and ushort) may
                // unbox into each other; leave them unknown. Other widths never do.
                if (CSharpOperationSemantics.IsScalar(runtime) && CSharpOperationSemantics.IsScalar(target))
                { return Width(runtime) == Width(target) ? null : false; }
                return Same(runtime, target);
            default:
                if (target.SpecialType == SpecialType.System_Object)
                { return true; }
                if (target is ITypeParameterSymbol || target.TypeKind == TypeKind.Dynamic ||
                    compilation is not Microsoft.CodeAnalysis.CSharp.CSharpCompilation csharp)
                { return null; }
                // Only an array is an array.
                if (target is IArrayTypeSymbol && runtime is not IArrayTypeSymbol)
                { return false; }
                if (runtime is IArrayTypeSymbol runtimeArray)
                {
                    if (target is IArrayTypeSymbol targetArray)
                    { return ArrayConverts(csharp, runtimeArray, targetArray); }
                    // CLR arrays implement generic interfaces of related element
                    // types more loosely than C# says.
                    if (target is INamedTypeSymbol { IsGenericType: true, TypeKind: TypeKind.Interface })
                    { return null; }
                }
                var conversion = csharp.ClassifyConversion(runtime, target);
                return conversion.Exists && (conversion.IsIdentity || conversion.IsImplicit && (conversion.IsReference || conversion.IsBoxing));
        }
    }

    // CLR array covariance also relates arrays of same-size primitives and
    // enums, which C# does not; those pairs stay unknown.
    private static bool? ArrayConverts(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation, IArrayTypeSymbol runtime, IArrayTypeSymbol target)
    {
        if (runtime.Rank != target.Rank || runtime.IsSZArray != target.IsSZArray)
        { return false; }
        if (Same(runtime.ElementType, target.ElementType) is { } same && same)
        { return true; }
        if (runtime.ElementType.IsValueType || target.ElementType.IsValueType)
        {
            if (CSharpOperationSemantics.IsScalar(runtime.ElementType) && CSharpOperationSemantics.IsScalar(target.ElementType))
            { return Width(runtime.ElementType) == Width(target.ElementType) ? null : false; }
            return runtime.ElementType.IsValueType && target.ElementType.IsValueType ? null : false;
        }
        var conversion = compilation.ClassifyConversion(runtime.ElementType, target.ElementType);
        return conversion.Exists && (conversion.IsIdentity || conversion.IsImplicit && conversion.IsReference);
    }

    private static int Width(ITypeSymbol type)
    {
        return CSharpOperationSemantics.ScalarSpecialType(type) switch
        {
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => 1,
            SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
            SpecialType.System_Int32 or SpecialType.System_UInt32 => 4,
            _ => 8
        };
    }

    private static bool? Same(ITypeSymbol left, ITypeSymbol right)
    {
        if (SymbolEqualityComparer.Default.Equals(left, right))
        { return true; }
        // SameRuntimeType also answers true when its comparison was cut short.
        return CompilerIdentityBridge.SameRuntimeType(left, right) ? null : false;
    }

    private static bool IsOpen(ITypeSymbol type)
    {
        return type.TypeKind is TypeKind.Error or TypeKind.TypeParameter or TypeKind.Pointer or TypeKind.FunctionPointer ||
            type is INamedTypeSymbol { IsUnboundGenericType: true } ||
            type is INamedTypeSymbol named && named.TypeArguments.Any(IsOpen) ||
            type is IArrayTypeSymbol array && IsOpen(array.ElementType);
    }
}
