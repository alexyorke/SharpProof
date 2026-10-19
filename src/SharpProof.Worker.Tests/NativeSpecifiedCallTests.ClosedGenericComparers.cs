using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    private static IEnumerable<TestCaseData> ClosedGenericIntegerComparerProofCases()
    {
        foreach (var element in ImmutableArray.Create("char", "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong"))
        {
            foreach (var local in ImmutableArray.Create(false, true))
            { yield return new TestCaseData(element, local); }
        }
    }

    [TestCaseSource(nameof(ClosedGenericIntegerComparerProofCases))]
    public async Task ClosedGenericDefaultComparersProveEqualValues(string element, bool local)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static int Compare<T>() => System.Collections.Generic.Comparer<T>.Default.Compare(default(T), default(T));";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); return Compare<" + element + ">(); " + (local ? helper : ""),
            WorkerClaimOutcome.Proven, "int", local ? "" : helper);
    }
}
