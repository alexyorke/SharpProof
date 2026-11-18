namespace SharpProof.Frontend;

// Framework collections a type initializer may make as fresh values. Each
// admitted constructor and Add only allocates and stores: it calls no user
// code and cannot throw except by running out of memory.
//
// - The parameterless and capacity constructors of List, Dictionary,
//   HashSet, Queue and Stack, and the parameterless ConcurrentDictionary
//   constructor, only allocate storage. For a reference-type key, the
//   Dictionary, HashSet and ConcurrentDictionary constructors read
//   EqualityComparer<T>.Default, whose framework initializer picks a
//   framework comparer by reflection and calls no member of T. A negative
//   capacity throws, so a capacity must be a constant from 0 to 65,536.
// - A comparer argument must be StringComparer.Ordinal or OrdinalIgnoreCase.
//   Those properties return framework instances that their framework
//   initializer makes. The constructors store the comparer, or compare it by
//   reference with the framework's own comparers, and call none of its
//   members. The culture comparers are excluded: CurrentCulture reads the
//   ambient culture, which may be a user subtype, and the invariant ones
//   load culture data.
// - A collection initializer calls Add, or for a Dictionary may set the
//   indexer. List.Add stores any value. HashSet.Add and Dictionary's Add and
//   indexer hash their key, so the key must be a non-null constant bool,
//   char, integer, string or enum: the default comparer for such a type, and
//   the ordinal comparers, are framework code. OrdinalIgnoreCase keys must be
//   ASCII, which every runtime folds the same way without culture data.
//   Dictionary.Add throws for a key the dictionary already holds, so its keys
//   must differ under the comparer.
//
// SharpProof.Worker.Tests' CollectionInitializerTests records these on the
// supported runtime.
internal static partial class CSharpOperationSemantics
{
    // As for arrays, a larger collection could fail with
    // OutOfMemoryException.
    private const int MaximumInitializerCollectionSize = 1 << 16;

    private enum KeyComparison
    {
        Default,
        Ordinal,
        OrdinalIgnoreCase
    }

    private static readonly ImmutableHashSet<string> InitializerCollectionConstructors = ImmutableHashSet.Create(StringComparer.Ordinal,
        "M:System.Collections.Generic.List`1.#ctor", "M:System.Collections.Generic.List`1.#ctor(System.Int32)",
        "M:System.Collections.Generic.Queue`1.#ctor", "M:System.Collections.Generic.Queue`1.#ctor(System.Int32)",
        "M:System.Collections.Generic.Stack`1.#ctor", "M:System.Collections.Generic.Stack`1.#ctor(System.Int32)",
        "M:System.Collections.Generic.Dictionary`2.#ctor", "M:System.Collections.Generic.Dictionary`2.#ctor(System.Int32)",
        "M:System.Collections.Generic.Dictionary`2.#ctor(System.Collections.Generic.IEqualityComparer{`0})",
        "M:System.Collections.Generic.Dictionary`2.#ctor(System.Int32,System.Collections.Generic.IEqualityComparer{`0})",
        "M:System.Collections.Generic.HashSet`1.#ctor", "M:System.Collections.Generic.HashSet`1.#ctor(System.Int32)",
        "M:System.Collections.Generic.HashSet`1.#ctor(System.Collections.Generic.IEqualityComparer{`0})",
        "M:System.Collections.Generic.HashSet`1.#ctor(System.Int32,System.Collections.Generic.IEqualityComparer{`0})",
        "M:System.Collections.Concurrent.ConcurrentDictionary`2.#ctor",
        "M:System.Collections.Concurrent.ConcurrentDictionary`2.#ctor(System.Collections.Generic.IEqualityComparer{`0})");

    private const string ListAdd = "M:System.Collections.Generic.List`1.Add(`0)";
    private const string HashSetAdd = "M:System.Collections.Generic.HashSet`1.Add(`0)";
    private const string DictionaryAdd = "M:System.Collections.Generic.Dictionary`2.Add(`0,`1)";
    private const string DictionaryIndexer = "P:System.Collections.Generic.Dictionary`2.Item(`0)";

    // A `new` of one of the collections above. values receives the values it
    // stores other than its keys, which the caller must still admit; keys are
    // checked here.
    internal static bool IsCallbackFreeCollectionCreation(IObjectCreationOperation creation, out ImmutableArray<IOperation> values)
    {
        creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
        values = ImmutableArray<IOperation>.Empty;
        if (creation.Constructor is not { } constructor || creation.Type is not INamedTypeSymbol { TypeArguments.IsEmpty: false } type ||
            !IsInitializerCollectionMember(constructor) ||
            !InitializerCollectionConstructors.Contains(constructor.OriginalDefinition.GetDocumentationCommentId() ?? ""))
        { return false; }
        var comparison = KeyComparison.Default;
        foreach (var argument in creation.Arguments)
        {
            if (argument.ArgumentKind != ArgumentKind.Explicit || argument.Parameter is not { RefKind: RefKind.None } parameter)
            { return false; }
            if (parameter.Type.SpecialType == SpecialType.System_Int32)
            {
                if (argument.Value.ConstantValue is not { HasValue: true, Value: int capacity } ||
                    capacity < 0 || capacity > MaximumInitializerCollectionSize)
                { return false; }
            }
            else if (OrdinalStringComparer(argument.Value) is { } named && type.TypeArguments[0].SpecialType == SpecialType.System_String)
            { comparison = named; }
            else
            { return false; }
        }
        return creation.Initializer == null || Entries(creation.Initializer, type, comparison, out values);
    }

