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
// SharpProof.Worker.Tests' TypeInitializerAdmissionWaveTests record these on
// the supported runtime.
internal static partial class CSharpOperationSemantics
{
    private const int MaximumSearchValuesLength = 1 << 16;

    private const string SearchValuesCreate = "M:System.Buffers.SearchValues.Create(System.ReadOnlySpan{System.Char})";

    // The class of a framework value named above, or null for any other
    // operation.
    internal static InitializationEffect? FrameworkInitializerValue(IOperation value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        switch (value)
        {
            case IFieldReferenceOperation { Instance: null, Field: { IsStatic: true, Name: "Empty", ContainingType.SpecialType: SpecialType.System_String } field }
                when IsFrameworkSymbol(field):
                return InitializationEffect.None;
            // The platform's line separator, a literal of the framework.
            case IPropertyReferenceOperation { Instance: null, Property: { IsStatic: true, Name: "NewLine" } property }
                when IsFrameworkSymbol(property) && property.ContainingType.GetDocumentationCommentId() == "T:System.Environment":
                return InitializationEffect.None;
            // One cached empty sequence per element type, as Array.Empty.
            case IInvocationOperation { Instance: null, Arguments.IsEmpty: true, TargetMethod: { Name: "Empty", IsGenericMethod: true } empty }
                when IsFrameworkSymbol(empty) && empty.ContainingType.GetDocumentationCommentId() == "T:System.Linq.Enumerable":
                return InitializationEffect.FreshValues;
            case IFieldReferenceOperation { Instance: null, Field: { IsStatic: true, Name: "EmptyTypes" } field }
                when IsFrameworkSymbol(field) && field.ContainingType.GetDocumentationCommentId() == "T:System.Type":
                return InitializationEffect.FreshValues;
            case IPropertyReferenceOperation when OrdinalStringComparer(value) != null:
                return InitializationEffect.FreshValues;
            case IObjectCreationOperation { Initializer: null, Arguments.IsEmpty: true, Constructor: { Parameters.IsEmpty: true } constructor }
                when IsFrameworkSymbol(constructor) &&
                    constructor.ContainingType.GetDocumentationCommentId() == "T:System.Globalization.IdnMapping":
                return InitializationEffect.FreshValues;
            case IInvocationOperation { Instance: null, TargetMethod: { } method, Arguments.Length: 1 } invocation
                when IsFrameworkSymbol(method) && method.OriginalDefinition.GetDocumentationCommentId() == SearchValuesCreate &&
                    invocation.Arguments[0].ArgumentKind == ArgumentKind.Explicit && IsConstantSpan(invocation.Arguments[0].Value):
                return InitializationEffect.FreshValues;
            default:
                return null;
        }
    }

    // `"..."` converted to ReadOnlySpan<char> by the framework conversion.
    private static bool IsConstantSpan(IOperation value)
    {
        // Before C# 14 the conversion calls String's implicit operator; C# 14
        // makes it a built-in span conversion that the compiler emits as the
        // same call.
        return value is IConversionOperation
        {
            Operand.ConstantValue: { HasValue: true, Value: string { Length: <= MaximumSearchValuesLength } },
            Type: INamedTypeSymbol { Name: "ReadOnlySpan", Arity: 1 } span
        } conversion && conversion.Conversion.IsImplicit &&
            span.TypeArguments[0].SpecialType == SpecialType.System_Char && IsFrameworkSymbol(span.OriginalDefinition) &&
            (conversion.OperatorMethod is null || conversion.OperatorMethod is { Name: "op_Implicit", ContainingType.SpecialType: SpecialType.System_String } method &&
                IsFrameworkSymbol(method));
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
            "System.Runtime" => identity.PublicKeyToken.SequenceEqual(MicrosoftToken),
            "System.Linq" => identity.PublicKeyToken.SequenceEqual(MicrosoftToken),
            // SearchValues' reference assembly.
            "System.Memory" => identity.PublicKeyToken.SequenceEqual(NetStandardToken),
            "netstandard" => identity.PublicKeyToken.SequenceEqual(NetStandardToken),
            _ => false
        };
    }
}
