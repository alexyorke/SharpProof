using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class PartialGenericResultAndPreStateTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PartialReferenceResultAndOldUseTheRequestedTargetFrame(bool constructed)
    {
        var compilation = TestCompilation.Create("PartialGenericResultAndPreState", """
            using System.Collections.Generic;
            using SharpProof.Attributes;
            public static partial class Target {
                public static partial List<T> Echo<T>(List<T> value);
                public static partial List<T> Echo<T>(List<T> value) {
                    Contract.Requires(value != null);
                    Contract.Ensures(Contract.Result<List<T>>() != null);
                    Contract.Ensures(Contract.Old(value) != null);
                    return value;
                }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var target = compilation.GetTypeByMetadataName("Target")!
            .GetMembers("Echo").OfType<IMethodSymbol>().Single();
        if (constructed)
        {
            target = target.Construct(compilation.GetSpecialType(SpecialType.System_String));
        }
        var factory = new IrFactory();
        var result = new ContractBinder(compilation, factory).Bind(target);
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        var contracts = result.Contracts!;
        Assert.That(contracts.Target, Is.SameAs(target));
        Assert.That(contracts.UsesCompanion, Is.False);
        Assert.That(contracts.Clauses, Has.Length.EqualTo(3));
        Assert.That(contracts.Clauses.Count(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Requires)), Is.EqualTo(1));
        Assert.That(contracts.Clauses.Count(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Ensures)), Is.EqualTo(2));
        var parameter = contracts.Variables.Single(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.Parameter));
        var returned = contracts.Variables.Single(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.Result));
        var preState = contracts.Variables.Single(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.PreState));
        Assert.That(SymbolEqualityComparer.Default.Equals(parameter.Symbol, target.Parameters[0]), Is.True);
        Assert.That(preState.CurrentStateVariable, Is.EqualTo(parameter.Variable));
        var expectedParameterType = factory.GetOrCreateReferenceType(
            CompilerIdentityBridge.InternType(factory, target.Parameters[0].Type),
            CompilerIdentityBridge.CreateTypeDisplay(target.Parameters[0].Type));
        var expectedReturnType = factory.GetOrCreateReferenceType(
            CompilerIdentityBridge.InternType(factory, target.ReturnType),
            CompilerIdentityBridge.CreateTypeDisplay(target.ReturnType));
        Assert.That(factory.GetVariableInfo(parameter.Variable).Type, Is.EqualTo(expectedParameterType));
        Assert.That(factory.GetVariableInfo(returned.Variable).Type, Is.EqualTo(expectedReturnType));
        Assert.That(factory.GetVariableInfo(preState.Variable).Type, Is.EqualTo(expectedParameterType));
    }
}
