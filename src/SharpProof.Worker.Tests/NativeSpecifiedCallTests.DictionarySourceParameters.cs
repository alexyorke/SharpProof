using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    [TestCase("ulong")]
    public async Task GenericPrimitiveDictionaryConstructionDoesNotWriteCallerStorage(string keyType)
    {
        ArgumentNullException.ThrowIfNull(keyType);
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length == 1); Contract.Ensures(Contract.Result<int>() == 17); values[0] = 17; var size = Allocate<" + keyType + ", int>(); return values[0] + size;",
            WorkerClaimOutcome.Proven, members: "static int Allocate<K, V>() { var data = new System.Collections.Generic.Dictionary<K, V>(); return data.Count; }");
    }

    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(1, 17); return Read<int, int>(data, 1);", "static V Read<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key) => data[key];", 17)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(1, 17); return Read<int, int>((System.Collections.Generic.Dictionary<int, int>)data, 1);", "static V Read<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key) => data[key];", 17)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<long, int>(); data.Add(long.MaxValue, 17); return Read<long, int>(data, long.MaxValue);", "static V Read<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key) => data[key];", 17)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); return Put<int, int>(data, 1, 17);", "static int Put<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key, V value) { data.Add(key, value); return data.Count; }", 1)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(1, 17); Replace<int, int>(data, 1, 23); return data[1] + data.Count;", "static void Replace<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key, V value) { data[key] = value; }", 24)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); return Forward<int, int>(data, 1, 17);", "static int Forward<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key, V value) { Store<K, V>(data, key, value); return data.Count; } static void Store<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key, V value) { if (!data.ContainsKey(key)) data.Add(key, value); }", 1)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, object>(); var item = new object(); Store<int, object>(data, 1, item); return object.ReferenceEquals(data[1], item) ? data.Count : -1;", "static void Store<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key, V value) { data[key] = value; }", 1)]
    public async Task DefaultDictionarySourceParametersShareCallerStorage(string body, string helper, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(helper);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body,
            WorkerClaimOutcome.Proven, members: helper);
    }

    [TestCase("decimal")]
    [TestCase("double")]
    [TestCase("System.Guid")]
    public async Task DefaultDictionaryHelpersCopyOpaqueValues(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayContract(element + " first, " + element + " second",
            "Contract.Ensures(Contract.Result<int>() == 2); var data = new System.Collections.Generic.Dictionary<int, " + element + ">(); Put<int, " + element + ">(data, 1, first); Put<int, " + element + ">(data, 3, second); return data.Count;",
            WorkerClaimOutcome.Proven, members: "static void Put<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key, V value) { data[key] = value; }");
    }

    [Test]
    public async Task DictionarySourceParametersDoNotSupplyIncomingComparerEvidence()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Dictionary<int, int> data", "Contract.Requires(data != null); Contract.Ensures(Contract.Result<int>() == 17); return Read(data);",
            WorkerClaimOutcome.Unknown, members: "static int Read(System.Collections.Generic.Dictionary<int, int> data) => data[1];");
    }
}
