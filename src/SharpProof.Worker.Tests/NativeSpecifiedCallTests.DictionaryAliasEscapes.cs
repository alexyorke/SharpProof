using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [Test]
    public async Task DictionaryEscapingAliasMustInvalidateOriginalReceiverProvenance()
    {
        await CheckInterfaceArrayContract("Mutator change", "Contract.Requires(change != null); Contract.Ensures(Contract.Result<int>() == 23); " +
            "var data = new System.Collections.Generic.Dictionary<int, int>(); var alias = data; change.Change(alias); " +
            "data[1] = 23; data[3] = 99; return data[1];", WorkerClaimOutcome.Unknown,
            members: "public abstract class Mutator { public abstract void Change(System.Collections.Generic.Dictionary<int, int> data); }");
    }

    [Test]
    public void ClrEscapedAliasCanChangeTheDictionaryComparer()
    {
        var data = new Dictionary<int, int>();
        var alias = data;
        typeof(Dictionary<int, int>).GetField("_comparer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(alias, new EqualKeys());
        data[1] = 23;
        data[3] = 99;
        Assert.That(data[1], Is.EqualTo(99));
    }

    private sealed class EqualKeys : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) { return true; }
        public int GetHashCode(int obj) { return 0; }
    }
}
