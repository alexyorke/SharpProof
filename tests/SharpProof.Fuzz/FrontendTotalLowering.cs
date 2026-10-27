using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Fuzz;

// Lowers one expression-bodied method through the production Total lowerer.
// A closed clause carries a placeholder value, so it is replaced with an
// impure opaque term that no consumer can mistake for a model.
internal static class FrontendTotalLowering
{
    internal static FrontendTotalLoweringResult Lower(IrFactory factory, IOperation operation)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(operation);
        var model = operation.SemanticModel ??
            throw new ArgumentException("The operation has no semantic model.", nameof(operation));
        var method = model.GetEnclosingSymbol(operation.Syntax.SpanStart) as IMethodSymbol ??
            throw new ArgumentException("The operation is not inside a method.", nameof(operation));
        return Lower(factory, method, operation, model.Compilation);
    }

    internal static FrontendTotalLoweringResult Lower(
        IrFactory factory,
        IMethodSymbol method,
        IOperation operation,
        Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(compilation);
        var context = new TotalLoweringContext(factory, method) { Compilation = compilation };
        var expression = new RoslynTotalExpressionLowerer(context).LowerClause(operation, TotalParameterState.Entry);
        // Bind only the parameters the expression reads, matching the
        // variables a model of this expression can mention.
        var referenced = operation.DescendantsAndSelf()
            .OfType<IParameterReferenceOperation>()
            .Select(static reference =>
            {
                ArgumentNullException.ThrowIfNull(reference);
                return reference.Parameter.Ordinal;
            })
            .ToImmutableHashSet();
        // An opaque abstention mentions no variables.
        ImmutableArray<FrontendVariableBinding> variables = !expression.Classification.IsExact ? [] : context.Parameters
            .Where(parameter =>
            {
                ArgumentNullException.ThrowIfNull(parameter);
                return referenced.Contains(parameter.Parameter.Ordinal);
            })
            .Select(static parameter =>
            {
                ArgumentNullException.ThrowIfNull(parameter);
                return new FrontendVariableBinding(parameter.Parameter, parameter.Entry);
            })
            .ToImmutableArray();
        var term = expression.Classification.IsExact
            ? expression.Value
            : Opaque(factory, operation, expression.Value.Type);
        return new FrontendTotalLoweringResult(term, expression.SafeCondition, expression.Classification, variables);
    }

    private static IrOpaqueTerm Opaque(IrFactory factory, IOperation operation, IrTypeId type)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(operation);
        if (type.IsDefault)
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }
        var identity = CompilerIdentityBridge.InternOperation(factory, operation, null, isPure: false);
        var member = factory.GetOrCreateMemberSnapshot(
            identity, factory.ObjectType, "opaque:" + operation.Kind, type, isStatic: true, []);
        var site = factory.CreateOperation(
            operation.Kind + "@" + operation.Syntax.SpanStart.ToString(CultureInfo.InvariantCulture));
        return factory.ImpureOpaque(site, member, null);
    }
}

// A lowered expression and the condition under which evaluating it completes
// normally; the Total lowerer keeps faults out of the value term.
internal sealed class FrontendTotalLoweringResult(
    IrTerm term,
    IrTerm safeCondition,
    FrontendSubsetClassification classification /* SPINT001: all values valid - every classification is valid */,
    ImmutableArray<FrontendVariableBinding> variables)
{
    public IrTerm Term { get; } = term ?? throw new ArgumentNullException(nameof(term));
    public IrTerm SafeCondition { get; } = safeCondition ?? throw new ArgumentNullException(nameof(safeCondition));
    public FrontendSubsetClassification Classification { get; } = classification;
    public ImmutableArray<FrontendVariableBinding> Variables { get; } =
        variables.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(variables)) : variables;
    public bool IsExact => Classification.IsExact;
}

// Lowers a whole method body through the production Total program lowerer,
// which models casts, element reads and runtime faults with their C# meaning.
internal sealed class FrontendTotalBody
{
    private const int MaximumSteps = 100_000;

    private FrontendTotalBody(
        IrFactory factory,
        FrontendProgramLoweringResult lowering,
        ImmutableArray<FrontendVariableBinding> parameters,
        ImmutableHashSet<IrVarId> unreadParameters)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(lowering);
        ArgumentNullException.ThrowIfNull(unreadParameters);
        if (parameters.IsDefault)
        {
            throw new ArgumentException("Array must be initialized.", nameof(parameters));
        }
        Factory = factory;
        Lowering = lowering;
        Parameters = parameters;
        UnreadParameters = unreadParameters;
    }

    public IrFactory Factory { get; }
    public FrontendProgramLoweringResult Lowering { get; }
    public ImmutableArray<FrontendVariableBinding> Parameters { get; }
    public ImmutableHashSet<IrVarId> UnreadParameters { get; }
    public FrontendSubsetClassification Classification => Lowering.Classification;
    public bool IsExact => Classification.IsExact;

    internal static FrontendTotalBody Lower(
        IMethodSymbol method,
        MethodDeclarationSyntax syntax,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(model);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        var graph = ControlFlowGraph.Create(syntax, model, cancellationToken) ??
            throw new InvalidOperationException("The method has no control-flow graph.");
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context, cancellationToken);
        // The program reads every parameter's entry variable, so each one is
        // bound; the oracle gives the ones the body never reads a neutral value.
        var read = graph.OriginalOperation.DescendantsAndSelf()
            .OfType<IParameterReferenceOperation>()
            .Select(static reference =>
            {
                ArgumentNullException.ThrowIfNull(reference);
                return reference.Parameter.Ordinal;
            })
            .ToImmutableHashSet();
        ImmutableArray<FrontendVariableBinding> parameters = [.. context.Parameters.Select(static parameter =>
        {
            ArgumentNullException.ThrowIfNull(parameter);
            return new FrontendVariableBinding(parameter.Parameter, parameter.Entry);
        })];
        var unread = context.Parameters
            .Where(parameter =>
            {
                ArgumentNullException.ThrowIfNull(parameter);
                return !read.Contains(parameter.Parameter.Ordinal);
            })
            .Select(static parameter =>
            {
                ArgumentNullException.ThrowIfNull(parameter);
                return parameter.Entry;
            })
            .ToImmutableHashSet();
        return new(factory, lowering, parameters, unread);
    }

    internal IrProgramExecutionResult Execute(
        ImmutableDictionary<IrVarId, IrValue> values,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new IrProgramInterpreter(Factory).Execute(Lowering.Program, values, MaximumSteps, cancellationToken);
    }
}
