using System.Globalization;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
internal sealed class ArrayReadScalarAlgebraCoverageTests
{
    [TestCase("sbyte", (sbyte)0, false)]
    [TestCase("sbyte", (sbyte)-1, false)]
    [TestCase("sbyte", (sbyte)0, true)]
    [TestCase("byte", (byte)0, false)]
    [TestCase("byte", byte.MaxValue, false)]
    [TestCase("short", (short)0, false)]
    [TestCase("short", (short)-1, false)]
    [TestCase("ushort", (ushort)0, false)]
    [TestCase("ushort", ushort.MaxValue, false)]
    [TestCase("int", 0, false)]
    public void OwnedArrayReadPreservesCompilerPromotionAndOrderedNativeFaults(
        string type, object index, bool nullArray)
    {
        System.ArgumentNullException.ThrowIfNull(index);
        System.ArgumentNullException.ThrowIfNull(type);
        using var subject = TypedProgramSubject.Create(
            $"int Target(int[] values, {type} index) => values[index];");
        int[]? values = nullArray ? null : [17];
        var native = subject.Invoke([values!, index]);
        var bindings = subject.Context.Parameters.ToArray();
        var receiver = subject.Factory.Variable(bindings[0].Current);
        var position = subject.Factory.Variable(bindings[1].Current);
        var arrayType = receiver.Type;
        var elementType = subject.Factory.GetTypeInfo(arrayType).ElementType!.Value;
        var arrayValue = nullArray ? subject.Factory.CreateNullValue(arrayType)
            : subject.Factory.CreateSequenceValue(arrayType, [subject.Factory.CreateIntegerValue(elementType, 17L)]);
        var indexValue = subject.Value(bindings[1].Current, index);
        var environment = new Dictionary<IrVarId, IrValue>
        {
            [bindings[0].Current] = arrayValue,
            [bindings[1].Current] = indexValue
        };
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
        var access = (IArrayElementReferenceOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(access.Indices, Has.Length.EqualTo(1));
        Assert.That(access.Indices[0].Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        if (type != "int")
        {
            Assert.That(access.Indices[0], Is.AssignableTo<IConversionOperation>());
            Assert.That(((IConversionOperation)access.Indices[0]).IsImplicit, Is.True);
        }
        var compilerIndex = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(access.Indices[0]);
        Assert.That(compilerIndex.Classification.IsExact, Is.True);
        var rule = CSharpOperationSemantics.ArrayReadScalar(subject.Factory, receiver, position);
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(rule.Throws, Has.Length.EqualTo(2));
        Assert.That(rule.Throws[0].Kind, Is.EqualTo(IrExceptionKind.NullReference));
        Assert.That(rule.Throws[1].Kind, Is.EqualTo(IrExceptionKind.IndexOutOfRange));
        Assert.That(rule.Value, Is.TypeOf<IrSequenceAccessTerm>());
        var read = (IrSequenceAccessTerm)rule.Value;
        var interpreter = new IrInterpreter(subject.Factory);
        var promoted = interpreter.Evaluate(read.Index, environment).Value!.IntegerNumericValue;
        Assert.That(promoted, Is.EqualTo(new BigInteger(Convert.ToInt64(index, CultureInfo.InvariantCulture))));
        Assert.That(interpreter.Evaluate(compilerIndex.Value, environment).Value!.IntegerNumericValue,
            Is.EqualTo(promoted));
        Assert.That(interpreter.Evaluate(rule.Throws[0].Condition, environment).Value!.Boolean,
            Is.EqualTo(nullArray));
        if (nullArray)
        {
            Assert.That(native, Is.TypeOf<NullReferenceException>());
        }
        else
        {
            var boundsFault = interpreter.Evaluate(rule.Throws[1].Condition, environment).Value!.Boolean;
            Assert.That(boundsFault, Is.EqualTo(native is IndexOutOfRangeException));
            if (boundsFault)
            {
                Assert.That(native, Is.TypeOf<IndexOutOfRangeException>());
            }
            else
            {
                Assert.That(native, Is.EqualTo(17));
                Assert.That(interpreter.Evaluate(rule.Value, environment).Value!.IntegerNumericValue,
                    Is.EqualTo(new BigInteger(17)));
            }
        }
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program,
            new Dictionary<IrVarId, IrValue>
            {
                [bindings[0].Entry] = arrayValue,
                [bindings[1].Entry] = indexValue
            });
        Assert.That(replay.ConsumedApproximation, Is.False);
        if (native is Exception exception)
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(replay.Exception!.Kind, Is.EqualTo(exception is NullReferenceException
                ? IrExceptionKind.NullReference : IrExceptionKind.IndexOutOfRange));
        }
        else
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(17)));
        }
    }

    [TestCase("bool Target(int[] values, bool index) => values != null && index;", true, FrontendAbstention.UnsupportedType)]
    [TestCase("int Target(int[] values, long index) => values[index];", 0L, FrontendAbstention.UnsupportedOperationKind)]
    public void OwnedArrayReadRetainsBooleanAndWideIndexRejection(
        string source, object input, FrontendAbstention expected)
    {
        System.ArgumentNullException.ThrowIfNull(input);
        System.ArgumentNullException.ThrowIfNull(source);
        using var subject = TypedProgramSubject.Create(source);
        int[] values = [17];
        var native = subject.Invoke([values, input]);
        Assert.That(native, Is.EqualTo(input is bool ? true : (object)17));
        var parameters = subject.Context.Parameters.ToArray();
        var rule = CSharpOperationSemantics.ArrayReadScalar(subject.Factory,
            subject.Factory.Variable(parameters[0].Current), subject.Factory.Variable(parameters[1].Current));
        Assert.That(rule.Classification.IsExact, Is.False);
        Assert.That(rule.Classification.Abstention, Is.EqualTo(expected));
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(new IrInterpreter(subject.Factory).Evaluate(rule.Value).Value!.Boolean, Is.False);
    }

    [TestCase(0U, false)]
    [TestCase(uint.MaxValue, true)]
    public void OwnedUnsignedArrayReadChecksBoundsBeforeSignedNarrowing(uint index, bool boundsFault)
    {
        using var subject = TypedProgramSubject.Create("int Target(int[] values, uint index) => values[index];");
        int[] values = [17];
        var native = subject.Invoke([values, index]);
        var bindings = subject.Context.Parameters.ToArray();
        var receiver = subject.Factory.Variable(bindings[0].Current);
        var position = subject.Factory.Variable(bindings[1].Current);
        var elementType = subject.Factory.GetTypeInfo(receiver.Type).ElementType!.Value;
        var arrayValue = subject.Factory.CreateSequenceValue(receiver.Type,
            [subject.Factory.CreateIntegerValue(elementType, 17L)]);
        var indexValue = subject.Value(bindings[1].Current, index);
        var environment = new Dictionary<IrVarId, IrValue>
        {
            [bindings[0].Current] = arrayValue,
            [bindings[1].Current] = indexValue
        };
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
        var access = (IArrayElementReferenceOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(access.Indices, Has.Length.EqualTo(1));
        Assert.That(access.Indices[0].Type!.SpecialType, Is.EqualTo(SpecialType.System_UInt32));
        var compilerIndex = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(access.Indices[0]);
        Assert.That(compilerIndex.Classification.IsExact, Is.True);
        Assert.That(subject.Factory.GetTypeInfo(compilerIndex.Value.Type).Width, Is.EqualTo(32));
        Assert.That(subject.Factory.GetTypeInfo(compilerIndex.Value.Type).Signed, Is.False);
        var rule = CSharpOperationSemantics.ArrayReadScalar(subject.Factory, receiver, position);
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(rule.Throws, Has.Length.EqualTo(2));
        Assert.That(rule.Throws[0].Kind, Is.EqualTo(IrExceptionKind.NullReference));
        Assert.That(rule.Throws[1].Kind, Is.EqualTo(IrExceptionKind.IndexOutOfRange));
        Assert.That(rule.Value, Is.TypeOf<IrSequenceAccessTerm>());
        var read = (IrSequenceAccessTerm)rule.Value;
        Assert.That(read.Index.Type, Is.EqualTo(subject.Factory.IntegerType));
        var interpreter = new IrInterpreter(subject.Factory);
        Assert.That(interpreter.Evaluate(compilerIndex.Value, environment).Value!.IntegerNumericValue,
            Is.EqualTo(new BigInteger(index)));
        Assert.That(interpreter.Evaluate(rule.Throws[0].Condition, environment).Value!.Boolean, Is.False);
        Assert.That(interpreter.Evaluate(rule.Throws[1].Condition, environment).Value!.Boolean, Is.EqualTo(boundsFault));
        Assert.That(interpreter.Evaluate(read.Index, environment).Value!.IntegerNumericValue,
            Is.EqualTo(boundsFault ? new BigInteger(-1) : BigInteger.Zero));
        if (boundsFault)
        {
            Assert.That(native, Is.TypeOf<IndexOutOfRangeException>());
        }
        else
        {
            Assert.That(native, Is.EqualTo(17));
            Assert.That(interpreter.Evaluate(rule.Value, environment).Value!.IntegerNumericValue,
                Is.EqualTo(new BigInteger(17)));
        }
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program,
            new Dictionary<IrVarId, IrValue>
            {
                [bindings[0].Entry] = arrayValue,
                [bindings[1].Entry] = indexValue
            });
        Assert.That(replay.ConsumedApproximation, Is.False);
        if (boundsFault)
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.IndexOutOfRange));
        }
        else
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(17)));
        }
    }
}
