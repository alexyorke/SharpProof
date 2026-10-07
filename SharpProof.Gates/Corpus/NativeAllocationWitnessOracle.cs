using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Gates.Corpus;

internal sealed record AllocationOracleObservation(string RuntimeOracle, string IlOracle, int RuntimeChecks, long? AllocatedBytes);

// All argument materialization, delegate construction and warmup occur outside
// the measured calls. The source image does not derive from the candidate IR.
internal sealed class NativeAllocationWitnessOracle(CSharpCompilation compilation) : IDisposable
{
    private readonly Dictionary<OptimizationLevel, (AssemblyLoadContext Context, Assembly Assembly)> _assemblies = [];

    internal AllocationOracleObservation Check(IMethodSymbol method, CompilerTotalCallablePreparation preparation,
        PassiveCallableCheckResult evidence, WorkerClaimOutcome outcome, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!evidence.HasFeasibleEntryWitness)
        { return Gap("NoFeasibleEntryWitness"); }
        if (!method.IsStatic || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None) ||
            preparation.Parameters.Length != method.Parameters.Length)
        { return Gap("UnsupportedInvocationShape"); }
        var inputs = evidence.EntryModel;
        var initial = new Dictionary<IrVarId, IrValue>();
        foreach (var parameter in preparation.Parameters)
        {
            if (!inputs.TryGetValue(parameter.Entry, out var value))
            { return Gap("NonConcreteInput"); }
            initial[parameter.Entry] = value;
            initial[parameter.Current] = value;
        }
        var replay = new IrProgramInterpreter(preparation.Program.Factory).Execute(preparation.Program, initial,
            PassiveCallableVcBuilder.MaximumSteps, cancellationToken: cancellationToken);
        if (replay.ConsumedApproximation || replay.Status is not (IrProgramExecutionStatus.Returned or IrProgramExecutionStatus.Exception))
        { return Gap("NonTerminatingOrIncompleteWitness"); }
        var assembly = EnsureAssembly(outcome, cancellationToken);
        var definition = assembly.GetType(MetadataName(method.ContainingType), throwOnError: true)!;
        var generic = method.Arity != 0 || definition.ContainsGenericParameters;
        var choices = generic ? new[] { typeof(int), typeof(string) } : new[] { typeof(int) };
        var checks = 0;
        long maximumBytes = 0;
        var il = "NoReachableAllocationOpcode";
        var skipped = "UnboundGenericConstraint";
        foreach (var choice in choices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryBind(method, definition, choice, out var callable))
            { continue; }
            var parameters = callable!.GetParameters();
            var values = new object?[parameters.Length];
            var aliases = new Dictionary<object, object>();
            var concrete = true;
            // Materialize nominal objects before object-typed aliases.
            foreach (var ordinal in Enumerable.Range(0, parameters.Length).OrderBy(index => parameters[index].ParameterType == typeof(object)))
            {
                if (!TryValue(parameters[ordinal].ParameterType, inputs[preparation.Parameters[ordinal].Entry], aliases, out values[ordinal]))
                { concrete = false; break; }
            }
            if (!concrete)
            { skipped = "NonConcreteInput"; continue; }
            var run = CreateInvocation(callable);
            for (var repeat = 0; repeat < 3; repeat++)
            { Invoke(run, values); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var repeat = 0; repeat < 32; repeat++)
            { Invoke(run, values); }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            maximumBytes = Math.Max(maximumBytes, bytes);
            checks++;
            if (HasReachableAllocationOpcode(callable))
            { il = "PotentialAllocationOpcode"; }
            if (outcome == WorkerClaimOutcome.Proven && bytes != 0)
            { return new("Contradiction", il, checks, bytes); }
        }
        if (checks == 0)
        { return Gap(skipped); }
        return new(outcome == WorkerClaimOutcome.Proven || maximumBytes > 0 ? "Confirmed" : "NoObservedAllocation", il, checks, maximumBytes);
    }

    private Assembly EnsureAssembly(WorkerClaimOutcome outcome, CancellationToken cancellationToken)
    {
        // Positive checks must execute the same compilation that was proved.
        // Negative observations retain the separate Debug policy that preserves
        // source allocation sites; they do not confirm Release emission behavior.
        var optimization = outcome == WorkerClaimOutcome.Proven ? compilation.Options.OptimizationLevel : OptimizationLevel.Debug;
        if (_assemblies.TryGetValue(optimization, out var existing))
        { return existing.Assembly; }
        using var image = new MemoryStream();
        var source = optimization == compilation.Options.OptimizationLevel ? compilation :
            compilation.WithOptions(compilation.Options.WithOptimizationLevel(optimization));
        var emitted = source.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidDataException("The allocation oracle source did not compile: " + string.Join("; ", emitted.Diagnostics.Take(5))); }
        image.Position = 0;
        var context = new AssemblyLoadContext("SharpProof.NativeAllocationOracle." + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            _assemblies.Add(optimization, (context, assembly));
            return assembly;
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    private static bool TryBind(IMethodSymbol symbol, Type definition, Type choice, out MethodInfo? callable)
    {
        callable = null;
        try
        {
            var owner = definition.ContainsGenericParameters
                ? definition.MakeGenericType(Enumerable.Repeat(choice, definition.GetGenericArguments().Length).ToArray()) : definition;
            foreach (var candidate in owner.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (candidate.Name != symbol.MetadataName || candidate.GetGenericArguments().Length != symbol.Arity)
                { continue; }
                var closed = candidate.IsGenericMethodDefinition
                    ? candidate.MakeGenericMethod(Enumerable.Repeat(choice, symbol.Arity).ToArray()) : candidate;
                var parameters = closed.GetParameters();
                if (parameters.Length == symbol.Parameters.Length && parameters.Select((parameter, ordinal) =>
                        Matches(symbol.Parameters[ordinal].Type, parameter.ParameterType, choice)).All(match => match))
                {
                    if (callable != null)
                    { throw new InvalidDataException("A source allocation witness has an ambiguous emitted method."); }
                    callable = closed;
                }
            }
            return callable != null;
        }
        catch (ArgumentException)
        { return false; }
    }

    private static bool Matches(ITypeSymbol symbol, Type runtime, Type choice)
    {
        if (symbol is ITypeParameterSymbol)
        { return runtime == choice; }
        if (symbol is IArrayTypeSymbol array)
        { return runtime.IsArray && runtime.GetArrayRank() == array.Rank && Matches(array.ElementType, runtime.GetElementType()!, choice); }
        if (symbol is not INamedTypeSymbol named)
        { return false; }
        var definition = runtime.IsGenericType ? runtime.GetGenericTypeDefinition() : runtime;
        if (MetadataName(named) != definition.FullName)
        { return false; }
        var assembly = definition.Assembly.GetName();
        if (named.SpecialType == SpecialType.None && (named.ContainingAssembly.Identity.Name != assembly.Name ||
            named.ContainingAssembly.Identity.Version != assembly.Version ||
            !named.ContainingAssembly.Identity.PublicKeyToken.SequenceEqual(assembly.GetPublicKeyToken() ?? [])))
        { return false; }
        var arguments = new Stack<INamedTypeSymbol>();
        for (var current = named; current != null; current = current.ContainingType)
        { arguments.Push(current); }
        var symbols = arguments.SelectMany(owner => owner.TypeArguments).ToArray();
        var types = runtime.GetGenericArguments();
        return symbols.Length == types.Length && symbols.Select((argument, ordinal) => Matches(argument, types[ordinal], choice)).All(match => match);
    }

    private static bool TryValue(Type type, IrValue value, Dictionary<object, object> aliases, out object? result)
    {
        result = null;
        if (value.Kind == IrValueKind.Null)
        { return !type.IsValueType; }
        if (value.Kind == IrValueKind.Boolean && type == typeof(bool))
        { result = value.Boolean; return true; }
        if (value.Kind == IrValueKind.String && type == typeof(string))
        { result = value.String; return true; }
        if (value.Kind == IrValueKind.Reference && !type.IsValueType && !type.IsAbstract && !type.IsInterface && !typeof(Delegate).IsAssignableFrom(type))
        {
            if (!aliases.TryGetValue(value.Reference, out var reference))
            { reference = RuntimeHelpers.GetUninitializedObject(type); aliases.Add(value.Reference, reference); }
            result = reference;
            return type.IsInstanceOfType(result);
        }
        if (value.Kind != IrValueKind.Integer)
        { return false; }
        var numeric = value.IntegerNumericValue;
        result = type == typeof(sbyte) ? (object)(sbyte)numeric : type == typeof(byte) ? (byte)numeric :
            type == typeof(short) ? (short)numeric : type == typeof(ushort) ? (ushort)numeric :
            type == typeof(char) ? (char)(ushort)numeric : type == typeof(int) ? (int)numeric :
            type == typeof(uint) ? (uint)numeric : type == typeof(long) ? (long)numeric :
            type == typeof(ulong) ? (ulong)numeric : null;
        return result != null;
    }

    private static Action<object?[]> CreateInvocation(MethodInfo callable)
    {
        var wrapper = new DynamicMethod("allocation-witness", typeof(void), [typeof(object[])], callable.Module, skipVisibility: true);
        var il = wrapper.GetILGenerator();
        var parameters = callable.GetParameters();
        for (var ordinal = 0; ordinal < parameters.Length; ordinal++)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, ordinal);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(parameters[ordinal].ParameterType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, parameters[ordinal].ParameterType);
        }
        il.Emit(OpCodes.Call, callable);
        if (callable.ReturnType != typeof(void))
        { il.Emit(OpCodes.Pop); }
        il.Emit(OpCodes.Ret);
        return wrapper.CreateDelegate<Action<object?[]>>();
    }

    private static void Invoke(Action<object?[]> run, object?[] values)
    { try { run(values); } catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException) { } }

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

    private static AllocationOracleObservation Gap(string reason)
    { return new(reason, "NotRun", 0, null); }

    private static bool HasReachableAllocationOpcode(MethodInfo method)
    { return AllocationIlOracle.HasPotentialAllocation(method); }

    public void Dispose()
    {
        foreach (var image in _assemblies.Values)
        { image.Context.Unload(); }
        _assemblies.Clear();
    }
}
