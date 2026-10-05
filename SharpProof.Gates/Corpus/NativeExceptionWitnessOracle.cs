using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Gates.Corpus;

// Execute only concrete, static witnesses whose original IR already terminated.
// Unsupported invocation shapes are explicit gaps, never oracle confirmations.
internal sealed class NativeExceptionWitnessOracle(CSharpCompilation compilation) : IDisposable
{
    private AssemblyLoadContext? _context;
    private Assembly? _assembly;

    internal string Check(IMethodSymbol method, CompilerTotalCallablePreparation preparation,
        ImmutableDictionary<IrVarId, IrValue> inputs, IrExceptionInfo expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!method.IsStatic || method.Arity != 0 || preparation.Parameters.Length != method.Parameters.Length ||
            method.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
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
            if (type == null || !TryValue(method.Parameters[ordinal].Type, inputs[preparation.Parameters[ordinal].Entry], out values[ordinal]))
            { return "NonConcreteInput"; }
            types[ordinal] = type;
        }
        EnsureAssembly(cancellationToken);
        var declaring = _assembly!.GetType(MetadataName(method.ContainingType), throwOnError: true)!;
        var callable = declaring.GetMethod(method.MetadataName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, types, modifiers: null) ?? throw new InvalidDataException("A source witness has no matching emitted method.");
        try
        {
            callable.Invoke(null, values);
            return "Contradiction";
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            return IrExceptionKindFacts.FromException(exception.InnerException) == expected.Kind ? "Confirmed" : "Contradiction";
        }
    }

    private void EnsureAssembly(CancellationToken cancellationToken)
    {
        if (_assembly != null)
        { return; }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidDataException("The source oracle did not compile: " + string.Join("; ", emitted.Diagnostics.Take(5))); }
        image.Position = 0;
        _context = new AssemblyLoadContext("SharpProof.NativeExceptionOracle." + Guid.NewGuid().ToString("N"), isCollectible: true);
        _assembly = _context.LoadFromStream(image);
    }

    private static string MetadataName(INamedTypeSymbol type)
    {
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
        var numeric = value.IntegerNumericValue;
        result = type.SpecialType switch
        {
            SpecialType.System_SByte => (sbyte)numeric,
            SpecialType.System_Byte => (byte)numeric,
            SpecialType.System_Int16 => (short)numeric,
            SpecialType.System_UInt16 => (ushort)numeric,
            SpecialType.System_Char => (char)(ushort)numeric,
            SpecialType.System_Int32 => (int)numeric,
            SpecialType.System_UInt32 => (uint)numeric,
            SpecialType.System_Int64 => (long)numeric,
            SpecialType.System_UInt64 => (ulong)numeric,
            _ => null
        };
        return result != null;
    }

    public void Dispose()
    { _context?.Unload(); }
}
