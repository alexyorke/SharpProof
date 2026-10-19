using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [Test]
    public async Task ReferenceEqualsApiPreservesRepeatedDefaultComparerIdentity()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<bool>() == false); " +
            "var first = System.Collections.Generic.Comparer<int>.Default; var second = System.Collections.Generic.Comparer<int>.Default; " +
            "return object.ReferenceEquals(first, second);", WorkerClaimOutcome.Refuted, "bool");
    }

    [Test]
    public async Task ReferenceEqualsApiDistinguishesEqualWidthDefaultComparerTypes()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<bool>() == true); return object.ReferenceEquals(" +
            "System.Collections.Generic.Comparer<char>.Default, System.Collections.Generic.Comparer<ushort>.Default);",
            WorkerClaimOutcome.Refuted, "bool");
    }
    [TestCase("object left, object right", "object.ReferenceEquals(left, right)", "left == right")]
    [TestCase("object left", "object.ReferenceEquals(left, left)", "true")]
    [TestCase("object left", "object.ReferenceEquals(null, left)", "left == null")]
    [TestCase("", "object.ReferenceEquals(null, null)", "true")]
    [TestCase("int[] values", "object.ReferenceEquals(values, values)", "true")]
    public async Task ReferenceEqualsApiModelsIdentityAndNulls(string parameters, string expression, string expected)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(expected);
        await CheckInterfaceArrayContract(parameters, "Contract.Ensures(Contract.Result<bool>() == (" + expected + ")); return " + expression + ";",
            WorkerClaimOutcome.Proven, "bool");
    }

    [Test]
    public async Task ReferenceEqualsApiIsAvailableInContractClauses()
    {
        await CheckInterfaceArrayContract("object left, object right",
            "Contract.Ensures(Contract.Result<bool>() == object.ReferenceEquals(left, right)); return left == right;", WorkerClaimOutcome.Proven, "bool");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReferenceEqualsApiEvaluatesNamedArgumentsInSourceOrder(bool readBeforeAssignment)
    {
        var arguments = readBeforeAssignment ? "objB: value, objA: (value = first)" : "objB: (value = first), objA: value";
        var expected = readBeforeAssignment ? "first == second" : "true";
        await CheckInterfaceArrayContract("object first, object second",
            "Contract.Ensures(Contract.Result<bool>() == (" + expected + ")); var value = second; return object.ReferenceEquals(" + arguments + ");",
            WorkerClaimOutcome.Proven, "bool");
    }

    [Test]
    public async Task ReferenceEqualsApiDoesNotInvokeEqualityCallbacks()
    {
        const string members = "public sealed class Trick { public override bool Equals(object other) { throw new System.InvalidOperationException(); } " +
            "public override int GetHashCode() => 0; public static bool operator ==(Trick a, Trick b) => false; public static bool operator !=(Trick a, Trick b) => true; }";
        await CheckInterfaceArrayContract("Trick value", "Contract.Ensures(Contract.Result<bool>()); return object.ReferenceEquals(value, value);",
            WorkerClaimOutcome.Proven, "bool", members);
    }

    [Test]
    public async Task ReferenceEqualsApiHasNoEffectsForExistingReferences()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "object left, object right",
            "return object.ReferenceEquals(left, right);", true, true, true, "bool");
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task ReferenceEqualsApiRetainsArgumentFaults(bool explicitMessage)
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<bool>()); try { return object.ReferenceEquals(Throw(), null); } " +
            "catch (System.InvalidOperationException) { return true; }", WorkerClaimOutcome.Proven, "bool",
            explicitMessage ? "static object Throw() { throw new System.InvalidOperationException(\"message\"); }"
                : "static object Throw() { throw new System.InvalidOperationException(); }");
    }
}
