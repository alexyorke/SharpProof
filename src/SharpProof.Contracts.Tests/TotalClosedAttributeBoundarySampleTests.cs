using System.Globalization;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class TotalClosedAttributeBoundarySampleTests
{
    [TestCase("sbyte")]
    [TestCase("ushort")]
    [TestCase("ulong")]
    public void SourceRangeAttributesRetainTypedCompilerAndClrBoundaries(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        var compilation = TestCompilation.Create("TotalClosedBoundarySample", $$"""
            using SharpProof.Attributes;
            public static class Target {
                [return: InRange(-1L, 1L)]
                public static {{typeName}} Read([InRange(-129L, 128L)] {{typeName}} value) => value;
            }
            public static class Caller {
                public static void Call() { _ = Target.Read(0); }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = model.GetOperation(call);
        Assert.That(operation, Is.InstanceOf<IInvocationOperation>());
        var target = ((IInvocationOperation)operation!).TargetMethod;
        Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(target.Parameters, Has.Length.EqualTo(1));
        Assert.That(target.Parameters[0].GetAttributes(), Has.Length.EqualTo(1));
        Assert.That(target.GetReturnTypeAttributes(), Has.Length.EqualTo(1));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var bound = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(bound.IsSuccess, Is.True, bound.Failure.ToString());
        Assert.That(bound.Clauses, Has.Length.EqualTo(2));
        Assert.That(bound.Clauses.All(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Evidence == BoundContractEvidence.ClosedAttribute)), Is.True);
        var requires = bound.Clauses.Single(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Requires));
        var ensures = bound.Clauses.Single(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Ensures));
        var entry = context.Parameters[0].Entry;
        var result = context.Result!.Value;
        var inputType = factory.GetVariableInfo(entry).Type;
        var info = factory.GetTypeInfo(inputType);
        Assert.That(info.Kind, Is.EqualTo(IrTypeKind.Integer));
        Assert.That(info.Width, Is.EqualTo(typeName switch { "sbyte" => 8, "ushort" => 16, _ => 64 }));
        Assert.That(info.Signed, Is.EqualTo(typeName == "sbyte"));
        var runtime = System.Reflection.Assembly.Load(image.ToArray()).GetType("Target")!.GetMethod("Read")!;
        var values = typeName switch
        {
            "sbyte" => new BigInteger[] { -128, -1, 0, 1, 127 },
            "ushort" => new BigInteger[] { 0, 1, 128, 129, ushort.MaxValue },
            _ => new BigInteger[] { 0, 1, 128, 129, ulong.MaxValue }
        };
        foreach (var value in values)
        {
            object argument = typeName switch { "sbyte" => (sbyte)value, "ushort" => (ushort)value, _ => (ulong)value };
            var actualReturn = runtime.Invoke(null, [argument]);
            Assert.That(BigInteger.Parse(Convert.ToString(actualReturn, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture), Is.EqualTo(value));
            var supplied = new Dictionary<IrVarId, IrValue>
            {
                [entry] = info.Signed ? factory.CreateIntegerValue(inputType, (long)value) : factory.CreateIntegerValue(inputType, (ulong)value),
                [result] = info.Signed ? factory.CreateIntegerValue(factory.GetVariableInfo(result).Type, (long)value) :
                    factory.CreateIntegerValue(factory.GetVariableInfo(result).Type, (ulong)value)
            };
            var interpreter = new IrInterpreter(factory);
            Assert.That(interpreter.Evaluate(requires.Value, supplied).Value!.Boolean, Is.EqualTo(value >= -129 && value <= 128));
            Assert.That(interpreter.Evaluate(ensures.Value, supplied).Value!.Boolean, Is.EqualTo(value >= -1 && value <= 1));
            Assert.That(interpreter.Evaluate(requires.SafeCondition, supplied).Value!.Boolean, Is.True);
            Assert.That(interpreter.Evaluate(ensures.SafeCondition, supplied).Value!.Boolean, Is.True);
        }
        var legacy = new ContractBinder(compilation, new IrFactory()).Bind(target);
        Assert.That(legacy.IsSuccess, Is.EqualTo(typeName != "ulong"));
        Assert.That(legacy.Failure, Is.EqualTo(typeName == "ulong"
            ? ContractBindingFailure.InvalidClosedAttribute : ContractBindingFailure.None));
    }
}
