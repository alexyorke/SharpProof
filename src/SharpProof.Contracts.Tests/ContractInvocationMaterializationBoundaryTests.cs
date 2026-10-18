using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractInvocationMaterializationBoundaryTests
{
    private static readonly ImmutableArray<bool> BooleanValues = [false, true];
    private static readonly ImmutableArray<BoundContractKind> FullKinds =
    [
        BoundContractKind.Requires,
        BoundContractKind.Assume,
        BoundContractKind.Ensures
    ];
    private const string SourceText = """
        using SharpProof.Attributes;
        public static class ContractFrames {
            public static bool Evaluate(bool input) {
                Contract.Requires(input);
                Contract.Assume(!input || input);
                Contract.Ensures(Contract.Result<bool>() == Contract.Old(input));
                return input;
            }
        }
        public static class OrdinaryCalls {
            public static void Zero() {}
            public static void Two(bool first, bool second) {}
            public static int One(int value) => value;
        }
        public static class RawCandidates {
            public static bool ZeroArgumentCall() {
                OrdinaryCalls.Zero();
                return true;
            }
            public static bool TwoArgumentCall() {
                OrdinaryCalls.Two(true, false);
                return true;
            }
            public static bool IntegerOperandCall() {
                _ = OrdinaryCalls.One(17);
                return true;
            }
        }
        """;

    [Test]
    public void ActualBooleanClauseCandidatesMaterializeInOrderWithTruthAndProvenance()
    {
        var context = CreateContext("InvocationMaterializationDirectValid", "ContractFrames", "Evaluate");
        var candidates = RecognizedCandidates(context.Body, context.Api);

        Assert.That(context.PublicContracts.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
            Is.EqualTo(candidates.Select(candidate => candidate.Kind)));
        Assert.That(candidates.Length, Is.EqualTo(3));
        var result = ContractInvocationMaterialization.Bind(
            context.Factory,
            context.ExpressionBinder,
            candidates,
            context.PublicContracts.UsesCompanion,
            requiresOnly: false);

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
            Is.EqualTo(FullKinds));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Evidence)),
            Is.All.EqualTo(BoundContractEvidence.CompilerBoundInvocation));
        Assert.That(
            result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.DiagnosticText)),
            Is.EqualTo(candidates.Select(candidate =>
                ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : (candidate.Invocation.Arguments[0].Value.Syntax.ToString())))));
        Assert.That(
            result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.SourceSyntax!.Span)),
            Is.EqualTo(candidates.Select(candidate => ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : (candidate.Invocation.Syntax.Span)))));
        Assert.That(
            result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.SourceSyntax!.SyntaxTree)),
            Is.All.SameAs(context.Tree));
        Assert.That(
            result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (DescribeOperation(context.Factory, clause))),
            Is.EqualTo(candidates.Select(candidate =>
                ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : ("contract@" + candidate.Invocation.Syntax.SpanStart.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))))));

        var parameter = context.ExpressionBinder.VariableBindings.Single(binding =>
            SymbolEqualityComparer.Default.Equals(binding.Symbol, context.Method.Parameters[0])).Variable;
        var preState = context.ExpressionBinder.PreStateVariables[parameter];
        var resultVariable = context.ExpressionBinder.ResultVariable ?? throw new AssertionException("The genuine Ensures context must have its result slot.");
        var interpreter = new IrInterpreter(context.Factory);
        foreach (var input in BooleanValues)
        {
            foreach (var oldInput in BooleanValues)
            {
                foreach (var resultValue in BooleanValues)
                {
                    var values = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(parameter, context.Factory.CreateBooleanValue(input)).SetItem(preState, context.Factory.CreateBooleanValue(oldInput)).SetItem(resultVariable, context.Factory.CreateBooleanValue(resultValue));
                    Assert.That(
                        interpreter.Evaluate(result.Clauses[0].Condition, values).Value!.Boolean,
                        Is.EqualTo(input));
                    Assert.That(
                        interpreter.Evaluate(result.Clauses[1].Condition, values).Value!.Boolean,
                        Is.True);
                    Assert.That(
                        interpreter.Evaluate(result.Clauses[2].Condition, values).Value!.Boolean,
                        Is.EqualTo(resultValue == oldInput));
                }
            }
        }
    }

    [TestCase("ZeroArgumentCall", "Zero", ContractBindingFailure.InvalidIntrinsicSignature)]
    [TestCase("TwoArgumentCall", "Two", ContractBindingFailure.InvalidIntrinsicSignature)]
    [TestCase("IntegerOperandCall", "One", ContractBindingFailure.NonBooleanCondition)]
    public void RawOrdinaryInvocationCandidatesFailOnMaterializationShape(
        string sourceMethodName,
        string invocationMethodName,
        ContractBindingFailure expectedFailure)
    {
        ArgumentNullException.ThrowIfNull(sourceMethodName);
        ArgumentNullException.ThrowIfNull(invocationMethodName);
        var context = CreateContext(
            "InvocationMaterializationRawCandidate_" + sourceMethodName,
            "RawCandidates",
            sourceMethodName);
        Assert.That(context.PublicContracts.Clauses, Is.Empty);
        var invocation = context.Body.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.TargetMethod.Name == invocationMethodName));
        Assert.That(context.Api.Clauses.GetClauseKind(invocation.TargetMethod), Is.Null);

        // These are ordinary calls supplied directly to the materializer as raw
        // candidates; the public facade above did not enroll them as Contract clauses.
        var candidates = ImmutableArray.Create(
            new ContractInvocationCandidate(BoundContractKind.Requires, invocation));
        var result = ContractInvocationMaterialization.Bind(
            context.Factory,
            context.ExpressionBinder,
            candidates,
            context.PublicContracts.UsesCompanion,
            requiresOnly: false);

        Assert.That(result.Failure, Is.EqualTo(expectedFailure));
        Assert.That(result.Clauses, Is.Empty);
    }

    private static ImmutableArray<ContractInvocationCandidate> RecognizedCandidates(
        IOperation body,
        ContractApiSymbols api)
    {
        System.ArgumentNullException.ThrowIfNull(api);
        System.ArgumentNullException.ThrowIfNull(body);
        var candidates = ImmutableArray.CreateBuilder<ContractInvocationCandidate>();
        foreach (var invocation in body.DescendantsAndSelf().OfType<IInvocationOperation>())
        {
            var kind = api.Clauses.GetClauseKind(invocation.TargetMethod);
            if (kind.HasValue)
            {
                candidates.Add(new ContractInvocationCandidate(kind.Value, invocation));
            }
        }

        return [.. candidates.OrderBy(candidate => ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : (candidate.Invocation.Syntax.SpanStart)))];
    }

    private static string DescribeOperation(IrFactory factory, BoundContractClause clause)
    {
        System.ArgumentNullException.ThrowIfNull(clause);
        System.ArgumentNullException.ThrowIfNull(factory);
        var description = factory.GetOperationInfo(clause.SourceOperation).Description;
        Assert.That(description.HasValue, Is.True);
        return factory.GetString(description!.Value);
    }

    private static DirectContext CreateContext(
        string assemblyName,
        string typeName,
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyName);
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(typeName);
        var compilation = TestCompilation.Create(assemblyName, SourceText);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var tree = compilation.SyntaxTrees.Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == methodName &&
                ((IMethodSymbol)semanticModel.GetDeclaredSymbol(method)!).ContainingType.Name == typeName));
        var source = (IMethodSymbol)semanticModel.GetDeclaredSymbol(syntax)!;
        Assert.That(
            SymbolEqualityComparer.Default.Equals(source.ContainingAssembly, compilation.Assembly),
            Is.True);
        var body = semanticModel.GetOperation(syntax)!;
        Assert.That(body, Is.Not.Null);
        Assert.That(body.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(body.Syntax.SyntaxTree, Is.SameAs(tree));
        var api = ContractApiSymbols.TryCreate(compilation);
        Assert.That(api, Is.Not.Null);
        var factory = new IrFactory();
        var publicBinding = new ContractBinder(compilation, factory).Bind(source);
        Assert.That(publicBinding.IsSuccess, Is.True, publicBinding.Failure.ToString());
        Assert.That(publicBinding.Contracts, Is.Not.Null);
        var canonicalization = new ContractCanonicalization(compilation, factory);
        var expressionBinder = new ContractExpressionBinder(
            factory,
            api!,
            source,
            canonicalization.CreateTypeSpecializer(source));
        return new DirectContext(
            tree,
            source,
            body,
            api!,
            factory,
            expressionBinder,
            publicBinding.Contracts!);
    }

    private sealed record DirectContext(
        SyntaxTree Tree,
        IMethodSymbol Method,
        IOperation Body,
        ContractApiSymbols Api,
        IrFactory Factory,
        ContractExpressionBinder ExpressionBinder,
        BoundMethodContracts PublicContracts)
    {
        public SyntaxTree Tree { get; init; } = Tree ?? throw new System.ArgumentNullException(nameof(Tree));
        public IMethodSymbol Method { get; init; } = Method ?? throw new System.ArgumentNullException(nameof(Method));
        public IOperation Body { get; init; } = Body ?? throw new System.ArgumentNullException(nameof(Body));
        public ContractApiSymbols Api { get; init; } = Api ?? throw new System.ArgumentNullException(nameof(Api));
        public IrFactory Factory { get; init; } = Factory ?? throw new System.ArgumentNullException(nameof(Factory));
        public ContractExpressionBinder ExpressionBinder { get; init; } = ExpressionBinder ?? throw new System.ArgumentNullException(nameof(ExpressionBinder));
        public BoundMethodContracts PublicContracts { get; init; } = PublicContracts ?? throw new System.ArgumentNullException(nameof(PublicContracts));
    }
}
