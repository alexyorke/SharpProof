using FiniteDomainPreparedFormula = SharpProof.Fuzz.FiniteDomainSmtDifferentialOracle.FiniteDomainPreparedFormula;
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
    internal sealed class NativeInput
    {
        private readonly int _seed;
        private readonly FuzzSourceCampaignCase? _source;

        private NativeInput(int seed, FuzzSourceCampaignCase? source)
        {
            _seed = seed;
            _source = source;
        }

        internal static NativeInput Generated(int seed)
        {
            return new NativeInput(seed, null);
        }

        internal static NativeInput Source(FuzzSourceCampaignCase source)
        {
            return new NativeInput(0, source);
        }

        internal Comparison CreateComparison()
        {
            return new Comparison(this);
        }

        internal sealed class Comparison
        {
            private readonly NativeInput _input;
            private readonly IrFactory? _generatedFactory;

            internal Comparison(NativeInput input)
            {
                _input = input;
                _generatedFactory = input._source == null ? CreateGeneratedFactory() : null;
            }

            internal async Task<FiniteDomainDifferentialResult> CompareFiniteAsync(CancellationToken cancellationToken)
            {
                using var scope = _input.CreateFinite(_generatedFactory, cancellationToken);
                if (scope.Prepared != null)
                {
                    return await FiniteDomainSmtDifferentialOracle.ComparePreparedAsync(
                        scope.Factory, scope.Prepared, cancellationToken).ConfigureAwait(false);
                }
                var observed = BooleanSourceScope.Invoke(scope.Target!, _input._source!.FiniteArguments);
                if (observed.Exception != null)
                {
                    throw new InvalidOperationException("The finite source sample did not return a Boolean.", observed.Exception);
                }
                scope.RequireSafety(cancellationToken);
                return await FiniteDomainSmtDifferentialOracle.CompareAsync(
                    scope.Factory, scope.Formula, cancellationToken).ConfigureAwait(false);
            }

            internal async Task<PartialTermSmtDifferentialResult> ComparePartialAsync(CancellationToken cancellationToken)
            {
                using var scope = _input.CreatePartial(_generatedFactory, cancellationToken);
                return await PartialTermSmtDifferentialOracle.CompareAsync(
                    scope.Factory, scope.Case, cancellationToken).ConfigureAwait(false);
            }
        }

        internal async Task<FuzzFailure> RetainFiniteAsync(int index, int seed, CancellationToken cancellationToken)
        {
            using var scope = CreateFinite(null, cancellationToken);
            scope.RequireSafety(cancellationToken);
            return await SmtFailureRetention.RetainFiniteDomainAsync(
                scope.Factory, scope.Formula, index, seed, cancellationToken).ConfigureAwait(false);
        }

        internal async Task<FuzzFailure> RetainPartialAsync(int index, int seed, CancellationToken cancellationToken)
        {
            using var scope = CreatePartial(null, cancellationToken);
            return await SmtFailureRetention.RetainPartialTermAsync(
                scope.Factory, scope.Case, index, seed, cancellationToken).ConfigureAwait(false);
        }

        private FiniteScope CreateFinite(IrFactory? generatedFactory, CancellationToken cancellationToken)
        {
            if (_source == null)
            {
                var factory = generatedFactory ?? CreateGeneratedFactory();
                var prepared = CreateTotalFiniteDomainFormula(factory, _seed, cancellationToken);
                return new FiniteScope(factory, prepared.Formula, prepared, null, null);
            }
            var source = new BooleanSourceScope(_source.FiniteMembers, cancellationToken);
            try
            {
                var target = source.Lower("Target", cancellationToken);
                return new FiniteScope(source.Factory, target.Term, null, source, target);
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }

        private PartialScope CreatePartial(IrFactory? generatedFactory, CancellationToken cancellationToken)
        {
            if (_source == null)
            {
                var factory = generatedFactory ?? CreateGeneratedFactory();
                var generated = PartialTermSmtCaseGenerator.Create(factory, unchecked(_seed ^ 0x243F6A88));
                return new PartialScope(factory, generated, null);
            }
            var source = new BooleanSourceScope(_source.PartialMembers, cancellationToken);
            try
            {
                var generated = source.CreatePartial(_source.PartialScenarios, cancellationToken);
                return new PartialScope(source.Factory, generated, source);
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }

        private static IrFactory CreateGeneratedFactory()
        {
            return new IrFactory(IrExecutionSemantics.Total);
        }

        private sealed record FiniteScope(IrFactory Factory, IrTerm Formula,
            FiniteDomainPreparedFormula? Prepared, BooleanSourceScope? Source, BooleanMethod? Target) : IDisposable
        {
            internal void RequireSafety(CancellationToken cancellationToken)
            {
                Source?.RequireFiniteSafety(Target!, cancellationToken);
            }

            public void Dispose()
            {
                Source?.Dispose();
            }
        }

        private sealed record PartialScope(IrFactory Factory, PartialTermSmtCase Case,
            BooleanSourceScope? Source) : IDisposable
        {
            public void Dispose()
            {
                Source?.Dispose();
            }
        }

        private static FiniteDomainPreparedFormula CreateTotalFiniteDomainFormula(
            IrFactory factory,
            int caseSeed,
            CancellationToken cancellationToken)
        {
            var generator = new WellSortedIrGenerator(
                factory,
                unchecked(caseSeed ^ 0x6C8E9CF5));
            cancellationToken.ThrowIfCancellationRequested();
            var generated = generator.NextArithmeticOrBoolean(maximumDepth: 3);
            var formula = generated.Term.Type == factory.BooleanType
                ? generated.Term
                : factory.Binary(
                    IrBinaryOperator.Equal,
                    generated.Term,
                    factory.Integer(
                        FiniteDomainSmtDifferentialOracle.IntegerDomain[
                            PositiveModulo(
                                caseSeed,
                                FiniteDomainSmtDifferentialOracle
                                    .IntegerDomain
                                    .Length)]));
            // Both callers use Total semantics. This generator's arithmetic and
            // Boolean terms are defined for all assignments to its two integer
            // variables and one Boolean (at most 50 finite-domain assignments).
            // Keep the admission check so a future generator change fails closed.
            return FiniteDomainCampaignAdmission.RequireDefinedFormula(factory, formula, cancellationToken);
        }
    
        private static int PositiveModulo(int value, int divisor)
        {
            var remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }
    }

    private sealed record BooleanMethod(IMethodSymbol Symbol, GuardedExpression Expression,
        IrTerm Term, ImmutableArray<FrontendVariableBinding> Variables, MethodInfo Runtime);

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
            var context = new TotalLoweringContext(Factory, symbol)
            {
                Compilation = _model.Compilation
            };
            var expression = new RoslynTotalExpressionLowerer(context).LowerClause(operation, TotalParameterState.Entry);
            IrTerm term;
            ImmutableArray<FrontendVariableBinding> variables;
            if (expression.Classification.IsExact)
            {
                term = expression.Value;
                variables = [.. context.Parameters.Select(parameter =>
                    new FrontendVariableBinding(parameter.Parameter, parameter.Entry))];
            }
            else
            {
                // A closed Total clause carries a placeholder value, not a model.
                // Keep the compatibility boundary's actual opaque term for rejection.
                var opaque = new RoslynOperationLowerer(Factory).Lower(operation);
                if (opaque.Term is not IrOpaqueTerm)
                {
                    throw new InvalidOperationException("An incomplete Total clause has no authentic opaque expression for rejection.");
                }
                term = opaque.Term;
                variables = opaque.Variables;
            }
            var runtime = _runtime.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ??
                throw new InvalidOperationException("The emitted source has no callable target.");
            return new BooleanMethod(symbol, expression, term, variables, runtime);
        }

        internal void RequireFiniteSafety(BooleanMethod method, CancellationToken cancellationToken)
        {
            if (method.Expression.Classification.IsExact &&
                !FiniteDomainSmtDifferentialOracle.IsSafeForAllAssignments(
                    Factory, method.Term, method.Expression.SafeCondition, cancellationToken))
            {
                throw new InvalidOperationException("The finite source safety condition is not defined and true for every bounded assignment.");
            }
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
            var normalCompletion = completion.Term;
            if (target.Expression.Classification.IsExact && completion.Expression.Classification.IsExact)
            {
                normalCompletion = Factory.Binary(IrBinaryOperator.AndAlso,
                    Factory.Binary(IrBinaryOperator.AndAlso, target.Expression.SafeCondition,
                        completion.Expression.SafeCondition), completion.Term);
            }
            return new PartialTermSmtCase(target.Term, normalCompletion,
                scenarios.ToImmutable(), references.ToImmutable());
        }

        private ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue> Bind(BooleanMethod method,
            IReadOnlyList<object?> inputs, ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue> scenario)
        {
            foreach (var binding in method.Variables)
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
