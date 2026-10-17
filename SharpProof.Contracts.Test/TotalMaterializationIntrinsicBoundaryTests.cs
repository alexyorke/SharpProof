using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class TotalMaterializationIntrinsicBoundaryTests
{
    [TestCase("ResultRequires", ContractBindingFailure.ResultOutsideEnsures)]
    [TestCase("WrongResultSignature", ContractBindingFailure.InvalidIntrinsicSignature)]
    public void ActualInvalidIntrinsicSourcesKeepPublicAndModuleFailure(string methodName, ContractBindingFailure expected)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        var compilation = Compile();
        var target = Method(compilation, methodName);
        var resolution = EffectiveContractSourceResolver.ForCompilation(compilation).Resolve(target);
        Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var intrinsics = new ContractIntrinsicValidator(api);
        var violations = intrinsics.Validate(resolution.Source, resolution.Inventory.ImplementationBody);
        Assert.That(violations, Has.Length.EqualTo(1));
        Assert.That(violations[0].Failure, Is.EqualTo(expected));
        Assert.That(violations[0].Operation.SemanticModel!.Compilation, Is.SameAs(compilation));
        var publicResult = new ContractBinder(compilation, factory).BindTotal(new TotalLoweringContext(factory, target));
        Assert.That(publicResult.Failure, Is.EqualTo(expected));
        Assert.That(publicResult.Clauses, Is.Empty);
        var candidates = resolution.Inventory.Clauses.Where(static occurrence => occurrence is null ? throw new System.ArgumentNullException(nameof(occurrence)) : (occurrence.IsValid))
            .Select(static occurrence => occurrence is null ? throw new System.ArgumentNullException(nameof(occurrence)) : (new ContractInvocationCandidate(occurrence.Kind, occurrence.Invocation))).ToImmutableArray();
        var moduleResult = TotalContractInvocationMaterialization.Bind(new TotalLoweringContext(factory, target),
            api, intrinsics, resolution, candidates, requiresOnly: false);
        Assert.That(moduleResult.Failure, Is.EqualTo(expected));
        Assert.That(moduleResult.Clauses, Is.Empty);
    }

    [Test]
    public void RequiresOnlyIgnoresActualEnsuresSignatureViolationAndPreservesPredicateTruth()
    {
        var compilation = Compile();
        var target = Method(compilation, "WrongResultSignature");
        var resolution = EffectiveContractSourceResolver.ForCompilation(compilation).Resolve(target);
        Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var intrinsics = new ContractIntrinsicValidator(api);
        var violation = intrinsics.Validate(resolution.Source, resolution.Inventory.ImplementationBody).Single();
        Assert.That(violation.EnclosingClauseKind, Is.EqualTo(BoundContractKind.Ensures));
        Assert.That(violation.Failure, Is.EqualTo(ContractBindingFailure.InvalidIntrinsicSignature));
        var candidates = resolution.Inventory.Clauses.Where(static occurrence => occurrence is null ? throw new System.ArgumentNullException(nameof(occurrence)) : (occurrence.IsValid))
            .Select(static occurrence => occurrence is null ? throw new System.ArgumentNullException(nameof(occurrence)) : (new ContractInvocationCandidate(occurrence.Kind, occurrence.Invocation))).ToImmutableArray();
        var moduleResult = TotalContractInvocationMaterialization.Bind(context, api, intrinsics, resolution, candidates, requiresOnly: true);
        var publicResult = new ContractBinder(compilation, factory).BindTotalRequires(context);
        Assert.That(moduleResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(moduleResult.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)), Is.EqualTo(new[] { BoundContractKind.Requires }));
        Assert.That(publicResult.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)), Is.EqualTo(new[] { BoundContractKind.Requires }));
        var entry = context.Parameters.Single().Entry;
        var interpreter = new IrInterpreter(factory);
        foreach (var input in new[] { false, true })
        {
            var values = new Dictionary<IrVarId, IrValue> { [entry] = factory.CreateBooleanValue(input) };
            Assert.That(interpreter.Evaluate(moduleResult.Clauses[0].Value, values).Value!.Boolean, Is.EqualTo(input));
            Assert.That(interpreter.Evaluate(publicResult.Clauses[0].Value, values).Value!.Boolean, Is.EqualTo(input));
            Assert.That(interpreter.Evaluate(moduleResult.Clauses[0].SafeCondition, values).Value!.Boolean, Is.True);
        }
    }

    private static IMethodSymbol Method(CSharpCompilation compilation, string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(methodName);
        return compilation.GetTypeByMetadataName("IntrinsicSource")!.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    }

    private static CSharpCompilation Compile()
    {
        var compilation = TestCompilation.Create("TotalMaterializationIntrinsicBoundaries", """
            using SharpProof.Attributes;
            public static class IntrinsicSource {
                public static bool ResultRequires(bool input) { Contract.Requires(Contract.Result<bool>()); return input; }
                public static int WrongResultSignature(bool input) {
                    Contract.Requires(input);
                    Contract.Ensures(Contract.Result<bool>());
                    return 0;
                }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        return compilation;
    }
}