    // The framework assemblies that define these collections.
    // ConcurrentDictionary has its own reference and implementation assembly
    // on .NET.
    private static bool IsInitializerCollectionMember(IMethodSymbol method)
    {
        if (IsFrameworkCollectionMember(method))
        { return true; }
        var identity = method.ContainingAssembly.Identity;
        return method.DeclaringSyntaxReferences.IsEmpty && identity.Name == "System.Collections.Concurrent" &&
            identity.PublicKeyToken.SequenceEqual(MicrosoftToken);
    }

    private static KeyComparison? OrdinalStringComparer(IOperation value)
    {
        while (value is IConversionOperation { OperatorMethod: null, IsTryCast: false } conversion &&
            (conversion.Conversion.IsIdentity || conversion.Conversion.IsReference && conversion.Conversion.IsImplicit))
        { value = conversion.Operand; }
        if (value is not IPropertyReferenceOperation
            {
                Instance: null, Arguments.IsEmpty: true,
                Property: { IsStatic: true, GetMethod: { } getter } property
            } || !IsFrameworkCollectionMember(getter) ||
            property.ContainingType.GetDocumentationCommentId() != "T:System.StringComparer")
        { return null; }
        return property.Name switch
        {
            "Ordinal" => KeyComparison.Ordinal,
            "OrdinalIgnoreCase" => KeyComparison.OrdinalIgnoreCase,
            _ => null
        };
    }

    private static bool Entries(IObjectOrCollectionInitializerOperation initializer, INamedTypeSymbol type, KeyComparison comparison,
        out ImmutableArray<IOperation> values)
    {
        values = ImmutableArray<IOperation>.Empty;
        if (initializer.Initializers.Length > MaximumInitializerCollectionSize)
        { return false; }
        var keys = new HashSet<object>();
        var stored = ImmutableArray.CreateBuilder<IOperation>();
        foreach (var entry in initializer.Initializers)
        {
            switch (entry)
            {
                case IInvocationOperation
                {
                    Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver },
                    TargetMethod: { IsStatic: false, IsVirtual: false } add
                } invocation when IsFrameworkCollectionMember(add) && PositionalArguments(invocation.Arguments):
                    var id = add.OriginalDefinition.GetDocumentationCommentId();
                    var arguments = invocation.Arguments;
                    // HashSet.Add keeps the set as it is for a key it holds;
                    // Dictionary.Add throws.
                    var admitted = id == ListAdd ? arguments.Length == 1
                        : id == HashSetAdd ? arguments.Length == 1 && Key(arguments[0].Value, type.TypeArguments[0], comparison) != null
                        : id == DictionaryAdd && arguments.Length == 2 &&
                            Key(arguments[0].Value, type.TypeArguments[0], comparison) is { } key && keys.Add(key);
                    if (!admitted)
                    { return false; }
                    if (id != HashSetAdd)
                    { stored.Add(arguments[arguments.Length - 1].Value); }
                    break;
                // The indexer replaces the value a key holds.
                case ISimpleAssignmentOperation
                {
                    IsRef: false,
                    Target: IPropertyReferenceOperation
                    {
                        Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver },
                        Property: { IsStatic: false, IsVirtual: false, SetMethod: { } setter } indexer,
                        Arguments.Length: 1
                    } target
                } assignment when IsFrameworkCollectionMember(setter) && PositionalArguments(target.Arguments) &&
                    indexer.OriginalDefinition.GetDocumentationCommentId() == DictionaryIndexer &&
                    Key(target.Arguments[0].Value, type.TypeArguments[0], comparison) is { } replaced:
                    keys.Add(replaced);
                    stored.Add(assignment.Value);
                    break;
                default:
                    return false;
            }
        }
        values = stored.ToImmutable();
        return true;
    }

    private static bool PositionalArguments(ImmutableArray<IArgumentOperation> arguments)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] is not { ArgumentKind: ArgumentKind.Explicit, Parameter: { RefKind: RefKind.None } parameter } ||
                parameter.Ordinal != index)
            { return false; }
        }
        return true;
    }

    // The key as the comparer sees it, or null when hashing it is not known
    // to be callback-free and fault-free.
    private static object? Key(IOperation value, ITypeSymbol keyType, KeyComparison comparison)
    {
        if (value.ConstantValue is not { HasValue: true, Value: { } constant } || !IsHashedKeyType(keyType) ||
            !SymbolEqualityComparer.Default.Equals(value.Type, keyType))
        { return null; }
        if (comparison == KeyComparison.Default)
        { return constant; }
        if (constant is not string text)
        { return null; }
        if (comparison == KeyComparison.Ordinal)
        { return text; }
        var folded = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character > '\u007f')
            { return null; }
            folded.Append(character is >= 'a' and <= 'z' ? (char)(character - ('a' - 'A')) : character);
        }
        return folded.ToString();
    }

    private static bool IsHashedKeyType(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Enum || type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char or
            SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_String;
    }
}
