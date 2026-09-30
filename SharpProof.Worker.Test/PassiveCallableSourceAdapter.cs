using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

// This trusted adapter is test-only. The worker receives immutable IR and never
// loads source assemblies. Mixed source origins cannot enroll a candidate.
internal sealed record PassiveSourceSubject(TotalLoweringContext Context,
    TotalContractBindingResult Binding, FrontendProgramLoweringResult Lowering)
{
    internal static PassiveSourceSubject Create(string source, IrFactory? factory = null, string methodName = "Target")
    {
        var compilation = TestCompilation.Create("PassiveCandidate", source);
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == methodName);
        var model = compilation.GetSemanticModel(method.SyntaxTree);
        var context = new TotalLoweringContext(factory ?? new IrFactory(IrExecutionSemantics.Total), (IMethodSymbol)model.GetDeclaredSymbol(method)!);
        var binding = new ContractBinder(compilation, context.Factory).BindTotal(context);
        var lowering = new RoslynProgramLowerer(context.Factory).LowerCandidate(ControlFlowGraph.Create(method, model)!, context);
        return new(context, binding, lowering);
    }

    internal PassiveCallableCandidate? Enroll()
    {
        return Enroll(Context, Binding, Lowering);
    }

    internal static PassiveCallableCandidate? Enroll(TotalLoweringContext context,
        TotalContractBindingResult binding, FrontendProgramLoweringResult lowering)
    {
        if (!ReferenceEquals(context.Origin, binding.Origin) || !ReferenceEquals(context.Origin, lowering.TotalOrigin) ||
            !ReferenceEquals(context.Factory, lowering.Program.Factory))
        { throw new ArgumentException("Binding and lowering must originate in the same callable context."); }
        // Source Assume is erased today. Do not enroll it until the original
        // replay program itself carries its point filter and user provenance.
        if (!binding.IsSuccess || !lowering.IsExact || binding.Clauses.Any(clause => clause.Kind == BoundContractKind.Assume))
        { return null; }
        ImmutableArray<PassiveContractClause> Clauses(BoundContractKind kind)
        {
            return [.. binding.Clauses.Where(clause => clause.Kind == kind)
                .Select(clause => new PassiveContractClause(clause.Value, clause.SafeCondition, clause.SourceOperation))];
        }
        return new(context.Target.Name, lowering.Program,
            [.. context.Parameters.Select(parameter => new PassiveParameterBinding(parameter.Entry, parameter.Current, parameter.PreState))],
            context.Result, Clauses(BoundContractKind.Requires), Clauses(BoundContractKind.Ensures));
    }
}
