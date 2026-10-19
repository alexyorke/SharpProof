using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [Test]
    public async Task IncompatibleClosedGenericQueueFieldsCannotRefuteIdentity()
    {
        await CheckInterfaceArrayContract("Holder holder",
            "Contract.Requires(holder != null && holder.First != null && holder.Second != null); Contract.Ensures(Contract.Result<int>() == 1); return object.ReferenceEquals(holder.First, holder.Second) ? 0 : 1;", WorkerClaimOutcome.Unknown,
            members: "public sealed class Holder { public System.Collections.Generic.Queue<int> First; public System.Collections.Generic.Queue<string> Second; }");
    }

    [TestCase("Queue")]
    [TestCase("List")]
    [TestCase("Stack")]
    public async Task DistinctClosedGenericCollectionsCannotHaveTheSameIdentity(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        await CheckInterfaceArrayContract("System.Collections.Generic." + family + "<int> first, System.Collections.Generic." + family + "<string> second",
            "Contract.Requires(first != null && second != null); Contract.Ensures(Contract.Result<int>() == 1); return object.ReferenceEquals(first, second) ? 0 : 1;", WorkerClaimOutcome.Proven);
    }
}
