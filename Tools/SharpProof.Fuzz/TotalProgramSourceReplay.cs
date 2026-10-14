using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker;

namespace SharpProof.Fuzz;

internal sealed record TotalProgramSourceCase(string Member, object?[] Arguments, bool IsLoop);

internal sealed record TotalProgramSourceResult(int Cases, int Agreements, int NativeProofs,
    int NativeRefutations, int ExceptionalExits, ImmutableArray<FuzzFailure> Failures);

internal sealed class TotalProgramSourceReplay
{
    private readonly int seed;
    private readonly ImmutableArray<FuzzFailure>.Builder failures = ImmutableArray.CreateBuilder<FuzzFailure>();
    private int totalCases;
    private int agreements;
    private int nativeProofs;
    private int nativeRefutations;
    private int exceptionalExits;

    internal TotalProgramSourceReplay(int seed)
    {
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        this.seed = seed;
    }

    internal TotalProgramSourceResult Result => new(totalCases, agreements, nativeProofs,
        nativeRefutations, exceptionalExits, failures.ToImmutable());

    internal async Task CompareBatchAsync(IReadOnlyList<TotalProgramSourceCase> cases,
        int offset, CancellationToken cancellationToken = default)
    {
        var count = cases.Count;
        var members = cases.Select(item => item.Member).ToArray();
        var inputs = cases.Select(item => item.Arguments).ToArray();
        var loops = cases.Select(item => item.IsLoop).ToArray();
        totalCases += count;
        var tree = CSharpSyntaxTree.ParseText("public static class TotalGenerated { " + string.Join("\n", members) + " }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "total-fuzz.cs", cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create("TotalProgramFuzz", [tree], TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        var (image, emit) = GeneratedCompilationEmitter.Emit(compilation, cancellationToken);
        using var imageScope = image;
        if (!emit.Success)
        { throw new InvalidOperationException(string.Join("\n", emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))); }
        var assemblyContext = new AssemblyLoadContext("TotalProgramFuzz", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = assemblyContext.LoadFromStream(image).GetType("TotalGenerated")
                ?? throw new InvalidOperationException("Generated Total type is missing from the emitted assembly.");
            var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree);
            var methods = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText.StartsWith("Target", StringComparison.Ordinal)).ToArray();
            if (methods.Length != count)
            { throw new InvalidOperationException("Generated Total method count does not match its case universe."); }
            for (var index = 0; index < methods.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var method = methods[index];
                void Fail(string detail)
                {
                    if (failures.Count < FuzzRunner.MaximumRetainedFailures)
                    {
                        var reproducer = members[index] + "\n// inputs: " + JsonSerializer.Serialize(inputs[index]);
                        failures.Add(new(offset + index, seed, "total-program", reproducer, reproducer, detail));
                    }
                }
                object? expected = null;
                Exception? expectedException = null;
                try
                {
                    var compiled = runtime.GetMethod(method.Identifier.ValueText)
                        ?? throw new InvalidOperationException("Generated Total method is missing from the emitted assembly.");
                    expected = compiled.Invoke(null, inputs[index]);
                }
                catch (TargetInvocationException exception) when (exception.InnerException != null)
                { expectedException = exception.InnerException; }
                var factory = new IrFactory(IrExecutionSemantics.Total);
                var declared = model.GetDeclaredSymbol(method, cancellationToken) as IMethodSymbol
                    ?? throw new InvalidOperationException("Generated Total method has no declared method symbol.");
                var context = new TotalLoweringContext(factory, declared);
                var lower = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(method, model, cancellationToken)!, context, static _ => true, cancellationToken);
                if (!lower.IsExact)
                { Fail(method.Identifier.ValueText + ": incomplete " + lower.Classification.Abstention); continue; }
                var aliases = new Dictionary<IrTypeId, Dictionary<object, IrValue>>();
                var entries = context.Parameters.ToDictionary(parameter => parameter.Entry,
                    parameter => TotalProgramDifferentialOracle.Value(factory, factory.GetVariableInfo(parameter.Entry).Type, inputs[index][parameter.Parameter.Ordinal], aliases));
                var observed = expectedException == null ? TotalProgramDifferentialOracle.Value(factory, factory.GetVariableInfo(context.Result!.Value).Type, expected, aliases) : null;
                var original = new IrProgramInterpreter(factory).Execute(lower.Program,
                    entries, cancellationToken: cancellationToken);
                var matches = expectedException == null
                    ? original.Status == IrProgramExecutionStatus.Returned && TotalProgramDifferentialOracle.Equal(original.ReturnValue!, observed!)
                    : expectedException is NullReferenceException && original.Status == IrProgramExecutionStatus.Exception && original.Exception!.Kind == IrExceptionKind.NullReference;
                if (!matches || original.ConsumedApproximation)
                { Fail(method.Identifier.ValueText + ": compiled/original disagreement"); continue; }
                var site = factory.CreateOperation("total-fuzz-postcondition");
                IrTerm trueGoal = observed == null ? factory.Boolean(false) : factory.Binary(IrBinaryOperator.Equal,
                    factory.Variable(context.Result!.Value), loops[index] ? factory.Variable(context.Parameters[0].Current) : TotalProgramDifferentialOracle.Term(factory, observed));
                var requirements = entries.Select(entry => new PassiveContractClause(TotalProgramDifferentialOracle.Observation(factory, entry.Key, entry.Value), factory.Boolean(true), site)).ToList();
                var referenceParameters = context.Parameters.Where(parameter =>
                    factory.GetVariableInfo(parameter.Entry).Type == factory.ObjectType).ToArray();
                for (var left = 0; left < referenceParameters.Length; left++)
                {
                    for (var right = left + 1; right < referenceParameters.Length; right++)
                    {
                        var leftParameter = referenceParameters[left];
                        var rightParameter = referenceParameters[right];
                        requirements.Add(new(factory.Binary(ReferenceEquals(
                            inputs[index][leftParameter.Parameter.Ordinal], inputs[index][rightParameter.Parameter.Ordinal])
                                ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
                            factory.Variable(leftParameter.Entry), factory.Variable(rightParameter.Entry)), factory.Boolean(true), site));
                    }
                }
                var candidate = new PassiveCallableCandidate(method.Identifier.ValueText, lower.Program,
                    [.. context.Parameters.Select(parameter => new PassiveParameterBinding(parameter.Entry, parameter.Current, parameter.PreState))], context.Result,
                    [.. requirements],
                    [new(trueGoal, factory.Boolean(true), site), new(factory.Unary(IrUnaryOperator.Not, trueGoal), factory.Boolean(true), site)]);
                if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason, cancellationToken))
                { Fail(method.Identifier.ValueText + ": native admission " + reason); continue; }
                using var solver = new PassiveCallableSolver(plan!);
                var proven = await solver.VerifyEnsuresAsync(0, cancellationToken);
                var refuted = await solver.VerifyEnsuresAsync(1, cancellationToken);
                if (proven.Outcome is ProvenOutcome)
                { nativeProofs++; }
                if (refuted.Outcome is RefutedOutcome)
                { nativeRefutations++; }
                var nativeMatches = expectedException == null
                    ? refuted.Outcome is RefutedOutcome && refuted.EntryModel.Count == context.Parameters.Length && context.Parameters.All(parameter => refuted.EntryModel.ContainsKey(parameter.Entry))
                    : refuted.Outcome is ProvenOutcome && (await solver.VerifyFeasibilityAsync(cancellationToken)).Kind == PassiveCallableFeasibilityKind.NoModeledNormalReturn;
                if (proven.Outcome is not ProvenOutcome || !nativeMatches)
                { Fail(method.Identifier.ValueText + ": native agreement " + proven.Reason + "/" + refuted.Reason); continue; }
                agreements++;
                if (expectedException != null)
                { exceptionalExits++; }
            }
        }
        finally
        { assemblyContext.Unload(); }
    }
}
