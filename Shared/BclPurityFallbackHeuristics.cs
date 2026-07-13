using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine;

internal static class BclPurityFallbackHeuristics
{
    public const string CatalogSource = "bcl_heuristic_fallback";
    public const string ProbablyPure = "probably_pure";
    public const string ProbablyImpure = "probably_impure";
    public const string Unknown = "unknown";

    public static bool TryClassify(Shape shape, out Classification classification)
    {
        classification = default;
        if (!shape.IsFrameworkMetadataSymbol) return false;

        if (shape.HasRefOrOutParameter)
        {
            classification = ProbablyImpureBecause("ref_or_out_parameter");
            return true;
        }

        if (shape.ReturnsByRef)
        {
            classification = ProbablyImpureBecause("byref_return");
            return true;
        }

        if (IsAmbientNamespaceOrType(shape.NamespaceName, shape.TypeName))
        {
            classification = ProbablyImpureBecause("ambient_namespace_or_type");
            return true;
        }

        if (shape.IsProperty)
        {
            classification = ClassifyProperty(shape);
            return true;
        }

        if (shape.IsField)
        {
            classification = ClassifyField(shape);
            return true;
        }

        classification = ClassifyMethod(shape);
        return true;
    }

    public static bool IsFrameworkSystemAssemblyName(string assemblyName)
    {
        return assemblyName.Equals("mscorlib", StringComparison.Ordinal) ||
               assemblyName.Equals("netstandard", StringComparison.Ordinal) ||
               assemblyName.Equals("System", StringComparison.Ordinal) ||
               assemblyName.Equals("System.Private.CoreLib", StringComparison.Ordinal) ||
               assemblyName.StartsWith("System.", StringComparison.Ordinal);
    }

    public static bool IsSystemNamespace(string namespaceName)
    {
        return namespaceName.Equals("System", StringComparison.Ordinal) ||
               namespaceName.StartsWith("System.", StringComparison.Ordinal);
    }

    public static bool IsValueLikeTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return false;

        var normalized = NormalizeTypeName(typeName);
        if (IsKnownPrimitiveOrValueAlias(normalized)) return true;

