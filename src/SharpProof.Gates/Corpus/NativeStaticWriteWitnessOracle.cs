using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Gates.Corpus;

internal sealed record StaticWriteOracleObservation(string RuntimeOracle, int RuntimeChecks)
{
    public string RuntimeOracle { get; init; } = RuntimeOracle ?? throw new System.ArgumentNullException(nameof(RuntimeOracle));
}

// Observes one exact source field in a fresh emission of the original compilation.
// A snapshot cannot confirm a write that stores the value already present.
internal static class NativeStaticWriteWitnessOracle
{
    internal static StaticWriteOracleObservation Check(CSharpCompilation compilation, IMethodSymbol method,
        CompilerTotalCallablePreparation preparation, PassiveCallableCheckResult evidence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!method.IsStatic || method.Arity != 0 || method.Parameters.Any(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.RefKind != RefKind.None)) ||
            method.Parameters.Length != preparation.Parameters.Length)
        { return Gap("UnsupportedWriteInvocationShape"); }
        // Native source writes retain their captured SDK span through replay.
        var span = preparation.Program.Factory.GetOperationInfo(evidence.WriteWitness!.Value).SourceSpan!;
        var initial = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        foreach (var parameter in preparation.Parameters)
        {
            // The native solver projects every parameter from this owned preparation.
            var input = evidence.EntryModel[parameter.Entry];
            initial[parameter.Entry] = input;
            initial[parameter.Current] = input;
        }
        var replay = new IrProgramInterpreter(preparation.Program.Factory).Execute(preparation.Program, initial.ToImmutable(),
            PassiveCallableVcBuilder.MaximumSteps, cancellationToken: cancellationToken);
        if (replay.ConsumedApproximation || replay.Status is not (IrProgramExecutionStatus.Returned or IrProgramExecutionStatus.Exception))
        { return Gap("NonTerminatingOrIncompleteWriteWitness"); }
        // The producer captures full paths while the SDK tree may retain a relative path.
        var trees = compilation.SyntaxTrees.Where(tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (Path.GetFullPath(
            string.IsNullOrEmpty(tree.FilePath) ? "<compiler-generated>" : tree.FilePath) == span.Document)).ToImmutableArray();
        if (trees.Length != 1)
        { return Gap("UnsupportedWriteSourceSite"); }
        var tree = trees[0];
        var root = tree.GetRoot(cancellationToken);
        var sourceSpan = new TextSpan(span.Start, span.Length);
        if (!root.FullSpan.Contains(sourceSpan))
        { return Gap("UnsupportedWriteSourceSite"); }
        var syntax = root.FindNode(sourceSpan, getInnermostNodeForTie: true);
        var operation = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree)
            .GetOperation(syntax, cancellationToken);
        var target = operation switch
        {
            ISimpleAssignmentOperation assignment => assignment.Target,
            ICompoundAssignmentOperation assignment => assignment.Target,
            IIncrementOrDecrementOperation increment => increment.Target,
            _ => null
        };
        if (target is not IFieldReferenceOperation { Field.IsStatic: true } fieldReference ||
            !SymbolEqualityComparer.Default.Equals(fieldReference.Field.ContainingAssembly, compilation.Assembly))
        { return Gap("UnsupportedWriteTarget"); }
        var fieldSymbol = fieldReference.Field;
        foreach (var owner in ImmutableArray.Create(method.ContainingType, fieldSymbol.ContainingType))
        {
            for (var current = owner; current != null; current = current.ContainingType)
            {
                if (current.Arity != 0 || current.StaticConstructors.Length != 0)
                { return Gap("UnsupportedWriteOwnerInitialization"); }
            }
        }
        // Mutable object/array contents require a separate independent observer.
        if (fieldSymbol.Type.SpecialType is not (SpecialType.System_Boolean or SpecialType.System_SByte or
            SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_String))
        { return Gap("UnsupportedWriteFieldType"); }
        using var image = NativeSourceEmission.EmitImage(compilation, "The static write oracle source did not compile: ", cancellationToken);
        var context = new AssemblyLoadContext("SharpProof.NativeStaticWriteOracle." + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var owner = assembly.GetType(CompilerClrTypeMatcher.MetadataName(method.ContainingType), throwOnError: true)!;
            var callable = owner.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Name == method.MetadataName && !candidate.IsGenericMethod &&
                    candidate.GetParameters().Length == method.Parameters.Length && candidate.GetParameters().Select((parameter, ordinal) =>
                        parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (CompilerClrTypeMatcher.Matches(method.Parameters[ordinal].Type, parameter.ParameterType, typeof(int)))).All(match => match)));
            var valueBuilder = ImmutableArray.CreateBuilder<object?>(method.Parameters.Length);
            var parameters = callable.GetParameters().ToImmutableArray();
            for (var ordinal = 0; ordinal < method.Parameters.Length; ordinal++)
            {
                if (!evidence.EntryModel.TryGetValue(preparation.Parameters[ordinal].Entry, out var input) ||
                    !TryValue(parameters[ordinal].ParameterType, input, out var value))
                { return Gap("UnsupportedWriteInput"); }
                valueBuilder.Add(value);
            }
            var field = assembly.GetType(CompilerClrTypeMatcher.MetadataName(fieldSymbol.ContainingType), throwOnError: true)!
                .GetField(fieldSymbol.MetadataName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                ?? throw new InvalidDataException("A source write witness has no matching emitted field.");
            var before = field.GetValue(null);
            try
            { callable.Invoke(null, valueBuilder.ToImmutable().ToArray()); }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            { return new("WriteInvocationThrew", 1); }
            var after = field.GetValue(null);
            return new(Equals(before, after) ? "NoObservedMutation" : "Confirmed", 1);
        }
        finally
        { context.Unload(); }
    }

    private static bool TryValue(Type type, IrValue value, out object? result)
    {
        result = null;
        if (value.Kind == IrValueKind.Null)
        { return !type.IsValueType; }
        if (value.Kind == IrValueKind.Boolean && type == typeof(bool))
        { result = value.Boolean; return true; }
        if (value.Kind == IrValueKind.String && type == typeof(string))
        { result = value.String; return true; }
        return value.Kind == IrValueKind.Integer && NativeIntegerConversion.TryRuntimeValue(type, value.IntegerNumericValue, out result);
    }

    private static StaticWriteOracleObservation Gap(string reason)
    { System.ArgumentNullException.ThrowIfNull(reason, nameof(reason)); return new(reason, 0); }
}
