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

internal sealed record AllocationOracleObservation(string RuntimeOracle, string IlOracle, int RuntimeChecks, long? AllocatedBytes)
{
    public string RuntimeOracle { get; init; } = RuntimeOracle ?? throw new System.ArgumentNullException(nameof(RuntimeOracle));
    public string IlOracle { get; init; } = IlOracle ?? throw new System.ArgumentNullException(nameof(IlOracle));
}

// All argument materialization, delegate construction and warmup occur outside
// the measured calls. The source image does not derive from the candidate IR.
internal sealed class NativeAllocationWitnessOracle(CSharpCompilation compilation) : IDisposable
{
    private readonly CSharpCompilation _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
    private ImmutableDictionary<OptimizationLevel, (AssemblyLoadContext Context, Assembly Assembly)> _assemblies = ImmutableDictionary<OptimizationLevel, (AssemblyLoadContext Context, Assembly Assembly)>.Empty;
    private readonly ImmutableArray<(AssemblyLoadContext Context, Assembly Assembly)>.Builder _assemblyOrder = ImmutableArray.CreateBuilder<(AssemblyLoadContext Context, Assembly Assembly)>();

    internal AllocationOracleObservation Check(IMethodSymbol method, CompilerTotalCallablePreparation preparation,
        PassiveCallableCheckResult evidence, WorkerClaimOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(evidence);
        cancellationToken.ThrowIfCancellationRequested();
        if (!evidence.HasFeasibleEntryWitness)
        { return Gap("NoFeasibleEntryWitness"); }
        if (!method.IsStatic || method.Parameters.Any(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.RefKind != RefKind.None)) ||
            preparation.Parameters.Length != method.Parameters.Length)
        { return Gap("UnsupportedInvocationShape"); }
        var inputs = evidence.EntryModel;
        var initialBuilder = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        foreach (var parameter in preparation.Parameters)
        {
            var value = inputs[parameter.Entry];
            initialBuilder[parameter.Entry] = value;
            initialBuilder[parameter.Current] = value;
        }
        var initial = initialBuilder.ToImmutable();
        var replay = new IrProgramInterpreter(preparation.Program.Factory).Execute(preparation.Program, initial,
            PassiveCallableVcBuilder.MaximumSteps, cancellationToken: cancellationToken);
        if (replay.ConsumedApproximation || replay.Status is not (IrProgramExecutionStatus.Returned or IrProgramExecutionStatus.Exception))
        { return Gap("NonTerminatingOrIncompleteWitness"); }
        var assembly = EnsureAssembly(outcome, cancellationToken);
        var definition = assembly.GetType(MetadataName(method.ContainingType), throwOnError: true)!;
        var generic = method.Arity != 0 || definition.ContainsGenericParameters;
        var choices = generic ? ImmutableArray.Create<Type>(typeof(int), typeof(string)) : ImmutableArray.Create<Type>(typeof(int));
        var checks = 0;
        long maximumBytes = 0;
        var il = "NoReachableAllocationOpcode";
        var skipped = "UnboundGenericConstraint";
        foreach (var choice in choices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryBind(method, definition, choice, out var callable))
            { continue; }
            var parameters = callable!.GetParameters().ToImmutableArray();
            var values = new object?[parameters.Length];
            var aliases = ImmutableDictionary.CreateBuilder<object, object>();
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
            for (var repeat = 0; repeat < CorpusExecutionDefaults.AllocationWarmupInvocations; repeat++)
            { Invoke(run, values); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var repeat = 0; repeat < CorpusExecutionDefaults.AllocationMeasuredInvocations; repeat++)
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
        var optimization = outcome == WorkerClaimOutcome.Proven ? _compilation.Options.OptimizationLevel : OptimizationLevel.Debug;
        if (_assemblies.TryGetValue(optimization, out var existing))
        { return existing.Assembly; }
        var source = optimization == _compilation.Options.OptimizationLevel ? _compilation :
            _compilation.WithOptions(_compilation.Options.WithOptimizationLevel(optimization));
        using var image = NativeSourceEmission.EmitImage(
            source, "The allocation oracle source did not compile: ", cancellationToken);
        var context = new AssemblyLoadContext("SharpProof.NativeAllocationOracle." + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var assemblies = _assemblies.AddUnique(optimization, (context, assembly));
            _assemblyOrder.Add((context, assembly));
            _assemblies = assemblies;
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
        System.ArgumentNullException.ThrowIfNull(choice);
        System.ArgumentNullException.ThrowIfNull(definition);
        System.ArgumentNullException.ThrowIfNull(symbol);
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
                var parameters = closed.GetParameters().ToImmutableArray();
                if (parameters.Length == symbol.Parameters.Length && parameters.Select((parameter, ordinal) =>
                        parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (Matches(symbol.Parameters[ordinal].Type, parameter.ParameterType, choice))).All(match => match))
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
    { System.ArgumentNullException.ThrowIfNull(choice, nameof(choice)); System.ArgumentNullException.ThrowIfNull(runtime, nameof(runtime)); System.ArgumentNullException.ThrowIfNull(symbol, nameof(symbol)); return CompilerClrTypeMatcher.Matches(symbol, runtime, choice); }

    private static bool TryValue(Type type, IrValue value, ImmutableDictionary<object, object>.Builder aliases, out object? result)
    {
        System.ArgumentNullException.ThrowIfNull(aliases);
        result = null;
        if (value.Kind == IrValueKind.Null)
        { return !type.IsValueType; }
        if (value.Kind == IrValueKind.Boolean && type == typeof(bool))
        { result = value.Boolean; return true; }
        if (value.Kind == IrValueKind.String && type == typeof(string))
        { result = value.String; return true; }
        if (value.Kind == IrValueKind.Reference && !type.IsValueType && !type.IsAbstract && !type.IsInterface && !type.IsArray && type != typeof(string) && !typeof(Delegate).IsAssignableFrom(type))
        {
            if (!aliases.TryGetValue(value.Reference, out var reference))
            { reference = RuntimeHelpers.GetUninitializedObject(type); aliases.Add(value.Reference, reference); }
            result = reference;
            return type.IsInstanceOfType(result);
        }
        if (value.Kind != IrValueKind.Integer)
        { return false; }
        return NativeIntegerConversion.TryRuntimeValue(type, value.IntegerNumericValue, out result);
    }

    private static Action<object?[]> CreateInvocation(MethodInfo callable)
    {
        System.ArgumentNullException.ThrowIfNull(callable);
        var wrapper = new DynamicMethod("allocation-witness", typeof(void), [typeof(object[])], callable.Module, skipVisibility: true);
        var il = wrapper.GetILGenerator();
        var parameters = callable.GetParameters().ToImmutableArray();
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
    { System.ArgumentNullException.ThrowIfNull(run, nameof(run)); System.ArgumentNullException.ThrowIfNull(values, nameof(values)); try { run(values); } catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException) { } }

    private static string MetadataName(INamedTypeSymbol type)
    { System.ArgumentNullException.ThrowIfNull(type, nameof(type)); return CompilerClrTypeMatcher.MetadataName(type); }

    private static AllocationOracleObservation Gap(string reason)
    { System.ArgumentNullException.ThrowIfNull(reason, nameof(reason)); return new(reason, "NotRun", 0, null); }

    private static bool HasReachableAllocationOpcode(MethodInfo method)
    { System.ArgumentNullException.ThrowIfNull(method, nameof(method)); return AllocationIlOracle.HasPotentialAllocation(method); }

    public void Dispose()
    {
        foreach (var image in _assemblyOrder)
        { image.Context.Unload(); }
        _assemblies = _assemblies.Clear();
        _assemblyOrder.Clear();
    }
}
