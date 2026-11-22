namespace SharpProof.Frontend;

// Framework values a type initializer may store without an effect a body can
// observe. Each is read or made by framework code that calls no user code,
// reads no culture or other ambient state, and cannot throw except by running
// out of memory.
//
// - `string.Empty` is the empty string. The runtime sets it before any
//   managed code runs, so reading it runs no initializer and allocates
//   nothing.
// - `Environment.NewLine` returns a string literal of the platform build.
// - `Enumerable.Empty<T>()` returns one cached empty sequence per T, made by
//   a framework initializer that runs no code of T.
// - `Type.EmptyTypes` is a zero-length array that the framework initializer
//   of Type makes, together with its other constants and filter delegates.
// - `StringComparer.Ordinal` and `OrdinalIgnoreCase` return comparers that
//   their framework initializer makes (see the collection rules).
// - `new IdnMapping()` runs an empty constructor. Its culture-independent
//   settings are plain fields; only GetAscii and GetUnicode consult
//   normalization data, and the initializer calls neither.
// - `SearchValues.Create(ReadOnlySpan<char>)` over a constant string only
//   reads those characters and builds a lookup object. The implicit
//   conversion from a non-null string to a span cannot throw. Which
//   implementation it picks depends on the characters and on the hardware,
//   never on state a program can change.
//
// Each member is bound by identity, not by name: its type must be the one the
// compilation resolves for that metadata name, which is none when two
// references declare it, and must come from a framework assembly. A
// referenced assembly that declares its own System.Environment, say, makes
// the name ambiguous, and the value stays an entry effect.
//
// SharpProof.Worker.Tests' TypeInitializerEffectTests and
// SharpProof.Frontend.Tests' TypeInitializerAdmissionTests record these.
internal static partial class CSharpOperationSemantics
{
    private const int MaximumSearchValuesLength = 1 << 16;

    private const string SearchValuesCreate = "M:System.Buffers.SearchValues.Create(System.ReadOnlySpan{System.Char})";

    // The class of a framework value named above, or null for any other
    // operation.
    internal static InitializationEffect? FrameworkInitializerValue(IOperation value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        var compilation = value.SemanticModel?.Compilation;
        switch (value)
        {
            case IFieldReferenceOperation { Instance: null, Field: { IsStatic: true, Name: "Empty" } field }
                when IsBoundFrameworkType(field.ContainingType, "System.String", compilation):
                return InitializationEffect.None;
            // The platform's line separator, a literal of the framework.
            case IPropertyReferenceOperation { Instance: null, Property: { IsStatic: true, Name: "NewLine" } property }
                when IsBoundFrameworkType(property.ContainingType, "System.Environment", compilation):
                return InitializationEffect.None;
            // One cached empty sequence per element type, as Array.Empty.
            case IInvocationOperation
            {
                Instance: null, Arguments.IsEmpty: true, TargetMethod: { Name: "Empty", IsGenericMethod: true, Parameters.IsEmpty: true } empty
            } when IsBoundFrameworkType(empty.ContainingType, "System.Linq.Enumerable", compilation):
                return InitializationEffect.FreshValues;
            case IFieldReferenceOperation { Instance: null, Field: { IsStatic: true, Name: "EmptyTypes" } field }
                when IsBoundFrameworkType(field.ContainingType, "System.Type", compilation):
                return InitializationEffect.FreshValues;
            case IPropertyReferenceOperation when OrdinalStringComparer(value) != null:
                return InitializationEffect.FreshValues;
            case IObjectCreationOperation { Initializer: null, Arguments.IsEmpty: true, Constructor: { Parameters.IsEmpty: true } constructor }
                when IsBoundFrameworkType(constructor.ContainingType, "System.Globalization.IdnMapping", compilation):
                return InitializationEffect.FreshValues;
            case IInvocationOperation { Instance: null, TargetMethod: { } method, Arguments.Length: 1 } invocation
                when IsBoundFrameworkType(method.ContainingType, "System.Buffers.SearchValues", compilation) &&
                    method.OriginalDefinition.GetDocumentationCommentId() == SearchValuesCreate &&
                    invocation.Arguments[0].ArgumentKind == ArgumentKind.Explicit && IsConstantSpan(invocation.Arguments[0].Value, compilation):
                return InitializationEffect.FreshValues;
            default:
                return null;
        }
    }

    // `"..."` converted to ReadOnlySpan<char> by the framework conversion.
    private static bool IsConstantSpan(IOperation value, Compilation? compilation)
    {
        // Before C# 14 the conversion calls String's implicit operator; C# 14
        // makes it a built-in span conversion that the compiler emits as the
        // same call.
        return value is IConversionOperation
        {
            Operand.ConstantValue: { HasValue: true, Value: string { Length: <= MaximumSearchValuesLength } },
            Type: INamedTypeSymbol { Arity: 1 } span
        } conversion && conversion.Conversion.IsImplicit &&
            span.TypeArguments[0].SpecialType == SpecialType.System_Char && IsBoundFrameworkType(span, "System.ReadOnlySpan`1", compilation) &&
            (conversion.OperatorMethod is null || conversion.OperatorMethod is { Name: "op_Implicit" } method &&
                IsBoundFrameworkType(method.ContainingType, "System.String", compilation));
    }

    // The framework type the compilation binds to metadataName.
    internal static bool IsBoundFrameworkType(ITypeSymbol? type, string metadataName, Compilation? compilation)
    {
        metadataName = RepositoryParameterGuard.NotNull(metadataName, nameof(metadataName));
        return type is INamedTypeSymbol named && compilation?.GetTypeByMetadataName(metadataName) is { } bound &&
            SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, bound) && IsFrameworkSymbol(bound);
    }

    // A top-level type's metadata name with its namespace.
    private static string MetadataFullName(INamedTypeSymbol type)
    {
        var name = type.MetadataName;
        for (var space = type.ContainingNamespace; space is { IsGlobalNamespace: false }; space = space.ContainingNamespace)
        { name = space.Name + "." + name; }
        return name;
    }

    private static bool IsFrameworkSymbol(ISymbol symbol)
    {
        if (!symbol.DeclaringSyntaxReferences.IsEmpty)
        { return false; }
        var identity = symbol.ContainingAssembly.Identity;
        return identity.Name switch
        {
            "System.Private.CoreLib" => identity.PublicKeyToken.SequenceEqual(CoreLibToken),
            "mscorlib" => identity.PublicKeyToken.SequenceEqual(EcmaToken),
            "System.Runtime" or "System.Linq" or "System.Collections" or "System.Collections.Concurrent" =>
                identity.PublicKeyToken.SequenceEqual(MicrosoftToken),
            // SearchValues' reference assembly.
            "System.Memory" => identity.PublicKeyToken.SequenceEqual(NetStandardToken),
            "netstandard" => identity.PublicKeyToken.SequenceEqual(NetStandardToken),
            _ => false
        };
    }
}
