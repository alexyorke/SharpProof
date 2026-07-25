using SharpProof.Attributes;

namespace SharpProof.Symbolic;

internal readonly record struct MethodPattern(
    string ContainingType,
    MethodKind Kind,
    string Name,
    bool IsStatic = true) {
    internal bool Matches(IMethodSymbol method) =>
        method.MethodKind == Kind &&
        method.IsStatic == IsStatic &&
        string.Equals(method.Name, Name, StringComparison.Ordinal) &&
        string.Equals(method.ContainingType?.ToDisplayString(), ContainingType, StringComparison.Ordinal);
}

internal readonly record struct BoundCall(IMethodSymbol Method, int FormalOffset) {
    internal static BoundCall Create(IMethodSymbol method) {
        var reduced = method.ReducedFrom;
        var normalized = ((reduced ?? method).PartialImplementationPart ?? reduced ?? method).OriginalDefinition;
        return new(normalized, reduced == null ? 0 : 1);
    }

    internal IReadOnlyList<T> BindFormal<T>(T receiver, IReadOnlyList<T> arguments) =>
        FormalOffset == 0 ? arguments : [receiver, .. arguments];
}

internal sealed record KnownEffectModel(
    MethodEffects Summary,
    ImmutableArray<int> WrittenArgumentOrdinals,
    ImmutableArray<int> ReadArgumentOrdinals);

