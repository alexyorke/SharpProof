using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("change.Change(data);", "Mutator change", "Mutator change", ", change", "public abstract class Mutator { public abstract void Change(System.Collections.Generic.Dictionary<int, int> data); }")]
    [TestCase("change();", "System.Action change", "System.Action change", ", change", "")]
    public async Task DictionaryUnknownWritersInsideSourceHelpersInvalidateComparerEvidence(string mutation, string parameters, string helperParameters, string arguments, string members)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(helperParameters);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(members);
        await CheckInterfaceArrayContract(parameters,
            "Contract.Requires(change != null); Contract.Ensures(Contract.Result<int>() == 23); var data = new System.Collections.Generic.Dictionary<int, int>(); return ChangeAndRead(data" + arguments + ");",
            WorkerClaimOutcome.Unknown,
            members: members + " static int ChangeAndRead(System.Collections.Generic.Dictionary<int, int> data, " + helperParameters + ") { " + mutation + " data[1] = 23; data[3] = 99; return data[1]; }");
    }

    [Test]
    public void ClrDictionarySourceHelperCanObserveComparerMutationBeforeItsWrites()
    {
        var data = new Dictionary<int, int>();
        var result = ChangeAndRead(data, alias =>
        {
            ArgumentNullException.ThrowIfNull(alias);
            typeof(Dictionary<int, int>)
                .GetField("_comparer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(alias, new EqualKeys());
        });
        Assert.That(result, Is.EqualTo(99));

        static int ChangeAndRead(Dictionary<int, int> dictionary, Action<Dictionary<int, int>> change)
        {
            ArgumentNullException.ThrowIfNull(dictionary);
            ArgumentNullException.ThrowIfNull(change);
            change(dictionary);
            dictionary[1] = 23;
            dictionary[3] = 99;
            return dictionary[1];
        }
    }

    [TestCase("new EqualIntegerKeys()")]
    [TestCase("System.Collections.Generic.EqualityComparer<int>.Default")]
    public async Task DictionarySourceHelpersDoNotInferDefaultConstructionFromComparerArguments(string comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        await CheckInterfaceArrayContract("",
            "Contract.Ensures(Contract.Result<int>() == 23); var data = new System.Collections.Generic.Dictionary<int, int>(" + comparer + "); return StoreAndRead(data);",
            WorkerClaimOutcome.Unknown,
            members: "static int StoreAndRead(System.Collections.Generic.Dictionary<int, int> data) { data[1] = 23; data[3] = 99; return data[1]; } public sealed class EqualIntegerKeys : System.Collections.Generic.IEqualityComparer<int> { public bool Equals(int left, int right) => true; public int GetHashCode(int value) => 0; }");
    }

    [TestCase("System.Collections.Generic.Dictionary<int, int> other", "return Read(data, other);", "static int Read(System.Collections.Generic.Dictionary<int, int> data, System.Collections.Generic.Dictionary<int, int> other) { data = other; return data[1]; }")]
    [TestCase("System.Collections.Generic.Dictionary<int, int> other, bool choose", "return Read(data, other, choose);", "static int Read(System.Collections.Generic.Dictionary<int, int> data, System.Collections.Generic.Dictionary<int, int> other, bool choose) { var alias = choose ? data : other; return alias[1]; }")]
    [TestCase("System.Collections.Generic.Dictionary<int, int> other", "return Read(data, other);", "static int Read(System.Collections.Generic.Dictionary<int, int> data, System.Collections.Generic.Dictionary<int, int> other) { System.Action replace = () => data = other; replace(); return data[1]; }")]
    [TestCase("", "return Read(data);", "static int Read(System.Collections.Generic.Dictionary<int, int> data) { Escape(ref data); return data[1]; } static void Escape(ref System.Collections.Generic.Dictionary<int, int> value) { }")]
    [TestCase("System.Collections.Generic.Dictionary<int, int> other", "return Read(data, other);", "static int Read(System.Collections.Generic.Dictionary<int, int> data, System.Collections.Generic.Dictionary<int, int> other) { __refvalue(__makeref(data), System.Collections.Generic.Dictionary<int, int>) = other; return data[1]; }")]
    public async Task DictionarySourceParametersDoNotPreserveOriginThroughMutableStorage(string parameters, string body, string helper)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(helper);
        await CheckInterfaceArrayContract(parameters,
            "Contract.Ensures(Contract.Result<int>() == 17); var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(1, 17); " + body,
            WorkerClaimOutcome.Unknown, members: helper);
    }

    [TestCase("data.Add(key, key = 3);")]
    [TestCase("data[key] = key = 3;")]
    public async Task DictionarySourceHelpersEvaluateKeysBeforeAssignedValues(string assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        await CheckInterfaceArrayContract("",
            "Contract.Ensures(Contract.Result<int>() == 3); var data = new System.Collections.Generic.Dictionary<int, int>(); return Store(data);",
            WorkerClaimOutcome.Proven,
            members: "static int Store(System.Collections.Generic.Dictionary<int, int> data) { var key = 1; " + assignment + " return data.ContainsKey(3) ? -1 : data[1]; }");
    }

    [Test]
    public async Task DictionaryReceiverIsEvaluatedBeforeAssignmentValueRebindsItsLocal()
    {
        await CheckInterfaceArrayContract("",
            "Contract.Ensures(Contract.Result<int>() == 1700); var data = new System.Collections.Generic.Dictionary<int, int>(); var original = data; var other = new System.Collections.Generic.Dictionary<int, int>(); data[1] = (data = other).Count + 17; return Read(original) * 100 + data.Count;",
            WorkerClaimOutcome.Proven,
            members: "static int Read(System.Collections.Generic.Dictionary<int, int> data) => data[1];");
    }
}
