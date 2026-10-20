using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase("ICollection")]
    [TestCase("IList")]
    public async Task ArrayBackedCollectionCountProvesLengthAndEffectClaims(string collection)
    {
        var expression = "((System.Collections.Generic." + collection + "<int>)values).Count";
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null); Contract.Ensures(Contract.Result<int>() == values.Length); return " + expression + ";",
            WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] values",
            "Contract.Requires(values != null); return " + expression + ";", true, true, true);
    }

    [TestCase("int", "int")]
    [TestCase("long", "long")]
    [TestCase("bool", "bool")]
    [TestCase("char", "char")]
    [TestCase("uint", "uint")]
    public async Task ArrayBackedListReadsProveContentsAndEffects(string element, string result)
    {
        var body = "Contract.Requires(values != null && index >= 0 && index < values.Length); " +
            "return ((System.Collections.Generic.IList<" + element + ">)values)[index];";
        await CheckInterfaceArrayContract(element + "[] values, int index", body.Replace("return ",
            "Contract.Ensures(Contract.Result<" + result + ">() == values[index]); return ", StringComparison.Ordinal),
            WorkerClaimOutcome.Proven, result);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]",
            element + "[] values, int index", body, true, true, true, result);
    }

    [TestCase("int")]
    [TestCase("long")]
    [TestCase("bool")]
    [TestCase("char")]
    [TestCase("uint")]
    public async Task ArrayBackedListStoreProvesStoredValueAndRetainsMutationEffects(string element)
    {
        var store = "((System.Collections.Generic.IList<" + element + ">)values)[index] = replacement;";
        const string requires = "Contract.Requires(values != null && index >= 0 && index < values.Length); ";
        var parameters = element + "[] values, int index, " + element + " replacement";
        await CheckInterfaceArrayContract(parameters,
            requires + "Contract.Ensures(Contract.Result<" + element + ">() == replacement); " + store + " return values[index];",
            WorkerClaimOutcome.Proven, element);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", parameters,
            requires + store + " return values[index];", true, true, false, element);
    }

    [Test]
    public async Task ArrayBackedListReadAndStoreKeepEvaluationOrder()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length > 0); " +
            "Contract.Ensures(Contract.Result<int>() == unchecked(values[0] + 100)); " +
            "int index = 0; return unchecked(((System.Collections.Generic.IList<int>)values)[index++] + index * 100);",
            WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length > 0); Contract.Ensures(Contract.Result<int>() == 101); " +
            "int index = 0; ((System.Collections.Generic.IList<int>)values)[index++] = index; return values[0] * 100 + index;",
            WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task InterfaceArrayStoresDoNotRetroactivelyChangeEarlierReads()
    {
        await CheckInterfaceArrayContract("int[] values, int[] alias, int replacement",
            "Contract.Requires(values != null && values.Length > 0 && alias == values); " +
            "Contract.Ensures(Contract.Result<int>() == Contract.Old(values[0])); " +
            "int before = ((System.Collections.Generic.IList<int>)values)[0]; " +
            "((System.Collections.Generic.IList<int>)alias)[0] = replacement; return before;", WorkerClaimOutcome.Proven);
    }

    [TestCase("((System.Collections.Generic.ICollection<int>)values).Count")]
    [TestCase("((System.Collections.Generic.IList<int>)values)[-1]")]
    [TestCase("((System.Collections.Generic.IList<int>)values)[int.MaxValue]")]
    public async Task ArrayBackedInterfaceNullAndBoundsFaultsAreNotErased(string expression)
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow]", "int[] values", "return " + expression + ";", false, null, null);
    }

    [Test]
    public async Task ArrayBackedInterfaceRejectsAnIncorrectResultClaim()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null); Contract.Ensures(Contract.Result<int>() == -1); " +
            "return ((System.Collections.Generic.ICollection<int>)values).Count;", WorkerClaimOutcome.Refuted);
    }

    [TestCase("System.Collections.Generic.ICollection<int>", "values.Count")]
    [TestCase("System.Collections.Generic.IList<int>", "values[0]")]
    [TestCase("string[]", "((System.Collections.Generic.IList<object>)values)[0] == null ? 0 : 1")]
    public async Task UnknownDispatchAndCovariantArraysRemainConservative(string type, string expression)
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow]", type + " values",
            "Contract.Requires(values != null); return " + expression + ";", false, null, null);
    }


    [Test]
    public async Task InterfaceArrayStoreEvaluatesItsValueBeforeTheNullCheck()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values == null); Contract.Ensures(Contract.Result<int>() == 1); int evaluated = 0; " +
            "try { ((System.Collections.Generic.IList<int>)values)[0] = ++evaluated; } " +
            "catch (System.NullReferenceException) { return evaluated; } return evaluated;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task InterfaceArrayStoreUsesTheReceiverCapturedBeforeItsValue()
    {
        await CheckInterfaceArrayContract("int[] values, int[] other",
            "Contract.Requires(values != null && values.Length > 0 && other != null && other.Length > 0); " +
            "Contract.Ensures(Contract.Result<int>() == other[0]); var original = values; " +
            "((System.Collections.Generic.IList<int>)values)[0] = (values = other)[0]; return original[0];", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task InterfaceArrayReadRemainsUnknownAcrossAnOpaqueMutation()
    {
        await CheckInterfaceArrayContract("int[] values, System.Action<int[]> mutate",
            "Contract.Requires(values != null && values.Length > 0 && mutate != null); " +
            "Contract.Ensures(Contract.Result<int>() == values[0]); int before = ((System.Collections.Generic.IList<int>)values)[0]; " +
            "mutate(values); return before;", WorkerClaimOutcome.Unknown);
    }


    [Test]
    public async Task ArrayBackedInterfaceGettersWorkInContractClauses()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null); " +
            "Contract.Ensures(Contract.Result<int>() == ((System.Collections.Generic.ICollection<int>)values).Count); " +
            "return values.Length;", WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("int[] values, int index",
            "Contract.Requires(values != null && index >= 0 && index < values.Length); " +
            "Contract.Ensures(Contract.Result<int>() == ((System.Collections.Generic.IList<int>)values)[index]); " +
            "return values[index];", WorkerClaimOutcome.Proven);
    }


    [TestCase(true)]
    [TestCase(false)]
    public async Task InterfaceArrayFailureResourceEffectsRequireAnInvalidIndex(bool valid)
    {
        var condition = valid ? "index >= 0 && index < values.Length" : "index < 0";
        var preparation = Prepare("[ZeroAllocations, EnforcePure, AllowedCapabilities(SharpProofCapability.None)] " +
            "public static int Target(int[] values, int index) { Contract.Requires(values != null && " + condition + "); " +
            "return ((System.Collections.Generic.IList<int>)values)[index]; }");
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var capabilities = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        if (!valid)
        { Assert.That(purity.Outcome, Is.Not.TypeOf<RefutedOutcome>(), "A possible cache write must not fabricate a definite impurity."); }
        foreach (var result in ImmutableArray.Create(allocations, purity, capabilities))
        { Assert.That(result.Outcome is ProvenOutcome, Is.EqualTo(valid), result.Reason.ToString()); }
    }

    [Test]
    public async Task KnownNonWritingCallsPreserveArrayContents()
    {
        await CheckInterfaceArrayContract("int[] values, string text",
            "Contract.Requires(values != null && values.Length > 0); Contract.Ensures(Contract.Result<int>() == 0); " +
            "int before = values[0]; _ = string.IsNullOrEmpty(text); return unchecked(values[0] - before);", WorkerClaimOutcome.Proven);
    }


    [Test]
    public async Task InterfaceArrayBoundsFailureDoesNotNarrowUnknownAmbientCapabilities()
    {
        var preparation = Prepare("[AllowedCapabilities(SharpProofCapability.IO | SharpProofCapability.Randomness | " +
            "SharpProofCapability.Reflection | SharpProofCapability.Synchronization | SharpProofCapability.NativeInterop)] " +
            "public static int Target(int[] values) { Contract.Requires(values != null); " +
            "return ((System.Collections.Generic.IList<int>)values)[-1]; }");
        var result = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [TestCase("System.ArgumentOutOfRangeException", false)]
    [TestCase("System.ArgumentException", false)]
    [TestCase("System.Exception", true)]
    [TestCase("System.IndexOutOfRangeException", false)]
    [TestCase("System.ArgumentNullException", false)]
    public async Task InterfaceArrayBoundsRetainResourceCallbackExceptionUncertainty(string allowed, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(allowed);
        foreach (var access in ImmutableArray.Create(
            "return ((System.Collections.Generic.IList<int>)values)[index];",
            "((System.Collections.Generic.IList<int>)values)[index] = 37; return 0;"))
        {
            var preparation = Prepare("[AllowedExceptions(typeof(" + allowed + "))] public static int Target(int[] values, int index) { Contract.Requires(values != null); " + access + " }");
            var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(result.Outcome is ProvenOutcome, Is.EqualTo(expected), result.Reason.ToString());
        }
        Assert.That(ExplicitThrowSites.TypeName(SharpProof.Ir.IrExceptionKind.ArgumentOutOfRange), Is.EqualTo("System.ArgumentOutOfRangeException"));
    }

    [Test]
    public async Task ExactArrayInterfaceBoundsRetainIndependentNullReceiverFaults()
    {
        var preparation = Prepare("[AllowedExceptions(typeof(System.Exception))] public static int Target(int[] values, int index) { return ((System.Collections.Generic.IList<int>)values)[index]; }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        var missingNull = Prepare("[AllowedExceptions(typeof(System.ArgumentOutOfRangeException))] public static int Target(int[] values, int index) { return ((System.Collections.Generic.IList<int>)values)[index]; }");
        var rejected = await NativeExceptionEffectVerifier.VerifyAsync(missingNull, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(rejected.Outcome, Is.Not.TypeOf<ProvenOutcome>());
    }

    [TestCase("System.ArgumentOutOfRangeException")]
    [TestCase("System.ArgumentException")]
    [TestCase("System.Exception")]
    public async Task ExactArrayInterfaceBoundsDispatchToTheirRuntimeCatchHierarchy(string exception)
    {
        System.ArgumentNullException.ThrowIfNull(exception);
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null); Contract.Ensures(Contract.Result<int>() == 37); try { return ((System.Collections.Generic.IList<int>)values)[-1]; } catch (" + exception + ") { return 37; }",
            WorkerClaimOutcome.Proven);
    }
    [Test]
    public async Task OpaqueMutationsPreserveEarlierScalarSnapshots()
    {
        await CheckInterfaceArrayContract("int[] values, System.Action<int[]> mutate",
            "Contract.Requires(values != null && values.Length > 0 && mutate != null); " +
            "Contract.Ensures(Contract.Result<int>() == Contract.Old(values[0])); int before = values[0]; " +
            "mutate(values); return before;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task UntakenOpaqueMutationsPreserveArrayContents()
    {
        await CheckInterfaceArrayContract("int[] values, System.Action<int[]> mutate, bool change",
            "Contract.Requires(values != null && values.Length > 0 && mutate != null && !change); " +
            "Contract.Ensures(Contract.Result<int>() == Contract.Old(values[0])); " +
            "if (change) mutate(values); return values[0];", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ExplicitStoresRestoreCellsAfterOpaqueMutations()
    {
        await CheckInterfaceArrayContract("int[] values, System.Action<int[]> mutate, int replacement",
            "Contract.Requires(values != null && values.Length > 0 && mutate != null); " +
            "Contract.Ensures(Contract.Result<int>() == replacement); " +
            "mutate(values); values[0] = replacement; return values[0];", WorkerClaimOutcome.Proven);
    }
    [Test]
    public async Task BoundsCallbackFailuresCanReachDifferentCatchHandlers()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null); Contract.Ensures(Contract.Result<int>() == 37); " +
            "try { return ((System.Collections.Generic.IList<int>)values)[-1]; } " +
            "catch (System.ArgumentOutOfRangeException) { return 37; } catch (System.Exception) { return 0; }",
            WorkerClaimOutcome.Unknown);
    }

    [TestCase("int before = cell.Value; mutate(cell); return before;", true)]
    [TestCase("int before = cell.Value; mutate(cell); return cell.Value;", false)]
    [TestCase("if (change) mutate(cell); return cell.Value;", true)]
    [TestCase("mutate(cell); cell.Value = 17; return cell.Value;", true)]
    public async Task OpaqueFieldMutationsRespectProgramOrderAndBranchReach(string body, bool proven)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("Cell cell, System.Action<Cell> mutate, bool change",
            "Contract.Requires(cell != null && cell.Value == 17 && mutate != null && !change); " +
            "Contract.Ensures(Contract.Result<int>() == 17); " + body,
            proven ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Unknown,
            members: "public sealed class Cell { public int Value; }");
    }
    [TestCase("int", "++", "", "1")]
    [TestCase("long", "++", "", "1")]
    [TestCase("uint", "++", "", "1U")]
    [TestCase("ulong", "++", "", "1UL")]
    [TestCase("char", "++", "", "(char)1")]
    [TestCase("int", "", "++", "1")]
    [TestCase("int", "--", "", "-1")]
    [TestCase("int", "", "--", "-1")]
    public async Task InterfaceArrayIncrementsPreserveExpressionAndStoredValues(string type, string prefix, string postfix, string delta)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        System.ArgumentNullException.ThrowIfNull(prefix);
        System.ArgumentNullException.ThrowIfNull(postfix);
        System.ArgumentNullException.ThrowIfNull(delta);
        const string previous = "Contract.Old(values[0])";
        var updated = "unchecked((" + type + ")(" + previous + " + (" + delta + ")))";
        await CheckInterfaceArrayContract(type + "[] values",
            "Contract.Requires(values != null && values.Length > 0); " +
            "Contract.Ensures(Contract.Result<" + type + ">() == " + (string.IsNullOrEmpty(postfix) ? updated : previous) + " && values[0] == " + updated + "); return unchecked(" + prefix + "((System.Collections.Generic.IList<" + type + ">)values)[0]" + postfix + ");",
            WorkerClaimOutcome.Proven, type);
    }

    [TestCase("+=", "+")]
    [TestCase("-=", "-")]
    [TestCase("*=", "*")]
    [TestCase("/=", "/")]
    [TestCase("%=", "%")]
    [TestCase("&=", "&")]
    [TestCase("|=", "|")]
    [TestCase("^=", "^")]
    [TestCase("<<=", "<<")]
    [TestCase(">>=", ">>")]
    [TestCase(">>>=", ">>>")]
    public async Task InterfaceArrayCompoundOperatorsProveTheirStoredResult(string assignment, string binary)
    {
        System.ArgumentNullException.ThrowIfNull(assignment);
        System.ArgumentNullException.ThrowIfNull(binary);
        var divisionGuard = assignment is "/=" or "%=" ? "Contract.Requires(replacement != 0 && !(values[0] == int.MinValue && replacement == -1)); " : "";
        await CheckInterfaceArrayContract("int[] values, int replacement",
            "Contract.Requires(values != null && values.Length > 0); " + divisionGuard +
            "Contract.Ensures(Contract.Result<int>() == unchecked(Contract.Old(values[0]) " + binary + " replacement) && values[0] == Contract.Result<int>()); return unchecked(((System.Collections.Generic.IList<int>)values)[0] " + assignment + " replacement);",
            WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task InterfaceArrayCompoundReadPrecedesRhsMutationAndRebinding()
    {
        const string requires = "Contract.Requires(values != null && values.Length > 1); ";
        await CheckInterfaceArrayContract("int[] values, int replacement",
            requires + "Contract.Ensures(Contract.Result<int>() == unchecked(Contract.Old(values[0]) + replacement)); " +
            "((System.Collections.Generic.IList<int>)values)[0] += (values[0] = replacement); return values[0];", WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("int[] values",
            requires + "Contract.Ensures(Contract.Result<int>() == unchecked(Contract.Old(values[0]) + 1)); " +
            "int index = 0; ((System.Collections.Generic.IList<int>)values)[index] += (index = 1); return values[0];", WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("int[] values, int[] other",
            requires + "Contract.Requires(other != null && other.Length > 0); " +
            "Contract.Ensures(Contract.Result<int>() == unchecked(Contract.Old(values[0]) + Contract.Old(other[0]))); " +
            "var original = values; ((System.Collections.Generic.IList<int>)values)[0] += (values = other)[0]; return original[0];", WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("int[] values",
            requires + "Contract.Ensures(Contract.Result<int>() == 1); int index = 0; " +
            "((System.Collections.Generic.IList<int>)values)[index++]++; return index;", WorkerClaimOutcome.Proven);
    }

    [TestCase("Contract.Requires(values == null);", "System.NullReferenceException", "0")]
    [TestCase("Contract.Requires(values != null);", "System.Exception", "-1")]
    public async Task InterfaceArrayCompoundGetterFaultPrecedesRhsEvaluation(string requires, string exception, string index)
    {
        System.ArgumentNullException.ThrowIfNull(requires);
        System.ArgumentNullException.ThrowIfNull(exception);
        System.ArgumentNullException.ThrowIfNull(index);
        await CheckInterfaceArrayContract("int[] values",
            requires + "Contract.Ensures(Contract.Result<int>() == 0); int evaluated = 0; " +
            "try { ((System.Collections.Generic.IList<int>)values)[" + index + "] += ++evaluated; } catch (" + exception + ") { return evaluated; } return evaluated;",
            WorkerClaimOutcome.Proven);
    }

    [TestCase("checked(++((System.Collections.Generic.IList<int>)values)[0])", "System.OverflowException", "int.MaxValue", "0")]
    [TestCase("((System.Collections.Generic.IList<int>)values)[0] /= divisor", "System.DivideByZeroException", "17", "0")]
    [TestCase("((System.Collections.Generic.IList<int>)values)[0] /= divisor", "System.OverflowException", "int.MinValue", "-1")]
    public async Task InterfaceArrayArithmeticFaultsDoNotPerformTheSetter(string expression, string exception, string initial, string divisor)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(exception);
        System.ArgumentNullException.ThrowIfNull(initial);
        System.ArgumentNullException.ThrowIfNull(divisor);
        await CheckInterfaceArrayContract("int[] values, int divisor",
            "Contract.Requires(values != null && values.Length > 0 && values[0] == " + initial + " && divisor == " + divisor + "); " +
            "Contract.Ensures(Contract.Result<int>() == " + initial + "); try { _ = " + expression + "; } catch (" + exception + ") { return values[0]; } return values[0];",
            WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task InterfaceArrayCompoundEffectsAndWrongClaimsRetainControls()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] values",
            "Contract.Requires(values != null && values.Length > 0); return unchecked(((System.Collections.Generic.IList<int>)values)[0]++);", true, true, false);
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length > 0 && values[0] == 17); Contract.Ensures(Contract.Result<int>() == 19); " +
            "return ++((System.Collections.Generic.IList<int>)values)[0];", WorkerClaimOutcome.Refuted);
        await CheckInterfaceArrayContract("System.Collections.Generic.IList<int> values",
            "Contract.Requires(values != null); Contract.Ensures(Contract.Result<int>() == 18); return ++values[0];", WorkerClaimOutcome.Unknown);
    }
    [Test]
    public async Task InterfaceArrayMultiplicationStaysWithinNativeProofBudget()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; public static class C { public static int Target(int[] values, int replacement) { " +
            "Contract.Requires(values != null && values.Length > 0); Contract.Ensures(Contract.Result<int>() == unchecked(Contract.Old(values[0]) * replacement) && values[0] == Contract.Result<int>()); " +
            "return unchecked(((System.Collections.Generic.IList<int>)values)[0] *= replacement); } }");
        SharpProof.CompilerArtifact.CompilerManifestArtifactJson.DeserializePrepared(
            SharpProof.CompilerArtifact.CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken),
            out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparations.Single())!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, TestContext.CurrentContext.CancellationToken), Is.True,
            "construction=" + failure + "; blocks=" + candidate.Program.Blocks.Length);
        var budgets = new WorkerBudgets();
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
        var entry = await solver.VerifyEntryAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(entry.Outcome, Is.TypeOf<RefutedOutcome>(), "entry=" + entry.Reason);
        var proof = await solver.VerifyEnsuresAsync(0, TestContext.CurrentContext.CancellationToken);
        Assert.That(proof.Outcome, Is.TypeOf<ProvenOutcome>(), "proof=" + proof.Reason + "; resources=" + solver.ConsumedResourceCount);
        await TestContext.Progress.WriteLineAsync("interface multiplication native resources=" + solver.ConsumedResourceCount);
    }
    [TestCase("&=", "&")]
    [TestCase("|=", "|")]
    [TestCase("^=", "^")]
    public async Task InterfaceArrayBooleanCompoundOperatorsPreserveTheirStoredResult(string assignment, string binary)
    {
        System.ArgumentNullException.ThrowIfNull(assignment);
        System.ArgumentNullException.ThrowIfNull(binary);
        await CheckInterfaceArrayContract("bool[] values, bool replacement",
            "Contract.Requires(values != null && values.Length > 0); " +
            "Contract.Ensures(Contract.Result<bool>() == (Contract.Old(values[0]) " + binary + " replacement) && values[0] == Contract.Result<bool>()); " +
            "return ((System.Collections.Generic.IList<bool>)values)[0] " + assignment + " replacement;", WorkerClaimOutcome.Proven, "bool");
    }

    [Test]
    public async Task CheckedCompoundOverflowRetainsRhsEffectsWithoutPerformingTheSetter()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length > 0 && values[0] == int.MaxValue); Contract.Ensures(Contract.Result<int>() == 1); " +
            "try { checked { ((System.Collections.Generic.IList<int>)values)[0] += (values[0] = 1); } } catch (System.OverflowException) { return values[0]; } return values[0];",
            WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("byte[] values, byte replacement",
            "Contract.Requires(values != null && values.Length > 0 && values[0] == byte.MaxValue && replacement == 1); Contract.Ensures(Contract.Result<int>() == byte.MaxValue); " +
            "try { checked { ((System.Collections.Generic.IList<byte>)values)[0] += replacement; } } catch (System.OverflowException) { return values[0]; } return values[0];",
            WorkerClaimOutcome.Proven);
    }
    private static async Task CheckInterfaceArrayContract(string parameters, string body, WorkerClaimOutcome expected, string result = "int", string members = "")
    {
        System.ArgumentNullException.ThrowIfNull(parameters);
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(result);
        System.ArgumentNullException.ThrowIfNull(members);
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static " +
            result + " Target(" + parameters + ") { " + body + " } " + members + " }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code + ": " + error.Message)), Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expected), response.ClaimResults.Single().Reason.ToString());
    }

    private static async Task CheckInterfaceArrayEffects(string attributes, string parameters, string body,
        bool? noThrow, bool? noAllocation, bool? pure, string result = "int")
    {
        System.ArgumentNullException.ThrowIfNull(attributes);
        System.ArgumentNullException.ThrowIfNull(parameters);
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(result);
        var preparation = Prepare(attributes + " public static " + result + " Target(" + parameters + ") { " + body + " }");
        if (noThrow.HasValue)
        {
            var verified = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(verified.Outcome is ProvenOutcome, Is.EqualTo(noThrow.Value), verified.Reason.ToString());
        }
        if (noAllocation.HasValue)
        {
            var verified = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(verified.Outcome is ProvenOutcome, Is.EqualTo(noAllocation.Value), verified.Reason.ToString());
        }
        if (pure.HasValue)
        {
            var verified = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(verified.Outcome is ProvenOutcome, Is.EqualTo(pure.Value), verified.Reason.ToString());
        }
    }
}
