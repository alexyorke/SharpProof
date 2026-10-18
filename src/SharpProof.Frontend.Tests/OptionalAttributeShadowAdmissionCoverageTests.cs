using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OptionalAttributeShadowAdmissionCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualOptionalWithoutExplicitDefaultStaysOutsideShadowAdmission(bool optionalAttribute)
    {
        using var subject = TypedProgramSubject.Create("int Target() => Helper(); public static int Helper(" +
            (optionalAttribute ? "[System.Runtime.InteropServices.Optional] int value" : "int value = 0") +
            ") => value + 17;");
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var targetSyntax = methods.Single(m => m is null ? throw new System.ArgumentNullException(nameof(m)) : (m.Identifier.ValueText == "Target"));
        var helperSyntax = methods.Single(m => m is null ? throw new System.ArgumentNullException(nameof(m)) : (m.Identifier.ValueText == "Helper"));
        var invocation = (IInvocationOperation)model.GetOperation(targetSyntax.ExpressionBody!.Expression)!;
        Assert.That(invocation.Instance, Is.Null);
        Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
        var argument = invocation.Arguments[0];
        var parameter = invocation.TargetMethod.Parameters.Single();
        Assert.That(parameter.RefKind, Is.EqualTo(RefKind.None));
        Assert.That(parameter.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(parameter.IsOptional, Is.True);
        Assert.That(parameter.HasExplicitDefaultValue, Is.EqualTo(!optionalAttribute));
        Assert.That(argument.ArgumentKind, Is.EqualTo(ArgumentKind.DefaultValue));
        Assert.That(argument.Parameter!.Ordinal, Is.Zero);
        Assert.That(SymbolEqualityComparer.Default.Equals(argument.Parameter.ContainingSymbol, invocation.TargetMethod), Is.True);
        Assert.That(argument.Value.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(argument.Value.ConstantValue.HasValue, Is.True);
        Assert.That(argument.Value.ConstantValue.Value, Is.EqualTo(0));
        var policyCalls = 0;
        bool PreserveActualHelper(IMethodSymbol method)
        {
            System.ArgumentNullException.ThrowIfNull(method);
            policyCalls++;
            return SymbolEqualityComparer.Default.Equals(method, invocation.TargetMethod.OriginalDefinition);
        }
        var shadow = new RoslynProgramLowerer(subject.Factory).LowerShadowSourceBody(subject.Graph, subject.Context,
            PreserveActualHelper, default);
        Assert.That(shadow.IsShadowCallSkeleton, Is.True);
        Assert.That(shadow.IsExact, Is.False);
        Assert.That(shadow.ConstructionLimitExceeded, Is.False);
        Assert.That(policyCalls, Is.EqualTo(optionalAttribute ? 0 : 1));
        Assert.That(shadow.Classification.IsExact, Is.EqualTo(!optionalAttribute));
        Assert.That(shadow.PreservedSourceCalls, Has.Count.EqualTo(optionalAttribute ? 0 : 1));
        if (optionalAttribute)
        {
            Assert.That(shadow.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        }
        var helperContext = new TotalLoweringContext(subject.Factory, invocation.TargetMethod);
        var helperGraph = ControlFlowGraph.Create(helperSyntax, model)!;
        var direct = new RoslynProgramLowerer(subject.Factory).LowerCandidate(helperGraph, helperContext);
        Assert.That(direct.IsExact, Is.True);
        Assert.That(direct.ConstructionLimitExceeded, Is.False);
        var replay = new IrProgramInterpreter(subject.Factory).Execute(direct.Program,
            ImmutableDictionaryCapture.Create(helperContext.Parameters, p => p is null ? throw new System.ArgumentNullException(nameof(p)) : (p.Entry),
                p => p is null ? throw new System.ArgumentNullException(nameof(p)) : (subject.Factory.CreateIntegerValue(subject.Factory.GetVariableInfo(p.Entry).Type,
                    (int)argument.Value.ConstantValue.Value!))));
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(17)));
    }
}
