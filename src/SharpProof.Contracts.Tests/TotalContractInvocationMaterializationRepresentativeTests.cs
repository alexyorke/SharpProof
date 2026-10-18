using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class TotalContractInvocationMaterializationRepresentativeTests
{
    private const string SourceText = """
        using SharpProof.Attributes;
        public static class Direct {
            public static bool Run(bool input) {
                Contract.Requires(input);
                Contract.Assume(!input);
                Contract.Ensures(Contract.Result<bool>() == Contract.Old(input));
                return input;
            }
            public static bool Raw() { _ = Ordinary.One(17); return true; }
        }
        public static class Target { public static bool Run(bool input) => input; }
        [ContractFor(typeof(Target))]
        public static class TargetContracts {
            public static bool Run(bool input) {
                Contract.Requires(input);
                Contract.Assume(!input);
                Contract.Ensures(Contract.Result<bool>() == Contract.Old(input));
                return input;
            }
        }
        public static class ByteTarget { public static bool Run(byte value) => value > 0; }
        [ContractFor(typeof(ByteTarget))]
        public static class ByteContracts {
            public static bool Run(byte value) { Contract.Requires(value > 0); return value > 0; }
        }
        public static class IntTarget { public static bool Run(int value) => value > 0; }
        public static class Ordinary { public static int One(int value) => value; }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public void CanonicalPublicBaselineAndModulePreserveStateTruthAndProvenance(bool companion)
    {
        var compilation = Compile();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var target = Method(compilation, companion ? "Target" : "Direct", "Run");
        var context = new TotalLoweringContext(factory, target);
        var publicResult = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        AssertTruth(factory, context, publicResult.Clauses);
        var resolution = EffectiveContractSourceResolver.ForCompilation(compilation, TestContext.CurrentContext.CancellationToken).Resolve(target, implementationBody: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(resolution.UsesCompanion, Is.EqualTo(companion));
        Assert.That(resolution.Inventory.ImplementationBody!.SemanticModel!.Compilation, Is.SameAs(compilation));
        var api = ContractApiSymbols.TryCreate(compilation)!;
        Assert.That(api, Is.Not.Null);
        var candidates = Candidates(resolution);
        Assert.That(candidates.Length, Is.EqualTo(3));
        foreach (var candidate in candidates)
        {
            Assert.That(candidate.Invocation.Arguments.Length, Is.EqualTo(1));
            Assert.That(api.Clauses.GetClauseKind(candidate.Invocation.TargetMethod), Is.EqualTo(candidate.Kind));
        }
        var result = TotalContractInvocationMaterialization.Bind(
            context, api, new ContractIntrinsicValidator(api), resolution, candidates, requiresOnly: false);
        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        AssertTruth(factory, context, result.Clauses);
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Evidence)), Is.All.EqualTo(
            companion ? BoundContractEvidence.Companion : BoundContractEvidence.CompilerBoundInvocation));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.SourceSyntax!.Span)),
            Is.EqualTo(candidates.Select(candidate => ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : (candidate.Invocation.Syntax.Span)))));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.DiagnosticText)),
            Is.EqualTo(candidates.Select(candidate => ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : (candidate.Invocation.Arguments[0].Value.Syntax.ToString())))));
        var requires = TotalContractInvocationMaterialization.Bind(
            context, api, new ContractIntrinsicValidator(api), resolution, candidates, requiresOnly: true);
        Assert.That(requires.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(requires.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)), Is.EqualTo(new[] { BoundContractKind.Requires }));
    }

    [Test]
    public void ActualDifferentWidthCompilerFramesRejectAsModuleArguments()
    {
        var compilation = Compile();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var byteTarget = Method(compilation, "ByteTarget", "Run");
        var intTarget = Method(compilation, "IntTarget", "Run");
        Assert.That(byteTarget.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Byte));
        Assert.That(intTarget.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var resolution = EffectiveContractSourceResolver.ForCompilation(compilation, TestContext.CurrentContext.CancellationToken).Resolve(byteTarget, implementationBody: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(resolution.UsesCompanion, Is.True);
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var byteContext = new TotalLoweringContext(factory, byteTarget);
        var valid = TotalContractInvocationMaterialization.Bind(byteContext, api,
            new ContractIntrinsicValidator(api), resolution, Candidates(resolution), requiresOnly: false);
        Assert.That(valid.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(valid.Clauses, Has.Length.EqualTo(1));
        var intContext = new TotalLoweringContext(factory, intTarget);
        Assert.That(factory.GetVariableInfo(byteContext.Parameters[0].Entry).Type,
            Is.Not.EqualTo(factory.GetVariableInfo(intContext.Parameters[0].Entry).Type));
        var incompatible = TotalContractInvocationMaterialization.Bind(intContext, api,
            new ContractIntrinsicValidator(api), resolution, Candidates(resolution), requiresOnly: false);
        Assert.That(incompatible.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(incompatible.Clauses, Is.Empty);
        Assert.That(new ContractBinder(compilation, factory).BindTotal(intContext).IsSuccess, Is.True);
    }

    [Test]
    public void RawIntegerConditionRejectsWithoutEnrollingAnOrdinaryCall()
    {
        var compilation = Compile();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var target = Method(compilation, "Direct", "Raw");
        var context = new TotalLoweringContext(factory, target);
        var resolution = EffectiveContractSourceResolver.ForCompilation(compilation, TestContext.CurrentContext.CancellationToken).Resolve(target, implementationBody: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(resolution.Inventory.Clauses, Is.Empty);
        var publicResult = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(publicResult.Clauses, Is.Empty);
        var invocation = resolution.Inventory.ImplementationBody!.DescendantsAndSelf().OfType<IInvocationOperation>().Single();
        Assert.That(invocation.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(invocation.Arguments.Length, Is.EqualTo(1));
        Assert.That(invocation.Arguments[0].Value.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var api = ContractApiSymbols.TryCreate(compilation)!;
        Assert.That(api.Clauses.GetClauseKind(invocation.TargetMethod), Is.Null);
        var result = TotalContractInvocationMaterialization.Bind(context, api, new ContractIntrinsicValidator(api), resolution,
            [new ContractInvocationCandidate(BoundContractKind.Requires, invocation)], requiresOnly: false);
        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.NonBooleanCondition));
        Assert.That(result.Clauses, Is.Empty);
    }

    private static void AssertTruth(IrFactory factory, TotalLoweringContext context,
        ImmutableArray<BoundTotalContractClause> clauses)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        Assert.That(clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)), Is.EqualTo(
            new[] { BoundContractKind.Requires, BoundContractKind.Assume, BoundContractKind.Ensures }));
        var result = context.Result ?? throw new AssertionException("Boolean target must own a result variable.");
        var parameter = context.Parameters.Single();
        var interpreter = new IrInterpreter(factory);
        foreach (var entry in new[] { false, true })
        {
            foreach (var current in new[] { false, true })
            {
                foreach (var previous in new[] { false, true })
                {
                    foreach (var returned in new[] { false, true })
                    {
                        var values = new Dictionary<IrVarId, IrValue>
                        {
                            [parameter.Entry] = factory.CreateBooleanValue(entry),
                            [parameter.Current] = factory.CreateBooleanValue(current),
                            [parameter.PreState] = factory.CreateBooleanValue(previous),
                            [result] = factory.CreateBooleanValue(returned)
                        };
                        var expected = new[] { entry, !current, returned == previous };
                        for (var index = 0; index < clauses.Length; index++)
                        {
                            Assert.That(interpreter.Evaluate(clauses[index].Value, values).Value!.Boolean, Is.EqualTo(expected[index]));
                            Assert.That(interpreter.Evaluate(clauses[index].SafeCondition, values).Value!.Boolean, Is.True);
                        }
                    }
                }
            }
        }
    }

    private static ImmutableArray<ContractInvocationCandidate> Candidates(EffectiveContractSourceResolution resolution)
    {
        System.ArgumentNullException.ThrowIfNull(resolution);
        return [.. resolution.Inventory.Clauses.Where(static occurrence => occurrence is null ? throw new System.ArgumentNullException(nameof(occurrence)) : (occurrence.IsValid))
            .Select(static occurrence => occurrence is null ? throw new System.ArgumentNullException(nameof(occurrence)) : (new ContractInvocationCandidate(occurrence.Kind, occurrence.Invocation)))];
    }

    private static IMethodSymbol Method(CSharpCompilation compilation, string typeName, string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(compilation);
        var method = compilation.GetTypeByMetadataName(typeName)!.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        return method;
    }

    private static CSharpCompilation Compile()
    {
        var compilation = TestCompilation.Create("TotalRawMaterializationRepresentatives", SourceText);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        return compilation;
    }
}
