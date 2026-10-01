using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedReferenceProgramLoweringTests
{
    public static IEnumerable<TestCaseData> ReferenceCases()
    {
        yield return Case("bool Target(object x) => x == null;", [null!], true);
        yield return Case("bool Target(object x) => x != null;", [new object()], true);
        var shared = new[] { 1, 2 };
        yield return Case("bool Target(int[] x) => x == null;", [shared], false);
        yield return Case("bool Target(int[] x) => x == null;", [null!], true);
        yield return Case("bool Target(int[] x, int[] y) => x != y;", [shared, shared], false);
        yield return Case("bool Target(int[] x, int[] y) => x != y;", [shared, shared.ToArray()], true);
        yield return Case("int Target(int[] x) => x.Length;", [null!], typeof(NullReferenceException));
        var sharedText = "a";
        var mixed = new object[] { sharedText, shared };
        var wide = new[] { ulong.MaxValue };
        yield return Case("bool Target(object x) => x != null;", [sharedText], true, "string as object");
        yield return Case("bool Target(object x) => x != null;", [shared], true, "array as object");
        yield return Case("bool Target(object x, object y) => x == y;", [shared, shared], true, "aliased arrays as objects");
        yield return Case("bool Target(object x, object y) => x == y;", [new string('a', 1), new string('a', 1)], false, "distinct strings as objects");
        yield return Case("bool Target(object x, string y) => x != null && y != null;", [sharedText, sharedText], true, "mixed static views");
        yield return Case("int Target(object[] x) => x.Length;", [mixed], 2, "reference elements");
        yield return Case("int Target(ulong[] x) => x.Length;", [wide], 1, "full ulong element");
        yield return Case("bool Target(int[] x, int[] y) => x == y;", [shared, shared], true);
        yield return Case("bool Target(int[] x, int[] y) => x == y;", [shared, shared.ToArray()], false);
        yield return Case("int Target(int[] x) => x.Length;", [shared], 2);
        yield return Case("int Target(string x) => x.Length;", [""], 0);
        yield return Case("int Target(string x) => x.Length;", ["\ud83d\ude00a"], 3);
        yield return Case("int Target(string x) => x.Length;", ["\ud800"], 1);
        yield return Case("int Target(string x) => x.Length;", [null!], typeof(NullReferenceException));
        yield return Case("int Target(string x) { try { return x.Length; } catch (System.NullReferenceException) { return -1; } }", [null!], -1);
        yield return Case("int Target(string x) { return Forward(x); } private static int Forward(string value) { return value.Length; }", ["\ud83d\ude00"], 2);
    }

    [TestCaseSource(nameof(ReferenceCases))]
    public void CompiledReferenceBodyMatchesNullLengthAndIdentityObservations(string members, object[] arguments, object expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        var actual = subject.Invoke(arguments);
        if (expected is Type exceptionType)
        {
            Assert.That(actual, Is.TypeOf(exceptionType));
        }
        else
        {
            Assert.That(actual, Is.EqualTo(expected));
        }
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var aliases = new Dictionary<IrTypeId, Dictionary<object, IrValue>>();
        var inputs = subject.Context.Parameters.ToDictionary(binding => binding.Entry,
            binding => Value(subject.Factory, subject.Factory.GetVariableInfo(binding.Entry).Type,
                arguments[binding.Parameter.Ordinal], aliases));
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program, inputs);
        if (expected is Type)
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.NullReference));
        }
        else
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.Kind == IrValueKind.Boolean ? (object)replay.ReturnValue.Boolean : (int)replay.ReturnValue.Integer,
                Is.EqualTo(expected));
        }
    }

    [Test]
    public void ExistingTotalReplayDistinguishesNullLengthAndSequenceIdentity()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var left = factory.CreateVariable("left", sequenceType);
        var right = factory.CreateVariable("right", sequenceType);
        var equal = factory.Binary(IrBinaryOperator.Equal, factory.Variable(left), factory.Variable(right));
        var interpreter = new IrInterpreter(factory);
        var shared = factory.CreateSequenceValue(sequenceType, [factory.CreateIntegerValue(1)]);
        var model = new Dictionary<IrVarId, IrValue> { [left] = shared, [right] = shared };
        Assert.That(interpreter.Evaluate(equal, model).Value!.Boolean, Is.True);
        model[right] = factory.CreateSequenceValue(sequenceType, [factory.CreateIntegerValue(1)]);
        Assert.That(interpreter.Evaluate(equal, model).Value!.Boolean, Is.False);
        var nullLength = interpreter.Evaluate(factory.Length(factory.Null(sequenceType))).Value!;
        Assert.That(nullLength.IntegerWidth, Is.EqualTo(32));
        Assert.That(nullLength.Integer, Is.Zero);
    }

    [TestCase("object", false)]
    [TestCase("object", true)]
    [TestCase("string", false)]
    [TestCase("string", true)]
    [TestCase("int[]", false)]
    [TestCase("int[]", true)]
    public void CompiledReferenceReturnsMatchOriginalSourceFrames(string type, bool isNull)
    {
        object? input = isNull ? null : type == "string" ? new string('a', 3)
            : type == "int[]" ? new[] { 1, 2, 3 } : new object();
        using var subject = TypedProgramSubject.Create($$"""
            {{type}} Target({{type}} x) { return Forward(x); }
            private static {{type}} Forward({{type}} value) { return Copy(value); }
            private static {{type}} Copy({{type}} value) { return value; }
            """);
        Assert.That(ReferenceEquals(subject.Invoke([input!]), input), Is.True);
        var lowered = subject.LowerSourceCalls();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var entry = subject.Context.Parameters.Single().Entry;
        var value = Value(subject.Factory, subject.Factory.GetVariableInfo(entry).Type, input, []);
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program,
            new Dictionary<IrVarId, IrValue> { [entry] = value });
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(ReferenceEquals(replay.ReturnValue, value), Is.True);
    }

    private static TestCaseData Case(string members, object[] arguments, object expected, string? label = null)
    {
        return new TestCaseData(members, arguments, expected).SetName("Reference observations: " + members + " #" +
            (expected is Type type ? type.Name : expected.ToString()) + (label == null ? "" : " " + label));
    }

    private static IrValue Value(IrFactory factory, IrTypeId type, object? input,
        Dictionary<IrTypeId, Dictionary<object, IrValue>> aliases)
    {
        if (input == null)
        { return factory.CreateNullValue(type); }
        var info = factory.GetTypeInfo(type);
        if (info.Kind == IrTypeKind.Boolean)
        { return factory.CreateBooleanValue((bool)input); }
        if (info.Kind == IrTypeKind.Integer)
        {
            return input is ulong bits ? factory.CreateIntegerValueFromBits(type, bits)
                : factory.CreateIntegerValue(type, Convert.ToInt64(input, System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!aliases.TryGetValue(type, out var typedAliases))
        {
            typedAliases = new Dictionary<object, IrValue>(ReferenceEqualityComparer.Instance);
            aliases.Add(type, typedAliases);
        }
        if (typedAliases.TryGetValue(input, out var shared))
        { return shared; }
        // String controls observe only null and UTF-16 length. A valid
        // same-length representative covers malformed UTF-16 inputs without
        // claiming that the IR string contains the original code units.
        var value = info.Kind switch
        {
            IrTypeKind.String when input is string text => factory.CreateStringValue(new string('\0', text.Length)),
            IrTypeKind.Sequence when input is Array array => factory.CreateSequenceValue(type,
                array.Cast<object>().Select(element => Value(factory, info.ElementType!.Value, element, aliases))),
            IrTypeKind.Reference => factory.CreateReferenceValue(type, input),
            _ => throw new ArgumentException("The runtime value does not match the reference oracle type.", nameof(input))
        };
        typedAliases.Add(input, value);
        return value;
    }
}
