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
    ImmutableArray<object?> FiniteArguments,
    string PartialMembers,
    ImmutableArray<ImmutableArray<object?>> PartialScenarios)
{
    public GeneratedCSharpCase Frontend { get; init; } = Frontend ?? throw new System.ArgumentNullException(nameof(Frontend));
    public string FiniteMembers { get; init; } = FiniteMembers ?? throw new System.ArgumentNullException(nameof(FiniteMembers));
    public ImmutableArray<object?> FiniteArguments { get; init; } = FiniteArguments.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(FiniteArguments)) : FiniteArguments;
    public string PartialMembers { get; init; } = PartialMembers ?? throw new System.ArgumentNullException(nameof(PartialMembers));
    public ImmutableArray<ImmutableArray<object?>> PartialScenarios { get; init; } = PartialScenarios.IsDefault || PartialScenarios.Any(arguments /* SPINT001: all values valid - validation inspects default scenario rows */ => arguments.IsDefault) ? throw new ArgumentException("Scenario arrays must be initialized.", nameof(PartialScenarios)) : PartialScenarios;
}

internal sealed record FuzzSourceCampaignResult(
    int Cases, int Seed, int Agreements, int Abstentions,
    int FrontendAgreements, int SmtAgreements, int PartialSmtAgreements,
    ImmutableArray<FuzzFailure> Failures,
    ImmutableArray<FrontendDifferentialResult> FrontendResults,
    ImmutableArray<FiniteDomainDifferentialResult> FiniteResults,
    ImmutableArray<PartialTermSmtDifferentialResult> PartialResults)
{
    public ImmutableArray<FuzzFailure> Failures { get; init; } = Failures.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Failures)) : Failures;
    public ImmutableArray<FrontendDifferentialResult> FrontendResults { get; init; } = FrontendResults.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(FrontendResults)) : FrontendResults;
    public ImmutableArray<FiniteDomainDifferentialResult> FiniteResults { get; init; } = FiniteResults.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(FiniteResults)) : FiniteResults;
    public ImmutableArray<PartialTermSmtDifferentialResult> PartialResults { get; init; } = PartialResults.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(PartialResults)) : PartialResults;
}

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
            source = RepositoryParameterGuard.NotNull(source, nameof(source));
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
                input = RepositoryParameterGuard.NotNull(input, nameof(input));
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
                return new FiniteScope(factory, prepared);
            }
            return new FiniteScope(_source.FiniteMembers, cancellationToken);
        }

        private PartialScope CreatePartial(IrFactory? generatedFactory, CancellationToken cancellationToken)
        {
            if (_source == null)
            {
                var factory = generatedFactory ?? CreateGeneratedFactory();
                var generated = PartialTermSmtCaseGenerator.Create(factory, unchecked(_seed ^ 0x243F6A88));
                return new PartialScope(factory, generated);
            }
            return new PartialScope(_source.PartialMembers, _source.PartialScenarios, cancellationToken);
        }

        private static IrFactory CreateGeneratedFactory()
        {
            return new IrFactory(IrExecutionSemantics.Total);
        }

        private sealed class FiniteScope : IDisposable
        {
            private readonly BooleanSourceScope? _source;

            internal IrFactory Factory { get; }
            internal IrTerm Formula { get; }
            internal FiniteDomainPreparedFormula? Prepared { get; }
            internal BooleanMethod? Target { get; }

            internal FiniteScope(IrFactory factory, FiniteDomainPreparedFormula prepared)
            {
                factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
                prepared = RepositoryParameterGuard.NotNull(prepared, nameof(prepared));
                Factory = factory;
                Formula = prepared.Formula;
                Prepared = prepared;
            }

            internal FiniteScope(string members, CancellationToken cancellationToken)
            {
                members = RepositoryParameterGuard.NotNull(members, nameof(members));
                _source = new BooleanSourceScope(members, cancellationToken);
                try
                {
                    Target = _source.Lower("Target", cancellationToken);
                    Factory = _source.Factory;
                    Formula = Target.Term;
                }
                catch
                {
                    _source.Dispose();
                    throw;
                }
            }

            internal void RequireSafety(CancellationToken cancellationToken)
            {
                _source?.RequireFiniteSafety(Target!, cancellationToken);
            }

            public void Dispose()
            {
                _source?.Dispose();
            }
        }

        private sealed class PartialScope : IDisposable
        {
            private readonly BooleanSourceScope? _source;

            internal IrFactory Factory { get; }
            internal PartialTermSmtCase Case { get; }

            internal PartialScope(IrFactory factory, PartialTermSmtCase generated)
            {
                factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
                generated = RepositoryParameterGuard.NotNull(generated, nameof(generated));
                Factory = factory;
                Case = generated;
            }

            internal PartialScope(string members, ImmutableArray<ImmutableArray<object?>> inputs,
                CancellationToken cancellationToken)
            {
                RepositoryParameterGuard.ThrowIfInvalid(inputs.IsDefault, nameof(inputs));
                members = RepositoryParameterGuard.NotNull(members, nameof(members));
                _source = new BooleanSourceScope(members, cancellationToken);
                try
                {
                    Case = _source.CreatePartial(inputs, cancellationToken);
                    Factory = _source.Factory;
                }
                catch
                {
                    _source.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                _source?.Dispose();
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

        private static int PositiveModulo(int value /* SPINT001: all values valid - every integer is normalized with a positive divisor */, int divisor)
        {
            RepositoryParameterGuard.ThrowIfInvalid(divisor <= 0, nameof(divisor));

            var remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }
    }

    private sealed record BooleanMethod(IMethodSymbol Symbol, GuardedExpression Expression,
        IrTerm Term, ImmutableArray<FrontendVariableBinding> Variables, MethodInfo Runtime)
    {
        public IMethodSymbol Symbol { get; init; } = Symbol ?? throw new System.ArgumentNullException(nameof(Symbol));
        public GuardedExpression Expression { get; init; } = Expression ?? throw new System.ArgumentNullException(nameof(Expression));
        public IrTerm Term { get; init; } = Term ?? throw new System.ArgumentNullException(nameof(Term));
        public MethodInfo Runtime { get; init; } = Runtime ?? throw new System.ArgumentNullException(nameof(Runtime));
        public ImmutableArray<FrontendVariableBinding> Variables { get; init; } = Variables.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Variables)) : Variables;
    }

    private sealed class BooleanSourceScope : IDisposable
    {
        private readonly MemoryStream _image;
        private readonly AssemblyLoadContext _context;
        private readonly SemanticModel _model;
        private readonly Type _runtime;
        private readonly ImmutableArray<MethodDeclarationSyntax> _methods;
        internal IrFactory Factory { get; } = new(IrExecutionSemantics.Total);

        internal BooleanSourceScope(string members, CancellationToken cancellationToken)
        {
            members = RepositoryParameterGuard.NotNull(members, nameof(members));
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
                _methods = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().ToImmutableArray();
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
            var method = _methods.Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == name));
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
                    parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (new FrontendVariableBinding(parameter.Parameter, parameter.Entry)))];
            }
            else
            {
                // A closed Total clause carries a placeholder value, not a model.
                var opaque = FrontendTotalLowering.Lower(Factory, symbol, operation, _model.Compilation);
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
        internal static (bool? Value, Exception? Exception) Invoke(BooleanMethod method, ImmutableArray<object?> inputs)
        {
            RepositoryParameterGuard.ThrowIfInvalid(inputs.IsDefault, nameof(inputs));
            method = RepositoryParameterGuard.NotNull(method, nameof(method));
            try
            {
                return ((bool)method.Runtime.Invoke(null, [.. inputs])!, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                return (null, exception.InnerException);
            }
        }

        internal PartialTermSmtCase CreatePartial(ImmutableArray<ImmutableArray<object?>> inputs,
            CancellationToken cancellationToken)
        {
            RepositoryParameterGuard.ThrowIfInvalid(inputs.IsDefault, nameof(inputs));
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
            // C# completes normally only when the target does not fault, whether
            // or not the completion predicate itself is modeled.
            var normalCompletion = completion.Term;
            if (target.Expression.Classification.IsExact)
            {
                var safe = completion.Expression.Classification.IsExact
                    ? Factory.Binary(IrBinaryOperator.AndAlso, target.Expression.SafeCondition,
                        completion.Expression.SafeCondition)
                    : target.Expression.SafeCondition;
                normalCompletion = Factory.Binary(IrBinaryOperator.AndAlso, safe, completion.Term);
            }
            return new PartialTermSmtCase(target.Term, normalCompletion,
                scenarios.ToImmutable(), references.ToImmutable());
        }

        private ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue> Bind(BooleanMethod method,
            ImmutableArray<object?> inputs, ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue> scenario)
        {
            RepositoryParameterGuard.ThrowIfInvalid(inputs.IsDefault, nameof(inputs));
            scenario = RepositoryParameterGuard.NotNull(scenario, nameof(scenario));
            var result = scenario.ToBuilder();
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
                result.Add(binding.Variable, value);
            }
            return result.ToImmutable();
        }

        public void Dispose()
        {
            _image.Dispose();
            _context.Unload();
        }
    }
}
