using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalThrowArgumentElisionApiTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void CompilerElidedConditionalArgumentsNeverExecuteEitherThrowArm(bool throwWhenNull)
    {
        var argument = throwWhenNull
            ? "value == null ? throw new System.NullReferenceException() : value.Length > 0"
            : "value == null ? value.Length > 0 : throw new System.NullReferenceException()";
        using var subject = TypedProgramSubject.Create("int Target(string value) { Log(" + argument +
            "); return 7; } [System.Diagnostics.Conditional(\"ABSENT\")] static void Log(bool value) { }");
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(syntax)!;
        Assert.That(invocation.TargetMethod.IsConditional, Is.True);
        Assert.That(invocation.TargetMethod.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType,
            subject.Context.Target.ContainingType), Is.True);
        Assert.That(CSharpPreprocessorSymbols.GetDefined(tree, TestContext.CurrentContext.CancellationToken), Does.Not.Contain("ABSENT"));
        Assert.That(subject.Graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        Assert.That(subject.Context.OwnsBody(subject.Graph.OriginalOperation), Is.True);
        var conditional = subject.Graph.Blocks.Single(block => block.ConditionKind != ControlFlowConditionKind.None);
        Assert.That(conditional.BranchValue, Is.Not.Null);
        var thrown = subject.Graph.Blocks.Single(block =>
            block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw);
        TestContext.WriteLine($"Throw SDK compiler={typeof(Compilation).Assembly.FullName}; CSharp={typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly.FullName}; kind={thrown.BranchValue?.Kind}; type={thrown.BranchValue?.Type?.ToDisplayString() ?? "<null>"}; implicit={thrown.BranchValue?.IsImplicit}");
        if (thrown.BranchValue is IConversionOperation convertedThrow)
        {
            TestContext.WriteLine($"Throw conversion exists={convertedThrow.Conversion.Exists}; identity={convertedThrow.Conversion.IsIdentity}; reference={convertedThrow.Conversion.IsReference}; numeric={convertedThrow.Conversion.IsNumeric}; operandKind={convertedThrow.Operand.Kind}; operandType={convertedThrow.Operand.Type?.ToDisplayString() ?? "<null>"}; operandImplicit={convertedThrow.Operand.IsImplicit}; constructor={(convertedThrow.Operand as IObjectCreationOperation)?.Constructor?.ToDisplayString() ?? "<none>"}");
        }
        Assert.That(thrown.BranchValue, Is.InstanceOf<IConversionOperation>());
        var throwConversion = (IConversionOperation)thrown.BranchValue!;
        Assert.That(throwConversion.IsImplicit, Is.True);
        Assert.That(throwConversion.Type!.ToDisplayString(), Is.EqualTo("System.Exception"));
        Assert.That(throwConversion.Conversion.Exists, Is.True);
        Assert.That(throwConversion.Conversion.IsIdentity, Is.False);
        Assert.That(throwConversion.Conversion.IsReference, Is.True);
        Assert.That(throwConversion.Conversion.IsNumeric, Is.False);
        Assert.That(throwConversion.Operand, Is.InstanceOf<IObjectCreationOperation>());
        Assert.That(throwConversion.Operand.IsImplicit, Is.False);
        var creation = (IObjectCreationOperation)throwConversion.Operand;
        Assert.That(creation.Constructor!.ContainingType.ToDisplayString(), Is.EqualTo("System.NullReferenceException"));
        Assert.That(conditional.IsReachable, Is.True);
        Assert.That(thrown.IsReachable, Is.True);
        var emission = new CSharpInvocationEmissionPolicy(subject.Compilation);
        Assert.That(emission.IsElided(invocation), Is.True);
        Assert.That(emission.IsElided(conditional.BranchValue!), Is.True);
        Assert.That(emission.IsElided(creation), Is.True);
        foreach (var input in new string?[] { null, "word" })
        {
            Assert.That(subject.Invoke([input!]), Is.EqualTo(7));
        }
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var parameter = subject.Context.Parameters.Single().Entry;
        var type = subject.Factory.GetVariableInfo(parameter).Type;
        foreach (var input in new string?[] { null, "word" })
        {
            var value = input == null ? subject.Factory.CreateNullValue(type) : subject.Factory.CreateStringValue(input);
            var replay = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program,
                new Dictionary<IrVarId, IrValue> { [parameter] = value });
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(7)));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }
    }
}