internal static class KnownEffectCatalog {
    private static readonly ImmutableDictionary<string, MethodPattern> BitConverterPatterns =
        new[] {
            "ToInt16", "ToInt32", "ToInt64", "ToUInt16", "ToUInt32", "ToUInt64",
            "ToBoolean", "ToChar", "ToSingle", "ToDouble", "ToHalf", "ToString",
            "DoubleToInt64Bits", "DoubleToUInt64Bits", "Int64BitsToDouble", "UInt64BitsToDouble",
            "SingleToInt32Bits", "SingleToUInt32Bits", "Int32BitsToSingle", "UInt32BitsToSingle",
            "GetBytes", "TryWriteBytes"
        }.ToImmutableDictionary(
            static name => name,
            static name => new MethodPattern("System.BitConverter", MethodKind.Ordinary, name),
            StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, SpecialType> ConversionTypes =
        new Dictionary<string, SpecialType>(StringComparer.Ordinal) {
            ["ToInt16"] = SpecialType.System_Int16,
            ["ToInt32"] = SpecialType.System_Int32,
            ["ToInt64"] = SpecialType.System_Int64,
            ["ToUInt16"] = SpecialType.System_UInt16,
            ["ToUInt32"] = SpecialType.System_UInt32,
            ["ToUInt64"] = SpecialType.System_UInt64,
            ["ToBoolean"] = SpecialType.System_Boolean,
            ["ToChar"] = SpecialType.System_Char,
            ["ToSingle"] = SpecialType.System_Single,
            ["ToDouble"] = SpecialType.System_Double
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, (SpecialType Parameter, SpecialType Result)> BitCasts =
        new Dictionary<string, (SpecialType, SpecialType)>(StringComparer.Ordinal) {
            ["DoubleToInt64Bits"] = (SpecialType.System_Double, SpecialType.System_Int64),
            ["DoubleToUInt64Bits"] = (SpecialType.System_Double, SpecialType.System_UInt64),
            ["Int64BitsToDouble"] = (SpecialType.System_Int64, SpecialType.System_Double),
            ["UInt64BitsToDouble"] = (SpecialType.System_UInt64, SpecialType.System_Double),
            ["SingleToInt32Bits"] = (SpecialType.System_Single, SpecialType.System_Int32),
            ["SingleToUInt32Bits"] = (SpecialType.System_Single, SpecialType.System_UInt32),
            ["Int32BitsToSingle"] = (SpecialType.System_Int32, SpecialType.System_Single),
            ["UInt32BitsToSingle"] = (SpecialType.System_UInt32, SpecialType.System_Single)
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableHashSet<SpecialType> ScalarTypes = [
        SpecialType.System_Int16,
        SpecialType.System_Int32,
        SpecialType.System_Int64,
        SpecialType.System_UInt16,
        SpecialType.System_UInt32,
        SpecialType.System_UInt64,
        SpecialType.System_Boolean,
        SpecialType.System_Char,
        SpecialType.System_Single,
        SpecialType.System_Double
    ];
    private static readonly ImmutableHashSet<string> UnaryMathMethods = [
        "BitIncrement", "BitDecrement", "Log2", "Cbrt", "Sin", "Cos", "Tan", "Asin", "Acos", "Atan"
    ];

    internal static bool TryGetEffect(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (BitConverterPatterns.TryGetValue(method.Name, out var pattern) && pattern.Matches(method))
            return method.Name switch {
                "ToString" => TryCreateToString(method, out model),
                "GetBytes" => TryCreateGetBytes(method, out model),
                "TryWriteBytes" => TryCreateTryWriteBytes(method, out model),
                "ToHalf" => TryCreateConversion(method, SpecialType.None, "half", out model),
                _ when ConversionTypes.TryGetValue(method.Name, out var result) =>
                    TryCreateConversion(method, result, method.Name.Substring(2).ToLowerInvariant(), out model),
                _ when BitCasts.TryGetValue(method.Name, out var cast) &&
                       IsSignature(method, cast.Result, cast.Parameter) =>
                    Create(SharpProofEffect.None, out model),
                _ => false
            };
        return TryCreateMath(method, out model) ||
               TryCreateArray(method, out model) ||
               TryCreateBuffer(method, out model) ||
               TryCreateFramework(method, out model);
    }

    private static bool TryCreateMath(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        var scalar = method.ContainingType?.ToDisplayString() switch {
            "System.Math" => SpecialType.System_Double,
            "System.MathF" => SpecialType.System_Single,
            _ => SpecialType.None
        };
        if (scalar == SpecialType.None ||
            method is not { MethodKind: MethodKind.Ordinary, IsStatic: true })
            return false;
        var parametersMatch = method.Name switch {
            _ when UnaryMathMethods.Contains(method.Name) =>
                IsSignature(method, scalar, scalar),
            "CopySign" or "Atan2" =>
                HasParameters(method, scalar, scalar, scalar),
            "ScaleB" =>
                HasParameters(method, scalar, scalar, SpecialType.System_Int32),
            "FusedMultiplyAdd" =>
                HasParameters(method, scalar, scalar, scalar, scalar),
            "ILogB" =>
                IsSignature(method, SpecialType.System_Int32, scalar),
            _ => false
        };
        return parametersMatch && Create(SharpProofEffect.None, out model);
    }

    private static bool TryCreateArray(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method.ContainingType?.SpecialType != SpecialType.System_Array) return false;
        return method.Name switch {
            "Copy" => TryCreateArrayCopy(method, constrained: false, out model),
            "ConstrainedCopy" => TryCreateArrayCopy(method, constrained: true, out model),
            "Clear" when IsVoidSignature(method, true,
                SpecialType.System_Array, SpecialType.System_Int32, SpecialType.System_Int32) =>
                Create(SharpProofEffect.WritesArgumentState | SharpProofEffect.Throws, out model,
                    Facts("framework_array_clear_model",
                        "System.ArgumentNullException", "System.IndexOutOfRangeException"), write: [0]),
            "Fill" => TryCreateArrayFill(method, out model),
            "Resize" => TryCreateArrayResize(method, out model),
            "Reverse" => TryCreateArrayReverse(method, out model),
            "Clone" when method is {
                MethodKind: MethodKind.Ordinary, IsStatic: false, Parameters.Length: 0,
                ReturnType.SpecialType: SpecialType.System_Object
            } => Create(SharpProofEffect.ReadsReceiverState | SharpProofEffect.Allocates, out model),
            "GetLength" when IsInstanceSignature(method, SpecialType.System_Int32, SpecialType.System_Int32) =>
                CreateArrayDimension(out model),
            "GetLongLength" when IsInstanceSignature(method, SpecialType.System_Int64, SpecialType.System_Int32) =>
                CreateArrayDimension(out model),
            "GetLowerBound" or "GetUpperBound"
                when IsInstanceSignature(method, SpecialType.System_Int32, SpecialType.System_Int32) =>
                CreateArrayDimension(out model),
            "get_Rank" when method is {
                MethodKind: MethodKind.PropertyGet, IsStatic: false, Parameters.Length: 0,
                ReturnType.SpecialType: SpecialType.System_Int32
            } => Create(SharpProofEffect.ReadsReceiverState, out model),
            "CreateInstance" => TryCreateArrayInstance(method, out model),
            "GetValue" => TryCreateArrayGetValue(method, out model),
            "SetValue" => TryCreateArraySetValue(method, out model),
            "CopyTo" when IsVoidSignature(method, false,
                SpecialType.System_Array, SpecialType.System_Int32) =>
                Create(SharpProofEffect.ReadsReceiverState | SharpProofEffect.WritesArgumentState |
                       SharpProofEffect.Throws, out model,
                    CopyFacts("framework_array_copy_to_model"), write: [0]),
            _ => false
        };
    }

    private static bool TryCreateArrayCopy(
        IMethodSymbol method,
        bool constrained,
        out KnownEffectModel model) {
        model = null!;
        var signature = constrained
            ? IsVoidSignature(method, true, SpecialType.System_Array, SpecialType.System_Int32,
                SpecialType.System_Array, SpecialType.System_Int32, SpecialType.System_Int32)
            : IsVoidSignature(method, true, SpecialType.System_Array, SpecialType.System_Array,
                  SpecialType.System_Int32) ||
              IsVoidSignature(method, true, SpecialType.System_Array, SpecialType.System_Int32,
                  SpecialType.System_Array, SpecialType.System_Int32, SpecialType.System_Int32);
        if (!signature || constrained != string.Equals(method.Name, "ConstrainedCopy", StringComparison.Ordinal))
            return false;
        var destination = method.Parameters.Length == 3 ? 1 : 2;
        var reason = constrained ? "framework_array_constrained_copy_model" : "framework_array_copy_model";
        return Create(
            SharpProofEffect.ReadsArgumentState | SharpProofEffect.WritesArgumentState | SharpProofEffect.Throws,
            out model, CopyFacts(reason), write: [destination], read: [0]);
    }

    private static bool TryCreateArrayFill(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (!IsGenericArrayMethod(method, "Fill", RefKind.None, 2, 4) ||
            !IsGenericValueParameter(method, 1) ||
            method.Parameters.Length == 4 &&
            (!IsParameter(method.Parameters[2], SpecialType.System_Int32) ||
             !IsParameter(method.Parameters[3], SpecialType.System_Int32)))
            return false;
        var exceptions = method.Parameters.Length == 2
            ? Facts("framework_array_fill_model", "System.ArgumentNullException")
            : Facts("framework_array_fill_model", "System.ArgumentNullException",
                "System.ArgumentOutOfRangeException", "System.ArgumentException");
        return Create(SharpProofEffect.WritesArgumentState | SharpProofEffect.Throws, out model,
            exceptions, write: [0]);
    }

    private static bool TryCreateArrayResize(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (!IsGenericArrayMethod(method, "Resize", RefKind.Ref, 2) ||
            !IsParameter(method.Parameters[1], SpecialType.System_Int32))
            return false;
        return Create(
            SharpProofEffect.ReadsArgumentState | SharpProofEffect.WritesArgumentState |
            SharpProofEffect.Allocates | SharpProofEffect.Throws,
            out model,
            Facts("framework_array_resize_model", "System.ArgumentOutOfRangeException"),
            write: [0], read: [0]);
    }

    private static bool TryCreateArrayReverse(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method is not {
            MethodKind: MethodKind.Ordinary, Name: "Reverse", IsStatic: true, ReturnsVoid: true
        } ||
            method.Parameters.Length is not (1 or 3))
            return false;
        var generic = method.IsGenericMethod && method.TypeArguments.Length == 1 &&
                      IsGenericArrayParameter(method.Parameters[0], method.TypeArguments[0], RefKind.None);
        var nonGeneric = !method.IsGenericMethod &&
                         IsParameter(method.Parameters[0], SpecialType.System_Array);
        if ((!generic && !nonGeneric) ||
            method.Parameters.Length == 3 &&
            (!IsParameter(method.Parameters[1], SpecialType.System_Int32) ||
             !IsParameter(method.Parameters[2], SpecialType.System_Int32)))
            return false;
        var reason = "framework_array_reverse_model";
        var exceptions = (generic, method.Parameters.Length) switch {
            (true, 1) => Facts(reason, "System.ArgumentNullException"),
            (true, 3) => Facts(reason, "System.ArgumentNullException",
                "System.ArgumentOutOfRangeException", "System.ArgumentException"),
            (false, 1) => Facts(reason, "System.ArgumentNullException", "System.RankException"),
            _ => Facts(reason, "System.ArgumentNullException", "System.RankException",
                "System.ArgumentOutOfRangeException", "System.ArgumentException")
        };
        return Create(
            SharpProofEffect.ReadsArgumentState | SharpProofEffect.WritesArgumentState | SharpProofEffect.Throws,
            out model, exceptions, write: [0], read: [0]);
    }

    private static bool TryCreateArrayInstance(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method is not {
            MethodKind: MethodKind.Ordinary, Name: "CreateInstance", IsStatic: true,
            ReturnType.SpecialType: SpecialType.System_Array
        } ||
            method.Parameters.Length is < 2 or > 4 ||
            method.Parameters[0].RefKind != RefKind.None ||
            method.Parameters[0].Type.ToDisplayString() != "System.Type")
            return false;
        var tail = method.Parameters.Skip(1).ToArray();
        var scalar = tail.All(static parameter =>
            parameter is { RefKind: RefKind.None, Type.SpecialType: SpecialType.System_Int32 });
        var arrays = tail.All(static parameter => parameter is {
            RefKind: RefKind.None,
            Type: IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Int32 }
        });
        if (!(scalar && tail.Length is 1 or 2 or 3) && !(arrays && tail.Length is 1 or 2))
            return false;
        ImmutableArray<int> read = arrays ? [.. Enumerable.Range(1, tail.Length)] : [];
        return Create(SharpProofEffect.Allocates | SharpProofEffect.Throws |
                      (arrays ? SharpProofEffect.ReadsArgumentState : SharpProofEffect.None),
            out model,
            Facts("framework_array_create_instance_model", "System.ArgumentNullException",
                "System.ArgumentException", "System.NotSupportedException", "System.ArgumentOutOfRangeException"),
            read: read);
    }

    private static bool TryCreateArrayGetValue(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method is not {
            MethodKind: MethodKind.Ordinary, Name: "GetValue", IsStatic: false,
            ReturnType.SpecialType: SpecialType.System_Object
        })
            return false;
        var indexes = IsInt32ArrayParameter(method.Parameters);
        var scalar = IsIndexParameters(method.Parameters);
        if (!indexes && !scalar) return false;
        return Create(
            SharpProofEffect.ReadsReceiverState | SharpProofEffect.Allocates | SharpProofEffect.Throws |
            (indexes ? SharpProofEffect.ReadsArgumentState : SharpProofEffect.None),
            out model,
            Facts("framework_array_get_value_model",
                "System.IndexOutOfRangeException", "System.ArgumentException"),
            read: indexes ? [0] : []);
    }

    private static bool TryCreateArraySetValue(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method is not {
            MethodKind: MethodKind.Ordinary, Name: "SetValue", IsStatic: false, ReturnsVoid: true
        } ||
            method.Parameters.Length is < 2 or > 4 ||
            !IsParameter(method.Parameters[0], SpecialType.System_Object))
            return false;
        var indexes = IsInt32ArrayParameter(method.Parameters.RemoveAt(0));
        var scalar = IsIndexParameters(method.Parameters.RemoveAt(0));
        if (!indexes && !scalar) return false;
        return Create(
            SharpProofEffect.WritesReceiverState | SharpProofEffect.Throws |
            (indexes ? SharpProofEffect.ReadsArgumentState : SharpProofEffect.None),
            out model,
            Facts("framework_array_set_value_model", "System.IndexOutOfRangeException",
                "System.ArgumentException", "System.InvalidCastException"),
            read: indexes ? [1] : []);
    }

    private static bool TryCreateBuffer(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method is not { MethodKind: MethodKind.Ordinary, IsStatic: true } ||
            method.ContainingType.ToDisplayString() != "System.Buffer")
            return false;
        return method.Name switch {
            "BlockCopy" when IsVoidSignature(method, true, SpecialType.System_Array,
                SpecialType.System_Int32, SpecialType.System_Array, SpecialType.System_Int32,
                SpecialType.System_Int32) =>
                Create(SharpProofEffect.ReadsArgumentState | SharpProofEffect.WritesArgumentState |
                       SharpProofEffect.Throws, out model,
                    Facts("framework_buffer_block_copy_model", "System.ArgumentNullException",
                        "System.ArgumentException", "System.ArgumentOutOfRangeException"),
                    write: [2], read: [0]),
            "ByteLength" when IsSignature(method, SpecialType.System_Int32, SpecialType.System_Array) =>
                Create(SharpProofEffect.ReadsArgumentState | SharpProofEffect.Throws, out model,
                    Facts("framework_buffer_byte_length_model",
                        "System.ArgumentNullException", "System.ArgumentException"), read: [0]),
            "GetByte" when HasParameters(method, SpecialType.System_Byte,
                SpecialType.System_Array, SpecialType.System_Int32) =>
                Create(SharpProofEffect.ReadsArgumentState | SharpProofEffect.Throws, out model,
                    Facts("framework_buffer_get_byte_model", "System.ArgumentNullException",
                        "System.ArgumentException", "System.ArgumentOutOfRangeException"), read: [0]),
            "SetByte" when IsVoidSignature(method, true, SpecialType.System_Array,
                SpecialType.System_Int32, SpecialType.System_Byte) =>
                Create(SharpProofEffect.WritesArgumentState | SharpProofEffect.Throws, out model,
                    Facts("framework_buffer_set_byte_model", "System.ArgumentNullException",
                        "System.ArgumentException", "System.ArgumentOutOfRangeException"), write: [0]),
            "MemoryCopy" when IsBufferMemoryCopy(method) =>
                Create(SharpProofEffect.ReadsArgumentState | SharpProofEffect.WritesArgumentState |
                       SharpProofEffect.Throws, out model,
                    Facts("framework_buffer_memory_copy_model", "System.ArgumentOutOfRangeException"),
                    write: [1], read: [0]),
            _ => false
        };
    }

    private static bool TryCreateFramework(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (TryCreateMemoryMarshal(method, out model) ||
            TryCreateRuntimeHelper(method, out model) ||
            TryCreateSpan(method, out model) ||
            TryCreateThreading(method, out model))
            return true;
        var type = method.ContainingType;
        var definition = type?.OriginalDefinition.ToDisplayString();
        var numeric = SymbolicTypeFacts.IsBuiltInNumericSpecialType(type?.SpecialType ?? SpecialType.None) &&
                      type?.SpecialType != SpecialType.System_Char;
        if (TryCreateThrowingFrameworkMember(
                method,
                definition,
                numeric,
                out model))
            return true;
        SharpProofEffect? effects = (definition, method.MethodKind, method.Name) switch {
            ("System.Collections.Generic.List<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>",
                MethodKind.PropertyGet, _) => SharpProofEffect.ReadsReceiverState,
            ("System.Collections.Generic.List<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>",
                MethodKind.PropertySet or MethodKind.Constructor, _) => SharpProofEffect.WritesReceiverState,
            ("System.Collections.Generic.List<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>", _, "Add") =>
                SharpProofEffect.WritesReceiverState | SharpProofEffect.Allocates,
            ("System.Array" or "System.Linq.Enumerable", _, "Empty")
                when method.IsGenericMethod && method.Parameters.Length == 0 => SharpProofEffect.None,
            (_, MethodKind.PropertyGet, _) when method.AssociatedSymbol is IPropertySymbol property &&
                                               IsStatelessEmptyViewProperty(property) => SharpProofEffect.None,
            (_, MethodKind.Ordinary, "GetType")
                when type?.SpecialType == SpecialType.System_Object && method.Parameters.Length == 0 =>
                SharpProofEffect.None,
            (_, _, "IsNullOrEmpty" or "IsNullOrWhiteSpace")
                when type?.SpecialType == SpecialType.System_String => SharpProofEffect.None,
            (_, _, "Contains" or "IndexOf" or "LastIndexOf" or "StartsWith" or "EndsWith")
                when type?.SpecialType == SpecialType.System_String &&
                     method.Parameters.Length == 1 &&
                     method.Parameters[0].Type.SpecialType == SpecialType.System_Char => SharpProofEffect.None,
            ("System.Math" or "System.MathF", _, "Min" or "Max" or "Sqrt") => SharpProofEffect.None,
            (_, _, "Parse") when numeric => SharpProofEffect.Throws,
            (_, _, "ToString") when numeric => SharpProofEffect.Allocates,
            (_, _, "Split" or "Substring" or "Trim" or "TrimStart" or "TrimEnd" or "Replace" or
                "ToUpper" or "ToUpperInvariant" or "ToLower" or "ToLowerInvariant")
                when type?.SpecialType == SpecialType.System_String => SharpProofEffect.Allocates,
            (_, _, "ToCharArray")
                when type?.SpecialType == SpecialType.System_String && method.Parameters.Length == 0 =>
                SharpProofEffect.Allocates,
            _ => null
        };
        var exceptions = effects == SharpProofEffect.Throws
            ? Facts("framework_parse_model", "System.FormatException", "System.OverflowException")
            : [];
        return effects.HasValue && Create(effects.Value, out model, exceptions);
    }
    private static bool TryCreateThrowingFrameworkMember(
        IMethodSymbol method,
        string? definition,
        bool numeric,
        out KnownEffectModel model) {
        model = null!;
        if (numeric &&
            method.Name == "ToString" &&
            method.Parameters.Length != 0)
            return Create(
                SharpProofEffect.Allocates | SharpProofEffect.Throws,
                out model,
                Facts(
                    "framework_numeric_format_model",
                    "System.FormatException"));
        if (method.ContainingType?.SpecialType == SpecialType.System_String &&
            method.Name is "Split" or "Replace")
            return Create(
                SharpProofEffect.Allocates | SharpProofEffect.Throws,
                out model,
                Facts(
                    "framework_string_argument_validation_model",
                    "System.ArgumentException",
                    "System.ArgumentNullException",
                    "System.ArgumentOutOfRangeException"));
        if (definition is not
            ("System.Collections.Generic.List<T>" or
             "System.Collections.Generic.Dictionary<TKey, TValue>"))
            return false;
        var throws = method.Name == "Add" ||
                     method.MethodKind == MethodKind.Constructor &&
                     method.Parameters.Length != 0 ||
                     method.AssociatedSymbol is IPropertySymbol {
                         IsIndexer: true
                     } ||
                     method.MethodKind == MethodKind.PropertySet;
        if (!throws)
            return false;
        var effects = method.MethodKind == MethodKind.PropertyGet
            ? SharpProofEffect.ReadsReceiverState
            : SharpProofEffect.WritesReceiverState;
        if (method.Name == "Add")
            effects |= SharpProofEffect.Allocates;
        return Create(
            effects | SharpProofEffect.Throws,
            out model,
            Facts(
                "framework_collection_argument_validation_model",
                "System.ArgumentException",
                "System.ArgumentNullException",
                "System.ArgumentOutOfRangeException",
                "System.Collections.Generic.KeyNotFoundException"));
    }

    private static bool TryCreateThreading(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method.MethodKind != MethodKind.Ordinary) return false;
        var definition = method.ContainingType?.OriginalDefinition.ToDisplayString();
        if (definition == "System.Threading.Interlocked") {
            var first = method.Parameters.FirstOrDefault();
            var integralRef = first is { RefKind: RefKind.Ref } &&
                              first.Type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64;
            if (method.Name is "Increment" or "Decrement" && method.Parameters.Length == 1 && integralRef)
                return Create(SharpProofEffect.WritesArgumentState, out model, write: [0]);
            if (method.Name is "Exchange" or "Add" && method.Parameters.Length == 2 && integralRef &&
                method.Parameters[1].Type.SpecialType == first!.Type.SpecialType)
                return Create(SharpProofEffect.WritesArgumentState, out model, write: [0]);
            if (method.Name == "CompareExchange" && method.Parameters.Length == 3 && integralRef &&
                method.Parameters[1].Type.SpecialType == first!.Type.SpecialType &&
                method.Parameters[2].Type.SpecialType == first.Type.SpecialType)
                return Create(SharpProofEffect.WritesArgumentState, out model, write: [0]);
            if (method.Name == "Read" && method.Parameters.Length == 1 &&
                first is { RefKind: not RefKind.None, Type.SpecialType: SpecialType.System_Int64 })
                return Create(SharpProofEffect.ReadsArgumentState, out model, read: [0]);
            return false;
        }
        if (definition != "System.Threading.Volatile") return false;
        if (method.Name == "Read" &&
            method.Parameters.Length == 1 &&
            method.Parameters[0].RefKind != RefKind.None)
            return Create(SharpProofEffect.ReadsArgumentState, out model, read: [0]);
        return method.Name == "Write" &&
               method.Parameters.Length == 2 &&
               method.Parameters[0].RefKind != RefKind.None &&
               Create(SharpProofEffect.WritesArgumentState, out model, write: [0]);
    }

    private static bool TryCreateSpan(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        var definition = method.ContainingType?.OriginalDefinition.ToDisplayString();
        if (method.MethodKind == MethodKind.PropertyGet &&
            method.AssociatedSymbol is IPropertySymbol property &&
            IsStatelessEmptyViewProperty(property))
            return Create(SharpProofEffect.None, out model);
        if (method.MethodKind == MethodKind.Ordinary &&
            method.ContainingType?.SpecialType == SpecialType.System_String &&
            method.Name == "CopyTo" &&
            HasSingleCharSpanParameter(method))
            return Create(SharpProofEffect.WritesArgumentState, out model, write: [0]);
        if (method.MethodKind == MethodKind.Ordinary && IsSpanCopy(method))
            return Create(
                SharpProofEffect.ReadsReceiverState | SharpProofEffect.WritesArgumentState,
                out model,
                write: [0]);
        if (method.MethodKind == MethodKind.Ordinary && IsSpanFill(method))
            return Create(SharpProofEffect.WritesReceiverState, out model);
        if (definition == "System.Span<T>" && method is {
            MethodKind: MethodKind.Ordinary, Name: "Clear", Parameters.Length: 0
        })
            return Create(SharpProofEffect.WritesReceiverState, out model);
        if (method.MethodKind == MethodKind.Ordinary && IsSpanReverse(method))
            return Create(SharpProofEffect.WritesArgumentState, out model, write: [0]);
        if (method.MethodKind == MethodKind.Ordinary && IsPureSpanOverlaps(method))
            return Create(SharpProofEffect.None, out model);
        if (method.MethodKind == MethodKind.Ordinary && IsSpanOverlapsWithOffset(method))
            return Create(SharpProofEffect.WritesArgumentState, out model, write: [2]);
        return definition is "System.Span<T>" or "System.ReadOnlySpan<T>" or
                   "System.Memory<T>" or "System.ReadOnlyMemory<T>" &&
               method.Name == "ToArray" &&
               Create(SharpProofEffect.Allocates, out model);
    }

    private static bool TryCreateMemoryMarshal(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method is not {
            MethodKind: MethodKind.Ordinary, IsStatic: true, IsGenericMethod: true,
            TypeArguments.Length: 1
        } ||
            method.ContainingType.ToDisplayString() != "System.Runtime.InteropServices.MemoryMarshal")
            return false;
        var reading = method.Name is "TryRead" or "Read";
        var throwing = method.Name is "Read" or "Write";
        var sourceDefinition = reading ? "System.ReadOnlySpan<T>" : "System.Span<T>";
        var expectedCount = method.Name == "Read" ? 1 : 2;
        if (method.Name is not ("TryRead" or "Read" or "TryWrite" or "Write") ||
            method.Parameters.Length != expectedCount ||
            method.Parameters[0] is not {
                RefKind: RefKind.None,
                Type: INamedTypeSymbol { TypeArguments.Length: 1 } buffer
            } ||
            buffer.OriginalDefinition.ToDisplayString() != sourceDefinition ||
            buffer.TypeArguments[0].SpecialType != SpecialType.System_Byte)
            return false;
        var valueMatches = method.Name switch {
            "TryRead" => method.ReturnType.SpecialType == SpecialType.System_Boolean &&
                         method.Parameters[1].RefKind == RefKind.Out &&
                         SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, method.TypeArguments[0]),
            "Read" => SymbolEqualityComparer.Default.Equals(method.ReturnType, method.TypeArguments[0]),
            "TryWrite" => method.ReturnType.SpecialType == SpecialType.System_Boolean &&
                          method.Parameters[1].RefKind == RefKind.In &&
                          SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, method.TypeArguments[0]),
            _ => method.ReturnsVoid &&
                 method.Parameters[1].RefKind == RefKind.In &&
                 SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, method.TypeArguments[0])
        };
        if (!valueMatches) return false;
        var effects = SharpProofEffect.ReadsArgumentState |
                      (method.Name == "Read" ? SharpProofEffect.None : SharpProofEffect.WritesArgumentState) |
                      (throwing ? SharpProofEffect.Throws : SharpProofEffect.None);
        var reason = method.Name == "Write"
            ? "framework_memory_marshal_write_model"
            : "framework_memory_marshal_read_model";
        var exceptions = throwing
            ? Facts(reason, "System.ArgumentOutOfRangeException", "System.ArgumentException")
            : [];
        return Create(effects, out model, exceptions,
            write: method.Name == "TryRead" ? [1] : method.Name is "TryWrite" or "Write" ? [0] : [],
            read: method.Name is "TryWrite" or "Write" ? [1] : [0]);
    }

    private static bool TryCreateRuntimeHelper(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method is not {
            MethodKind: MethodKind.Ordinary, IsStatic: true, IsGenericMethod: true,
            TypeArguments.Length: 1
        } ||
            method.ContainingType.ToDisplayString() != "System.Runtime.CompilerServices.RuntimeHelpers")
            return false;
        if (method is {
            Name: "IsReferenceOrContainsReferences", Parameters.Length: 0,
            ReturnType.SpecialType: SpecialType.System_Boolean
        })
            return Create(SharpProofEffect.None, out model);
        if (method is not {
            Name: "GetSubArray", Parameters.Length: 2, ReturnType: IArrayTypeSymbol result
        } ||
            method.Parameters[0] is not { RefKind: RefKind.None, Type: IArrayTypeSymbol source } ||
            !SymbolEqualityComparer.Default.Equals(source.ElementType, method.TypeArguments[0]) ||
            !SymbolEqualityComparer.Default.Equals(result.ElementType, method.TypeArguments[0]) ||
            method.Parameters[1] is not {
                RefKind: RefKind.None,
                Type.Name: "Range",
                Type.ContainingNamespace: { } rangeNamespace
            } ||
            rangeNamespace.ToDisplayString() != "System")
            return false;
        return Create(
            SharpProofEffect.ReadsArgumentState | SharpProofEffect.Allocates | SharpProofEffect.Throws,
            out model,
            Facts("framework_runtime_helpers_get_sub_array_model",
                "System.ArgumentNullException", "System.ArgumentOutOfRangeException"),
            read: [0]);
    }

    private static bool TryCreateConversion(
        IMethodSymbol method,
        SpecialType result,
        string resultName,
        out KnownEffectModel model) {
        model = null!;
        var resultMatches = result == SpecialType.None
            ? method.ReturnType.ToDisplayString() == "System.Half"
            : method.ReturnType.SpecialType == result;
        if (!resultMatches) return false;
        var source = method.Parameters.FirstOrDefault();
        var array = method.Parameters.Length == 2 && IsByteArray(source?.Type) &&
                    IsParameter(method.Parameters[1], SpecialType.System_Int32);
        var span = method.Parameters.Length == 1 && IsByteSpan(source?.Type, readOnly: true);
        if (!array && !span || source?.RefKind != RefKind.None) return false;
        var reason = "framework_bit_converter_to_" + resultName + (array ? "_array_model" : "_span_model");
        var exceptions = array
            ? ExceptionFacts(reason, includeNull: true, includeArgument: result != SpecialType.System_Boolean)
            : ExceptionFacts(reason, includeArgument: true, includeOutOfRange: false);
        return Create(SharpProofEffect.ReadsArgumentState | SharpProofEffect.Throws, out model, exceptions, read: [0]);
    }

    private static bool TryCreateToString(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method.ReturnType.SpecialType != SpecialType.System_String ||
            method.Parameters.Length is < 1 or > 3 ||
            !IsParameter(method.Parameters[0], IsByteArray))
            return false;
        for (var index = 1; index < method.Parameters.Length; index++)
            if (!IsParameter(method.Parameters[index], SpecialType.System_Int32)) return false;
        var suffix = method.Parameters.Length switch { 1 => "array", 2 => "array_from_offset", _ => "array_range" };
        var reason = "framework_bit_converter_to_string_" + suffix + "_model";
        var exceptions = ExceptionFacts(
            reason,
            includeNull: true,
            includeArgument: method.Parameters.Length == 3,
            includeOutOfRange: method.Parameters.Length > 1);
        return Create(
            SharpProofEffect.ReadsArgumentState | SharpProofEffect.Allocates | SharpProofEffect.Throws,
            out model,
            exceptions,
            read: [0]);
    }

    private static bool TryCreateGetBytes(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method.Parameters.Length != 1 || !IsByteArray(method.ReturnType) ||
            !IsSupportedScalar(method.Parameters[0]))
            return false;
        return Create(SharpProofEffect.Allocates, out model);
    }

    private static bool TryCreateTryWriteBytes(IMethodSymbol method, out KnownEffectModel model) {
        model = null!;
        if (method.ReturnType.SpecialType != SpecialType.System_Boolean ||
            method.Parameters.Length != 2 ||
            !IsParameter(method.Parameters[0], type => IsByteSpan(type, readOnly: false)) ||
            !IsSupportedScalar(method.Parameters[1]))
            return false;
        return Create(SharpProofEffect.WritesArgumentState, out model, write: [0]);
    }

    private static bool IsSignature(
        IMethodSymbol method,
        SpecialType result,
        SpecialType parameter) =>
        method.ReturnType.SpecialType == result &&
        method.Parameters.Length == 1 &&
        IsParameter(method.Parameters[0], parameter);

    private static bool IsInstanceSignature(
        IMethodSymbol method,
        SpecialType result,
        params SpecialType[] parameters) =>
        method is { MethodKind: MethodKind.Ordinary, IsStatic: false } &&
        HasParameters(method, result, parameters);

    private static bool HasParameters(
        IMethodSymbol method,
        SpecialType result,
        params SpecialType[] parameters) =>
        method.ReturnType.SpecialType == result &&
        method.Parameters.Length == parameters.Length &&
        method.Parameters.Select(static parameter => parameter.RefKind == RefKind.None
                ? parameter.Type.SpecialType
                : SpecialType.None)
            .SequenceEqual(parameters);

    private static bool IsVoidSignature(
        IMethodSymbol method,
        bool isStatic,
        params SpecialType[] parameters) =>
        method is { MethodKind: MethodKind.Ordinary, ReturnsVoid: true } &&
        method.IsStatic == isStatic &&
        method.Parameters.Length == parameters.Length &&
        method.Parameters.Select(static parameter => parameter.RefKind == RefKind.None
                ? parameter.Type.SpecialType
                : SpecialType.None)
            .SequenceEqual(parameters);

    private static bool IsGenericArrayMethod(
        IMethodSymbol method,
        string name,
        RefKind arrayRefKind,
        params int[] parameterCounts) =>
        method is {
            MethodKind: MethodKind.Ordinary, IsStatic: true, IsGenericMethod: true,
            TypeArguments.Length: 1, ReturnsVoid: true
        } &&
        string.Equals(method.Name, name, StringComparison.Ordinal) &&
        parameterCounts.Contains(method.Parameters.Length) &&
        IsGenericArrayParameter(method.Parameters[0], method.TypeArguments[0], arrayRefKind);

    private static bool IsGenericArrayParameter(
        IParameterSymbol parameter,
        ITypeSymbol element,
        RefKind refKind) =>
        parameter.RefKind == refKind &&
        parameter.Type is IArrayTypeSymbol array &&
        SymbolEqualityComparer.Default.Equals(array.ElementType, element);

    private static bool IsGenericValueParameter(IMethodSymbol method, int ordinal) =>
        method.Parameters[ordinal].RefKind == RefKind.None &&
        SymbolEqualityComparer.Default.Equals(method.Parameters[ordinal].Type, method.TypeArguments[0]);

    private static bool IsIndexParameters(ImmutableArray<IParameterSymbol> parameters) =>
        parameters.Length is >= 1 and <= 3 &&
        (parameters.Length != 1 ||
         parameters[0].Type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64) &&
        parameters.All(static parameter => parameter.RefKind == RefKind.None &&
            parameter.Type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64) &&
        (parameters.Length == 1 || parameters.All(static parameter =>
            parameter.Type.SpecialType == SpecialType.System_Int32));

    private static bool IsInt32ArrayParameter(ImmutableArray<IParameterSymbol> parameters) =>
        parameters.Length == 1 &&
        parameters[0] is {
            RefKind: RefKind.None,
            Type: IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Int32 }
        };

    private static bool IsBufferMemoryCopy(IMethodSymbol method) =>
        method is { Parameters.Length: 4, ReturnsVoid: true } &&
        method.Parameters[0] is {
            RefKind: RefKind.None,
            Type: IPointerTypeSymbol { PointedAtType.SpecialType: SpecialType.System_Void }
        } &&
        method.Parameters[1] is {
            RefKind: RefKind.None,
            Type: IPointerTypeSymbol { PointedAtType.SpecialType: SpecialType.System_Void }
        } &&
        method.Parameters[2].RefKind == RefKind.None &&
        method.Parameters[3].RefKind == RefKind.None &&
        method.Parameters[2].Type.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64 &&
        method.Parameters[3].Type.SpecialType == method.Parameters[2].Type.SpecialType;

    private static bool CreateArrayDimension(out KnownEffectModel model) =>
        Create(SharpProofEffect.ReadsReceiverState | SharpProofEffect.Throws, out model,
            Facts("framework_array_get_length_model", "System.IndexOutOfRangeException"));

    private static bool IsSupportedScalar(IParameterSymbol parameter) =>
        parameter.RefKind == RefKind.None &&
        (ScalarTypes.Contains(parameter.Type.SpecialType) || parameter.Type.ToDisplayString() == "System.Half");

    private static bool IsParameter(IParameterSymbol parameter, SpecialType type) =>
        parameter is { RefKind: RefKind.None } && parameter.Type.SpecialType == type;

    private static bool IsParameter(IParameterSymbol parameter, Func<ITypeSymbol, bool> predicate) =>
        parameter.RefKind == RefKind.None && predicate(parameter.Type);

    private static bool HasSingleCharSpanParameter(IMethodSymbol method) =>
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type is INamedTypeSymbol { TypeArguments.Length: 1 } span &&
        span.OriginalDefinition.ToDisplayString() == "System.Span<T>" &&
        span.TypeArguments[0].SpecialType == SpecialType.System_Char;

    private static bool IsSpanCopy(IMethodSymbol method) =>
        method is {
            Name: "CopyTo" or "TryCopyTo",
            Parameters.Length: 1,
            ContainingType.TypeArguments.Length: 1
        } &&
        method.ContainingType.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>" &&
        method.Parameters[0].Type is INamedTypeSymbol { TypeArguments.Length: 1 } destination &&
        destination.OriginalDefinition.ToDisplayString() == "System.Span<T>" &&
        SymbolEqualityComparer.Default.Equals(method.ContainingType.TypeArguments[0], destination.TypeArguments[0]);

    private static bool IsSpanFill(IMethodSymbol method) =>
        method is { Name: "Fill", Parameters.Length: 1, ContainingType.TypeArguments.Length: 1 } &&
        method.ContainingType.OriginalDefinition.ToDisplayString() == "System.Span<T>" &&
        SymbolEqualityComparer.Default.Equals(method.ContainingType.TypeArguments[0], method.Parameters[0].Type);

    private static bool IsSpanReverse(IMethodSymbol method) =>
        method is { Name: "Reverse", Parameters.Length: 1 } &&
        method.ContainingType.ToDisplayString() == "System.MemoryExtensions" &&
        method.Parameters[0].Type.OriginalDefinition.ToDisplayString() == "System.Span<T>";

    private static bool IsPureSpanOverlaps(IMethodSymbol method) =>
        TryGetSpanOverlapTypes(method, 2, out _, out _);

    private static bool IsSpanOverlapsWithOffset(IMethodSymbol method) =>
        TryGetSpanOverlapTypes(method, 3, out _, out _) &&
        method.Parameters[2] is { RefKind: RefKind.Out, Type.SpecialType: SpecialType.System_Int32 };

    private static bool TryGetSpanOverlapTypes(
        IMethodSymbol method,
        int parameterCount,
        out INamedTypeSymbol left,
        out INamedTypeSymbol right) {
        left = null!;
        right = null!;
        if (method is not { Name: "Overlaps" } ||
            method.Parameters.Length != parameterCount ||
            method.ContainingType.ToDisplayString() != "System.MemoryExtensions" ||
            method.Parameters[0].Type is not INamedTypeSymbol { TypeArguments.Length: 1 } leftType ||
            method.Parameters[1].Type is not INamedTypeSymbol { TypeArguments.Length: 1 } rightType ||
            leftType.OriginalDefinition.ToDisplayString() != "System.ReadOnlySpan<T>" ||
            rightType.OriginalDefinition.ToDisplayString() != "System.ReadOnlySpan<T>" ||
            !SymbolEqualityComparer.Default.Equals(leftType.TypeArguments[0], rightType.TypeArguments[0]))
            return false;
        left = leftType;
        right = rightType;
        return true;
    }

    internal static bool IsStatelessEmptyViewProperty(IPropertySymbol property) =>
        property is { Name: "Empty", IsStatic: true } &&
        property.ContainingType.OriginalDefinition.ToDisplayString() is
            "System.Span<T>" or "System.ReadOnlySpan<T>" or
            "System.Memory<T>" or "System.ReadOnlyMemory<T>";

    private static bool IsByteArray(ITypeSymbol? type) =>
        type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte };

    private static bool IsByteSpan(ITypeSymbol? type, bool readOnly) =>
        type is INamedTypeSymbol { TypeArguments.Length: 1 } span &&
        span.OriginalDefinition.ToDisplayString() == (readOnly ? "System.ReadOnlySpan<T>" : "System.Span<T>") &&
        span.TypeArguments[0].SpecialType == SpecialType.System_Byte;

    private static ImmutableArray<MethodExceptionFact> ExceptionFacts(
        string reason,
        bool includeNull = false,
        bool includeArgument = false,
        bool includeOutOfRange = true) {
        var facts = ImmutableArray.CreateBuilder<MethodExceptionFact>(3);
        if (includeNull)
            facts.Add(MethodExceptionFact.Boundary(
                "System.ArgumentNullException", MethodExceptionSource.Contract, reason));
        if (includeOutOfRange)
            facts.Add(MethodExceptionFact.Boundary(
                "System.ArgumentOutOfRangeException", MethodExceptionSource.Contract, reason));
        if (includeArgument)
            facts.Add(MethodExceptionFact.Boundary(
                "System.ArgumentException", MethodExceptionSource.Contract, reason));
        return facts.ToImmutable();
    }

    private static ImmutableArray<MethodExceptionFact> CopyFacts(string reason) =>
        Facts(reason, "System.ArgumentNullException", "System.RankException",
            "System.ArrayTypeMismatchException", "System.InvalidCastException",
            "System.ArgumentOutOfRangeException", "System.ArgumentException");

    private static ImmutableArray<MethodExceptionFact> Facts(string reason, params string[] exceptionTypes) =>
        [.. exceptionTypes.Select(exceptionType =>
            MethodExceptionFact.Boundary(exceptionType, MethodExceptionSource.Contract, reason))];

    private static bool Create(
        SharpProofEffect effects,
        out KnownEffectModel model,
        ImmutableArray<MethodExceptionFact> exceptions = default,
        ImmutableArray<int> write = default,
        ImmutableArray<int> read = default) {
        model = new(
            new(effects, SharpProofCapability.None,
                exceptions.IsDefault ? [] : exceptions, [], []),
            write.IsDefault ? [] : write,
            read.IsDefault ? [] : read);
        return true;
    }
}
