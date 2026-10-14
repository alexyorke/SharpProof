using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Fuzz;

internal sealed record FuzzSourceCampaignCase(
    GeneratedCSharpCase Frontend,
    string FiniteMembers,
    IReadOnlyList<object?> FiniteArguments,
    string PartialMembers,
    IReadOnlyList<IReadOnlyList<object?>> PartialScenarios);

internal sealed record FuzzSourceCampaignResult(
    int Cases, int Seed, int Agreements, int Abstentions,
    int FrontendAgreements, int SmtAgreements, int PartialSmtAgreements,
    ImmutableArray<FuzzFailure> Failures,
    ImmutableArray<FrontendDifferentialResult> FrontendResults,
    ImmutableArray<FiniteDomainDifferentialResult> FiniteResults,
    ImmutableArray<PartialTermSmtDifferentialResult> PartialResults);

internal static class FuzzSourceCaseExecutor
{
    internal static async Task<FiniteDomainDifferentialResult> CompareFiniteAsync(
        FuzzSourceCampaignCase source, CancellationToken cancellationToken)
    {
        using var scope = new BooleanSourceScope(source.FiniteMembers, cancellationToken);
        var target = scope.Lower("Target", cancellationToken);
        var observed = BooleanSourceScope.Invoke(target, source.FiniteArguments);
        if (observed.Exception != null)
        {
            throw new InvalidOperationException("The finite source sample did not return a Boolean.", observed.Exception);
        }
        return await FiniteDomainSmtDifferentialOracle.CompareAsync(
            scope.Factory, target.Lowering.Term, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<PartialTermSmtDifferentialResult> ComparePartialAsync(
        FuzzSourceCampaignCase source, CancellationToken cancellationToken)
    {
        using var scope = new BooleanSourceScope(source.PartialMembers, cancellationToken);
        var generated = scope.CreatePartial(source.PartialScenarios, cancellationToken);
        return await PartialTermSmtDifferentialOracle.CompareAsync(
            scope.Factory, generated, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<FuzzFailure> RetainFiniteAsync(FuzzSourceCampaignCase source,
        int index, int seed, CancellationToken cancellationToken)
    {
        using var scope = new BooleanSourceScope(source.FiniteMembers, cancellationToken);
        var target = scope.Lower("Target", cancellationToken);
        return await SmtFailureRetention.RetainFiniteDomainAsync(
            scope.Factory, target.Lowering.Term, index, seed, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<FuzzFailure> RetainPartialAsync(FuzzSourceCampaignCase source,
        int index, int seed, CancellationToken cancellationToken)
    {
        using var scope = new BooleanSourceScope(source.PartialMembers, cancellationToken);
        var generated = scope.CreatePartial(source.PartialScenarios, cancellationToken);
        return await SmtFailureRetention.RetainPartialTermAsync(
            scope.Factory, generated, index, seed, cancellationToken).ConfigureAwait(false);
    }

    private sealed record BooleanMethod(IMethodSymbol Symbol,
        FrontendLoweringResult Lowering, MethodInfo Runtime);

    private sealed class BooleanSourceScope : IDisposable
    {
        private readonly MemoryStream _image;
        private readonly AssemblyLoadContext _context;
        private readonly SemanticModel _model;
        private readonly Type _runtime;
        private readonly MethodDeclarationSyntax[] _methods;
        internal IrFactory Factory { get; } = new(IrExecutionSemantics.Total);

        internal BooleanSourceScope(string members, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = "#nullable enable\npublic static class SharpProofSourceCampaignBoolean {\n" + members + "\n}\n";
            var tree = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.CSharp12), cancellationToken: cancellationToken);
            var compilation = CSharpCompilation.Create("SharpProofSourceCampaignBoolean", [tree],
                TestMetadataReferences.SortedDistinctPlatform,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release, checkOverflow: true,
                    nullableContextOptions: NullableContextOptions.Enable));
            var emission = GeneratedCompilationEmitter.Emit(compilation, cancellationToken);
            _image = emission.Image;
            if (!emission.Emit.Success)
            {
                _image.Dispose();
                throw new InvalidOperationException("Source campaign C# did not compile: " +
                    DifferentialFormatting.FormatErrors(emission.Emit.Diagnostics, includeIdTieBreak: true));
            }
            _context = new AssemblyLoadContext("SharpProofSourceCampaignBoolean", isCollectible: true);
            try
            {
                _model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
                    compilation, tree);
                _methods = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
                _image.Position = 0;
                _runtime = _context.LoadFromStream(_image).GetType("SharpProofSourceCampaignBoolean") ??
                    throw new InvalidOperationException("The compiled source does not contain its subject type.");
            }
            catch
            {
                _image.Dispose();
                _context.Unload();
                throw;
            }
        }

        internal BooleanMethod Lower(string name, CancellationToken cancellationToken)
        {
            var method = _methods.Single(method => method.Identifier.ValueText == name);
            var symbol = _model.GetDeclaredSymbol(method, cancellationToken) ??
                throw new InvalidOperationException("The source method has no actual SDK symbol.");
            if (!symbol.IsStatic || symbol.ReturnType.SpecialType != SpecialType.System_Boolean)
            {
                throw new InvalidOperationException("Source campaign methods require static Boolean return values.");
            }
            var operation = FrontendDifferentialOracle.GetExpressionOperation(
                _model, method.ExpressionBody?.Expression, cancellationToken) ??
                throw new InvalidOperationException("The source method has no expression operation.");
            var lowering = new RoslynOperationLowerer(Factory).Lower(operation);
            var runtime = _runtime.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ??
                throw new InvalidOperationException("The emitted source has no callable target.");
            return new BooleanMethod(symbol, lowering, runtime);
        }

        internal static (bool? Value, Exception? Exception) Invoke(BooleanMethod method, IReadOnlyList<object?> inputs)
        {
            try
            {
                return ((bool)method.Runtime.Invoke(null, [.. inputs])!, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                return (null, exception.InnerException);
            }
        }

        internal PartialTermSmtCase CreatePartial(IReadOnlyList<IReadOnlyList<object?>> inputs,
            CancellationToken cancellationToken)
        {
            var target = Lower("Target", cancellationToken);
            var completion = Lower("NormalCompletion", cancellationToken);
            var scenarios = ImmutableArray.CreateBuilder<ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>>();
            var references = ImmutableArray.CreateBuilder<PartialTermSemanticOutcome>();
            foreach (var arguments in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reference = Invoke(target, arguments);
                var normal = Invoke(completion, arguments);
                if (normal.Exception != null || normal.Value != (reference.Exception == null))
                {
                    throw new InvalidOperationException("Actual C# normal completion disagrees with its reference execution.");
                }
                references.Add(reference.Exception != null ? PartialTermSemanticOutcome.Undefined
                    : reference.Value == true ? PartialTermSemanticOutcome.DefinedTrue : PartialTermSemanticOutcome.DefinedFalse);
                var scenario = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty;
                scenario = Bind(target, arguments, scenario);
                scenario = Bind(completion, arguments, scenario);
                scenarios.Add(scenario);
            }
            return new PartialTermSmtCase(target.Lowering.Term, completion.Lowering.Term,
                scenarios.ToImmutable(), references.ToImmutable());
        }

        private ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue> Bind(BooleanMethod method,
            IReadOnlyList<object?> inputs, ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue> scenario)
        {
            foreach (var binding in method.Lowering.Variables)
            {
                if (binding.Symbol is not IParameterSymbol parameter ||
                    !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method.Symbol))
                {
                    throw new InvalidOperationException("Source replay requires actual method parameter bindings.");
                }
                var input = inputs[parameter.Ordinal];
                var type = Factory.GetVariableInfo(binding.Variable).Type;
                var value = input is bool boolean ? Factory.CreateBooleanValue(boolean)
                    : Factory.CreateIntegerValue(type, Convert.ToInt64(input, CultureInfo.InvariantCulture));
                scenario = scenario.Add(binding.Variable, value);
            }
            return scenario;
        }

        public void Dispose()
        {
            _image.Dispose();
            _context.Unload();
        }
    }
}
