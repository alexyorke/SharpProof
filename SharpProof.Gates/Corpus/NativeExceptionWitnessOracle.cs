using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Worker;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;
using OperationId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrOperationTag>;

namespace SharpProof.Gates.Corpus;

// Execute only concrete, static witnesses whose original IR already terminated.
// Unsupported invocation shapes are explicit gaps, never oracle confirmations.
internal sealed class NativeExceptionWitnessOracle(CSharpCompilation compilation) : IDisposable
{
    private readonly CSharpCompilation _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
    private AssemblyLoadContext? _context;
    private Assembly? _assembly;

    internal string Check(IMethodSymbol method, CompilerTotalCallablePreparation preparation,
        ImmutableDictionary<IrVarId, IrValue> inputs, IrExceptionInfo expected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        if (!method.IsStatic || method.Arity != 0 || preparation.Parameters.Length != method.Parameters.Length ||
            method.Parameters.Any(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.RefKind != RefKind.None)))
        { return "UnsupportedInvocationShape"; }
        for (var owner = method.ContainingType; owner != null; owner = owner.ContainingType)
        {
            if (owner.Arity != 0)
            { return "UnboundGenericOwner"; }
            if (owner.StaticConstructors.Length != 0)
            { return "StaticInitializationNotModeled"; }
        }
        var values = new object?[method.Parameters.Length];
        var types = new Type[method.Parameters.Length];
        for (var ordinal = 0; ordinal < method.Parameters.Length; ordinal++)
        {
            var type = RuntimeType(method.Parameters[ordinal].Type);
            if (type == null || !inputs.TryGetValue(preparation.Parameters[ordinal].Entry, out var input) ||
                !TryValue(method.Parameters[ordinal].Type, input, out values[ordinal]))
            { return "NonConcreteInput"; }
            types[ordinal] = type;
        }
        EnsureAssembly(cancellationToken);
        var declaring = _assembly!.GetType(MetadataName(method.ContainingType), throwOnError: true)!;
        var callable = declaring.GetMethod(method.MetadataName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, types, modifiers: null) ?? throw new InvalidDataException("A source witness has no matching emitted method.");
        var actual = ObserveInvocationException(callable, values);
        var confirmed = actual != null && (expected.Kind == IrExceptionKind.Explicit && expected.Site is { } site
            ? MatchesExplicitException(preparation.Program.Factory, site, actual)
            : IrExceptionKindFacts.FromException(actual) == expected.Kind);
        return confirmed ? "Confirmed" : "Contradiction";
    }

    // Reports what the compiled invocation actually did, before matching native evidence.
    internal static Exception? ObserveInvocationException(MethodInfo callable, object?[] values)
    {
        System.ArgumentNullException.ThrowIfNull(values, nameof(values));
        System.ArgumentNullException.ThrowIfNull(callable);
        try
        {
            callable.Invoke(null, values);
            return null;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            return exception.InnerException;
        }
    }

    private bool MatchesExplicitException(IrFactory factory, OperationId site, Exception actual)
    {
        if (site.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(site));
        }
        System.ArgumentNullException.ThrowIfNull(actual);
        System.ArgumentNullException.ThrowIfNull(factory);
        var expectedType = ExplicitThrowSites.Types(factory, site).FirstOrDefault();
        var exact = ExplicitThrowSites.IsExact(factory, site);
        return MatchesExceptionType(compilation, expectedType, exact, actual);
    }

    internal static bool MatchesExceptionType(CSharpCompilation compilation, string? expectedType, bool exact, Exception actual)
    {
        System.ArgumentNullException.ThrowIfNull(actual);
        System.ArgumentNullException.ThrowIfNull(compilation);
        for (Type? runtimeType = actual.GetType(); runtimeType != null; runtimeType = exact ? null : runtimeType.BaseType)
        {
            if (runtimeType.FullName is not { } metadataName ||
                compilation.GetTypeByMetadataName(metadataName) is not { } sourceType ||
                !string.Equals(sourceType.ContainingAssembly.Identity.ToString(), runtimeType.Assembly.FullName, StringComparison.Ordinal))
            {
                continue;
            }
            if (string.Equals(CompilerIdentityBridge.CreateTypeDisplay(sourceType), expectedType, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private void EnsureAssembly(CancellationToken cancellationToken)
    {
        if (_assembly != null)
        { return; }
        using var image = NativeSourceEmission.EmitImage(
            _compilation, "The source oracle did not compile: ", cancellationToken);
        _context = new AssemblyLoadContext("SharpProof.NativeExceptionOracle." + Guid.NewGuid().ToString("N"), isCollectible: true);
        _assembly = _context.LoadFromStream(image);
    }

    private static string MetadataName(INamedTypeSymbol type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var names = new Stack<string>();
        for (var current = type; current != null; current = current.ContainingType)
        { names.Push(current.MetadataName); }
        var namespaces = new Stack<string>();
        for (var current = type.ContainingNamespace; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        { namespaces.Push(current.MetadataName); }
        return (namespaces.Count == 0 ? "" : string.Join(".", namespaces) + ".") + string.Join("+", names);
    }

    private static Type? RuntimeType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol { Rank: 1 } array)
        { return RuntimeType(array.ElementType)?.MakeArrayType(); }
        return type.SpecialType switch
        {
            SpecialType.System_Boolean => typeof(bool),
            SpecialType.System_SByte => typeof(sbyte),
            SpecialType.System_Byte => typeof(byte),
            SpecialType.System_Int16 => typeof(short),
            SpecialType.System_UInt16 => typeof(ushort),
            SpecialType.System_Char => typeof(char),
            SpecialType.System_Int32 => typeof(int),
            SpecialType.System_UInt32 => typeof(uint),
            SpecialType.System_Int64 => typeof(long),
            SpecialType.System_UInt64 => typeof(ulong),
            SpecialType.System_String => typeof(string),
            SpecialType.System_Object => typeof(object),
            _ => null
        };
    }

    private static bool TryValue(ITypeSymbol type, IrValue value, out object? result)
    {
        if (value.Kind == IrValueKind.Null && type.IsReferenceType)
        { result = null; return true; }
        if (value.Kind == IrValueKind.Boolean && type.SpecialType == SpecialType.System_Boolean)
        { result = value.Boolean; return true; }
        if (value.Kind == IrValueKind.String && type.SpecialType == SpecialType.System_String)
        { result = value.String; return true; }
        if (value.Kind != IrValueKind.Integer)
        { result = null; return false; }
        return NativeIntegerConversion.TryCompilerValue(type, value.IntegerNumericValue, out result);
    }

    public void Dispose()
    { _context?.Unload(); }
}