        return normalized.Equals("System.String", StringComparison.Ordinal) ||
               normalized.Equals("System.Object", StringComparison.Ordinal) ||
               normalized.Equals("System.Type", StringComparison.Ordinal) ||
               normalized.Equals("System.Version", StringComparison.Ordinal) ||
               normalized.Equals("System.Uri", StringComparison.Ordinal) ||
               normalized.Equals("System.Globalization.CultureInfo", StringComparison.Ordinal) ||
               IsLikelyFrameworkValueTypeName(normalized);
    }

    public static bool IsReadOnlyViewTypeName(string typeName)
    {
        var normalized = NormalizeTypeName(typeName);
        return normalized.StartsWith("System.ReadOnlySpan<", StringComparison.Ordinal) ||
               normalized.StartsWith("System.ReadOnlyMemory<", StringComparison.Ordinal);
    }

    public static bool IsKnownValueTypeName(string typeName)
    {
        var normalized = NormalizeTypeName(typeName);
        return IsKnownPrimitiveOrValueAlias(normalized) ||
               IsLikelyFrameworkValueTypeName(normalized);
    }

    public static string NormalizeTypeName(string typeName)
    {
        return typeName.Trim().TrimEnd('&');
    }

    public static string GetDisplayReason(string reason)
    {
        return reason switch
        {
            "ref_or_out_parameter" => "member has ref or out parameters",
            "byref_return" => "member returns by-reference data",
            "ambient_namespace_or_type" => "member belongs to an ambient framework namespace or type",
            "value_type_constructor_value_like_parameters" => "value-type constructor only uses value-like parameters",
            "metadata_constructor_without_body" => "metadata constructor has no body available",
            "void_returning_metadata_method" => "metadata method returns void",
            "mutating_method_name" => "member name suggests mutation",
            "reference_returning_instance_metadata_method" => "instance metadata method returns a reference-like value",
            "value_return_no_ref_or_out" => "member returns a value-like result without ref or out parameters",
            "metadata_method_shape_ambiguous" => "metadata method shape is ambiguous",
            "metadata_property_setter" => "metadata property only exposes a setter",
            "reference_returning_instance_metadata_property" =>
                "instance metadata property returns a reference-like value",
            "metadata_getter_value_like_return" => "metadata getter returns a value-like result",
            "mutable_metadata_field" => "metadata field is mutable",
            "readonly_reference_metadata_field" => "readonly metadata field returns a reference-like value",
            "readonly_metadata_field_value_like" => "readonly metadata field returns a value-like result",
            _ => reason.Replace('_', ' ')
        };
    }

    private static Classification ClassifyMethod(Shape shape)
    {
        if (shape.IsConstructor)
            return shape.HasValueTypeContainingType &&
                   shape.HasOnlyValueLikeOrReadOnlyViewParameters
                ? ProbablyPureBecause("value_type_constructor_value_like_parameters")
                : UnknownBecause("metadata_constructor_without_body");

        if (shape.ReturnsVoid) return ProbablyImpureBecause("void_returning_metadata_method");

        if (!IsKnownImmutableCollectionType(shape.TypeName) && HasMutatingName(shape.MemberName))
            return ProbablyImpureBecause("mutating_method_name");

        if (!shape.HasValueLikeReturn &&
            !shape.IsStatic)
            return UnknownBecause("reference_returning_instance_metadata_method");

        if (shape.HasOnlyValueLikeOrReadOnlyViewParameters) return ProbablyPureBecause("value_return_no_ref_or_out");

        return UnknownBecause("metadata_method_shape_ambiguous");
    }

    private static Classification ClassifyProperty(Shape shape)
    {
        if (shape.IsSetterOnlyProperty) return ProbablyImpureBecause("metadata_property_setter");

        if (!shape.HasValueLikeReturn)
            return UnknownBecause("reference_returning_instance_metadata_property");

        return ProbablyPureBecause("metadata_getter_value_like_return");
    }

    private static Classification ClassifyField(Shape shape)
    {
        if (!shape.IsReadOnlyField) return ProbablyImpureBecause("mutable_metadata_field");

        if (!shape.HasValueLikeReturn)
            return shape.IsStatic
                ? UnknownBecause("readonly_reference_metadata_field")
                : UnknownBecause("reference_returning_instance_metadata_field");

        return ProbablyPureBecause("readonly_metadata_field_value_like");
    }

    private static bool IsAmbientNamespaceOrType(string namespaceName, string typeName)
    {
        if (Constants.KnownImpureNamespaces.Any(known =>
                namespaceName.Equals(known, StringComparison.Ordinal) ||
                namespaceName.StartsWith(known + ".", StringComparison.Ordinal)))
            return true;

        return typeName is
            "System.Console" or
            "System.Environment" or
            "System.Diagnostics.Process" or
            "System.Random" or
            "System.GC" or
            "Microsoft.Win32.Registry";
    }

    private static bool IsKnownImmutableCollectionType(string typeName)
    {
        var normalized = NormalizeTypeName(typeName);
        return normalized.StartsWith("System.Collections.Immutable.", StringComparison.Ordinal) &&
               normalized.IndexOf(".Builder", StringComparison.Ordinal) < 0;
    }

    private static bool HasMutatingName(string name)
    {
        return StartsWithAny(
                   name,
                   "Add",
                   "Append",
                   "Clear",
                   "Close",
                   "Create",
                   "Delete",
                   "Ensure",
                   "Insert",
                   "Load",
                   "Move",
                   "Open",
                   "Read",
                   "Receive",
                   "Register",
                   "Remove",
                   "Replace",
                   "Reset",
                   "Run",
                   "Save",
                   "Send",
                   "Set",
                   "Sort",
                   "Start",
                   "Stop",
                   "Throw",
                   "Write") ||
               name.Equals("Dispose", StringComparison.Ordinal);
    }

    private static bool IsKnownPrimitiveOrValueAlias(string typeName)
    {
        return GetKnownPrimitiveOrValueAliases().Contains(typeName);
    }

    private static ImmutableHashSet<string> GetKnownPrimitiveOrValueAliases()
    {
        return KnownPrimitiveOrValueAliases.Value;
    }

    private static bool IsLikelyFrameworkValueTypeName(string typeName)
    {
        return typeName.StartsWith("System.Nullable<", StringComparison.Ordinal) ||
               typeName.Equals("System.DateTime", StringComparison.Ordinal) ||
               typeName.Equals("System.DateTimeOffset", StringComparison.Ordinal) ||
               typeName.Equals("System.TimeSpan", StringComparison.Ordinal) ||
               typeName.Equals("System.Guid", StringComparison.Ordinal) ||
               typeName.Equals("System.Decimal", StringComparison.Ordinal) ||
               typeName.Equals("System.Range", StringComparison.Ordinal) ||
               typeName.Equals("System.Index", StringComparison.Ordinal) ||
               typeName.Equals("System.HashCode", StringComparison.Ordinal) ||
               typeName.StartsWith("System.ValueTuple<", StringComparison.Ordinal) ||
               typeName.StartsWith("System.Tuple<", StringComparison.Ordinal);
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
    {
        return prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static Classification ProbablyPureBecause(string reason)
    {
        return ClassificationBecause(ProbablyPure, "probably_pure", reason);
    }

    private static Classification ProbablyImpureBecause(string reason)
    {
        return ClassificationBecause(ProbablyImpure, "probably_impure", reason);
    }

    private static Classification UnknownBecause(string reason)
    {
        return ClassificationBecause(Unknown, "unknown", reason);
    }

    private static Classification ClassificationBecause(string guess, string category, string reason)
    {
        return new Classification(guess, "low", reason, "bcl_fallback_" + category);
    }

    public readonly struct Shape
    {
        public Shape(
            string namespaceName,
            string typeName,
            string memberName,
            bool isFrameworkMetadataSymbol,
            bool isProperty,
            bool isField,
            bool isConstructor,
            bool isStatic,
            bool returnsVoid,
            bool returnsByRef,
            bool hasRefOrOutParameter,
            bool hasValueLikeReturn,
            bool hasValueTypeContainingType,
            bool hasOnlyValueLikeOrReadOnlyViewParameters,
            bool isSetterOnlyProperty,
            bool isReadOnlyField = false)
        {
            NamespaceName = namespaceName ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            IsFrameworkMetadataSymbol = isFrameworkMetadataSymbol;
            IsProperty = isProperty;
            IsField = isField;
            IsConstructor = isConstructor;
            IsStatic = isStatic;
            ReturnsVoid = returnsVoid;
            ReturnsByRef = returnsByRef;
            HasRefOrOutParameter = hasRefOrOutParameter;
            HasValueLikeReturn = hasValueLikeReturn;
            HasValueTypeContainingType = hasValueTypeContainingType;
            HasOnlyValueLikeOrReadOnlyViewParameters = hasOnlyValueLikeOrReadOnlyViewParameters;
            IsSetterOnlyProperty = isSetterOnlyProperty;
            IsReadOnlyField = isReadOnlyField;
        }

        public string NamespaceName { get; }
        public string TypeName { get; }
        public string MemberName { get; }
        public bool IsFrameworkMetadataSymbol { get; }
        public bool IsProperty { get; }
        public bool IsField { get; }
        public bool IsConstructor { get; }
        public bool IsStatic { get; }
        public bool ReturnsVoid { get; }
        public bool ReturnsByRef { get; }
        public bool HasRefOrOutParameter { get; }
        public bool HasValueLikeReturn { get; }
        public bool HasValueTypeContainingType { get; }
        public bool HasOnlyValueLikeOrReadOnlyViewParameters { get; }
        public bool IsSetterOnlyProperty { get; }
        public bool IsReadOnlyField { get; }
    }

    public readonly struct Classification
    {
        public Classification(string guess, string confidence, string reason, string category)
        {
            Guess = guess;
            Confidence = confidence;
            Reason = reason;
            Category = category;
        }

        public string Guess { get; }
        public string Confidence { get; }
        public string Reason { get; }
        public string Category { get; }
    }

    private static class KnownPrimitiveOrValueAliases
    {
        public static readonly ImmutableHashSet<string> Value = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "bool",
            "byte",
            "char",
            "decimal",
            "double",
            "float",
            "int",
            "long",
            "nint",
            "nuint",
            "sbyte",
            "short",
            "uint",
            "ulong",
            "ushort",
            "void",
            "System.Boolean",
            "System.Byte",
            "System.Char",
            "System.Decimal",
            "System.Double",
            "System.Int16",
            "System.Int32",
            "System.Int64",
            "System.IntPtr",
            "System.SByte",
            "System.Single",
            "System.UInt16",
            "System.UInt32",
            "System.UInt64",
            "System.UIntPtr",
            "System.Void");
    }
}
