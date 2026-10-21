using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
{
    [Test]
    public async Task CheckedExpectedCostsReachCacheAndSarifAndRejectMalformedRecords()
    {
        using var project = TestProject.Create("using SharpProof.Attributes; public static class Subject { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedElements,0,nameof(n),7)] public static void Target(int n,[ResourceBooleanInput(1,3)] bool selected) { " +
            "for(int i=0;i<n;i++) { if(selected) {var a=new int[7];} else {var b=new int[2];} } } }");
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(first, WorkerClaimOutcome.Proven);
        AssertClaimVerdict(second, WorkerClaimOutcome.Proven);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        bool Valid()
        {
            return WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
                second.Manifest, request, second.Summary.Versions).IsValid;
        }
        Assert.That(Valid(), Is.True);
        var claim = second.ClaimResults.Single();
        var bound = claim.ResourceBound!;
        var original = bound.Expected!;
        Assert.That(original.Model, Is.EqualTo(WorkerResourceModels.Expected));
        Assert.That(original.UnknownReason, Is.Null);
        Assert.That(original.TrueWeight, Is.EqualTo(1));
        Assert.That(original.FalseWeight, Is.EqualTo(3));
        Assert.That(original.InputOrdinal, Is.EqualTo(1));
        Assert.That(original.InputName, Is.EqualTo("selected"));
        Assert.That(original.FalseUpperBound, Is.Not.EqualTo(original.TrueUpperBound));
        Assert.That(original.UpperBound, Is.Not.EqualTo(bound.InferredUpperBound));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(item => item.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        var expected = row.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("expected");
        Assert.That(expected.GetProperty("upperBound").GetString(), Is.EqualTo(original.UpperBound));
        Assert.That(expected.GetProperty("model").GetString(), Is.EqualTo(original.Model));
        var payload = JsonSerializer.SerializeToUtf8Bytes(original, WorkerProtocolJson.Options);
        var core = claim.ProofCore;
        for (var mutation = 0; mutation < 21; mutation++)
        {
            var evidence = JsonSerializer.Deserialize<WorkerResourceExpectedEvidence>(payload, WorkerProtocolJson.Options)!;
            claim.ProofCore = core;
            switch (mutation)
            {
                case 0:
                    evidence = evidence with { Model = "foreign" };
                    break;
                case 1:
                    evidence = evidence with { InputOrdinal = -1 };
                    break;
                case 2:
                    evidence = evidence with { InputName = "" };
                    break;
                case 3:
                    evidence = evidence with { InputName = new string((char)10, 1) };
                    break;
                case 4:
                    evidence = evidence with { TrueWeight = -1 };
                    break;
                case 5:
                    evidence = evidence with { TrueWeight = 0 };
                    evidence = evidence with { FalseWeight = 0 };
                    break;
                case 6:
                    evidence = evidence with { FalseWeight = 1000001 };
                    break;
                case 7:
                    evidence = evidence with { UpperBound = "" };
                    break;
                case 8:
                    evidence = evidence with { FalseUpperBound = null };
                    break;
                case 9:
                    evidence = evidence with { TrueUpperBound = new string((char)10, 1) };
                    break;
                case 10:
                    evidence = evidence with { UnknownReason = "unknown" };
                    break;
                case 11:
                    claim.ProofCore = [.. core.Where(item => item is null ? throw new ArgumentNullException(nameof(item)) : !item.StartsWith("resource-input-model:", StringComparison.Ordinal))];
                    break;
                case 12:
                    bound = bound with { Expected = null };
                    break;
                case 13:
                    evidence = evidence with { UpperBound = new string('n', 16385) };
                    break;
                default:
                    evidence = evidence with { InputName = new string('n', 257) };
                    break;
            }
            if (mutation != 12)
            { bound = bound with { Expected = evidence }; }
            second = second with { ClaimResults = [claim with { ResourceBound = bound }] };
            Assert.That(Valid(), Is.False, "mutation " + mutation);
        }
        bound = bound with { Expected = original };
        claim.ProofCore = core;
        second = second with { ClaimResults = [claim with { ResourceBound = bound }] };
        Assert.That(Valid(), Is.True);
    }

    [Test]
    public async Task CheckedAccountingReachesValidatedCacheAndSarifAndRejectsMalformedRecords()
    {
        using var project = TestProject.Create("using SharpProof.Attributes; public static class Subject { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),2)] public static void Target(int n,bool flush) { " +
            "int credit=0; for(int i=0;i<n;i++) { credit++; if(flush) { while(credit>0) {credit--;} } } } }");
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(first, WorkerClaimOutcome.Proven);
        AssertClaimVerdict(second, WorkerClaimOutcome.Proven);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
            second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        var claim = second.ClaimResults.Single();
        var bound = claim.ResourceBound!;
        Assert.That(bound.Accounting, Has.Length.EqualTo(1));
        var original = bound.Accounting[0];
        Assert.That(original.MaximumSpend, Is.EqualTo("max(0, n)"));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(item => item.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        var accounting = row.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("accounting")[0];
        Assert.That(accounting.GetProperty("model").GetString(), Is.EqualTo(WorkerResourceModels.Accounting));
        Assert.That(accounting.GetProperty("maximumSpend").GetString(), Is.EqualTo("max(0, n)"));
        var payload = JsonSerializer.SerializeToUtf8Bytes(original, WorkerProtocolJson.Options);
        var core = claim.ProofCore;
        for (var mutation = 0; mutation < 13; mutation++)
        {
            var evidence = JsonSerializer.Deserialize<WorkerResourceAccountingEvidence>(payload, WorkerProtocolJson.Options)!;
            claim.ProofCore = core;
            switch (mutation)
            {
                case 0:
                    evidence = evidence with { Model = "foreign" };
                    break;
                case 1:
                    evidence = evidence with { InitialCredit = "-1" };
                    break;
                case 2:
                    evidence = evidence with { InitialCredit = "00" };
                    break;
                case 3:
                    evidence = evidence with { DepositPerIteration = "0" };
                    break;
                case 4:
                    evidence = evidence with { DepositPerIteration = "x" };
                    break;
                case 5:
                    evidence = evidence with { CounterWidth = 16 };
                    break;
                case 6:
                    evidence = evidence with { InitialCredit = "2147483648" };
                    break;
                case 7:
                    evidence = evidence with { MaximumSpend = "" };
                    break;
                case 8:
                    evidence = evidence with { MaximumSpend = new string((char)10, 1) };
                    break;
                case 9:
                    evidence = evidence with { MaximumSpend = new string('n', 16385) };
                    break;
                case 10:
                    bound = bound with { Accounting = [] };
                    break;
                case 11:
                    bound = bound with { Accounting = Enumerable.Repeat(evidence, 17).ToArray() };
                    break;
                default:
                    claim.ProofCore = [];
                    break;
            }
            if (mutation is not (10 or 11))
            { bound = bound with { Accounting = [evidence] }; }
            second = second with { ClaimResults = [claim with { ResourceBound = bound }] };
            Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False, "mutation " + mutation);
        }
        claim.ProofCore = core;
        bound = bound with { Accounting = [original] };
        second = second with { ClaimResults = [claim with { ResourceBound = bound }] };
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NativeLowerGrowthReachesValidatedCacheAndSarifWithoutInventingTightness(bool dependent)
    {
        var inner = dependent ? "i" : "n";
        using var project = TestProject.Create("using SharpProof.Attributes; public static class Subject { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,long.MaxValue)] public static void Target(int n) { " +
            "for(int i=0;i<n;i++) { for(int j=0;j<" + inner + ";j++) {} } } }");
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(first, WorkerClaimOutcome.Proven);
        AssertClaimVerdict(second, WorkerClaimOutcome.Proven);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
            second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        var lower = second.ClaimResults.Single().ResourceBound!.Asymptotic!.Lower!;
        Assert.That(lower.LowerBound, Is.EqualTo(dependent ? "Omega(n)" : "Omega(n^2)"), lower.UnknownReason);
        Assert.That(lower.TightBound, Is.EqualTo(dependent ? null : "Theta(n^2)"));
        Assert.That(lower.TightUnknownReason, Is.EqualTo(dependent ? "ResourceTightnessUnproved" : null));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(result => result.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        var record = row.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("asymptotic").GetProperty("lower");
        Assert.That(record.GetProperty("model").GetString(), Is.EqualTo(WorkerResourceModels.NormalDomain));
        Assert.That(record.GetProperty("lowerBound").GetString(), Is.EqualTo(lower.LowerBound));
        Assert.That(record.GetProperty("tightBound").GetString(), Is.EqualTo(lower.TightBound));
    }

    [Test]
    public async Task NativeLowerGrowthProtocolRejectsContradictoryAndRecursiveRecords()
    {
        using var project = TestProject.Create(AsymptoticConsumerSource(0));
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(response, WorkerClaimOutcome.Proven);
        var bound = response.ClaimResults.Single().ResourceBound!;
        var original = bound.Asymptotic!;
        Assert.That(original.Lower!.UnknownReason, Is.Null);
        var payload = JsonSerializer.SerializeToUtf8Bytes(original, WorkerProtocolJson.Options);
        for (var mutation = 0; mutation < 17; mutation++)
        {
            var parent = JsonSerializer.Deserialize<WorkerResourceAsymptoticEvidence>(payload, WorkerProtocolJson.Options)!;
            var lower = parent.Lower!;
            switch (mutation)
            {
                case 0:
                    parent = parent with { Lower = null };
                    break;
                case 1:
                    lower = lower with { Model = "foreign" };
                    break;
                case 2:
                    lower = lower with { InferredBound = null };
                    break;
                case 3:
                    lower = lower with { LowerBound = "Theta(n^2)" };
                    break;
                case 4:
                    lower = lower with { TightBound = null };
                    break;
                case 5:
                    lower = lower with { Growth = null };
                    break;
                case 6:
                    lower = lower with { Growth = lower.Growth! with { Lower = lower } };
                    break;
                case 7:
                    lower = lower with { Growth = lower.Growth! with { IsZero = true } };
                    break;
                case 8:
                    lower = lower with { Growth = lower.Growth! with { PolynomialDegree = 3 } };
                    lower = lower with { Growth = lower.Growth! with { UpperBound = "O(n^3)" } };
                    break;
                case 9:
                    lower = lower with { Growth = lower.Growth! with { InputOrdinal = 1 } };
                    break;
                case 10:
                    lower = lower with { UnknownReason = "contradictory" };
                    break;
                case 11:
                    lower = lower with { Growth = lower.Growth! with { UnknownReason = "unproved" } };
                    break;
                case 12:
                    lower = lower with { Growth = lower.Growth! with { LogarithmicDegree = null } };
                    break;
                case 13:
                    lower = lower with { InferredBound = new string((char)10, 1) };
                    break;
                case 14:
                    lower = lower with { TightBound = "Theta(1)" };
                    break;
                case 15:
                    lower = lower with { Growth = lower.Growth! with { Model = "foreign" } };
                    break;
                default:
                    lower = lower with { TightUnknownReason = "unproved" };
                    break;
            }
            if (mutation != 0)
            { parent = parent with { Lower = lower }; }
            bound = bound with { Asymptotic = parent };
            response = response with { ClaimResults = [response.ClaimResults.Single() with { ResourceBound = bound }] };
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False, "mutation " + mutation);
        }
        bound = bound with { Asymptotic = original };
        response = response with { ClaimResults = [response.ClaimResults.Single() with { ResourceBound = bound }] };
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [TestCase(0, "O(n^2)")]
    [TestCase(1, "O(log(n))")]
    [TestCase(2, "O(n * log(n))")]
    [TestCase(3, "O(0)")]
    [TestCase(4, "O(1)")]
    [TestCase(5, "O(log(n)^2)")]
    [TestCase(6, "O(n)")]
    [TestCase(7, "O(n)")]
    [TestCase(8, "O(n)")]
    public async Task NativeAsymptoticUpperEvidenceReachesValidatedCacheAndSarif(int kind, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using var project = TestProject.Create(AsymptoticConsumerSource(kind));
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(first, WorkerClaimOutcome.Proven);
        AssertClaimVerdict(second, WorkerClaimOutcome.Proven);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
            second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        var growth = second.ClaimResults.Single().ResourceBound!.Asymptotic!;
        Assert.That(growth.UpperBound, Is.EqualTo(expected));
        Assert.That(growth.UnknownReason, Is.Null);
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(result => result.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        var record = row.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("asymptotic");
        Assert.That(record.GetProperty("upperBound").GetString(), Is.EqualTo(expected));
        Assert.That(record.GetProperty("model").GetString(), Is.EqualTo(WorkerResourceModels.Asymptotic));
    }

    [Test]
    public async Task NativeGrowthProtocolRejectsMalformedAndContradictoryKnownRecords()
    {
        using var project = TestProject.Create(AsymptoticConsumerSource(0));
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(response, WorkerClaimOutcome.Proven);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var bound = response.ClaimResults.Single().ResourceBound!;
        var original = bound.Asymptotic!;
        var payload = JsonSerializer.SerializeToUtf8Bytes(original, WorkerProtocolJson.Options);
        for (var mutation = 0; mutation < 19; mutation++)
        {
            var growth = JsonSerializer.Deserialize<WorkerResourceAsymptoticEvidence>(payload, WorkerProtocolJson.Options)!;
            switch (mutation)
            {
                case 0:
                    bound = bound with { Asymptotic = null };
                    break;
                case 1:
                    growth = growth with { Model = "unowned" };
                    break;
                case 2:
                    growth = growth with { PolynomialDegree = -1 };
                    break;
                case 3:
                    growth = growth with { LogarithmicDegree = WorkerResourceModels.MaximumAsymptoticDegree + 1 };
                    break;
                case 4:
                    growth = growth with { IsZero = true };
                    break;
                case 5:
                    growth = growth with { UpperBound = "Theta(n^2)" };
                    break;
                case 6:
                    growth = growth with { InputOrdinal = null };
                    break;
                case 7:
                    growth = growth with { InputKind = null };
                    break;
                case 8:
                    growth = growth with { InputName = null };
                    break;
                case 9:
                    growth = growth with { UnknownReason = "contradictory" };
                    break;
                case 10:
                    growth = growth with { InputName = new string((char)10, 1) };
                    break;
                case 11:
                    growth = growth with { InputOrdinal = -1 };
                    break;
                case 12:
                    growth = growth with { InputOrdinal = null };
                    growth = growth with { InputKind = null };
                    growth = growth with { InputName = null };
                    break;
                case 13:
                    growth = growth with { IsZero = null };
                    break;
                case 14:
                    growth = growth with { PolynomialDegree = null };
                    break;
                case 15:
                    growth = growth with { UpperBound = null };
                    break;
                case 16:
                    growth = growth with { InputKind = "Unspecified" };
                    break;
                case 17:
                    growth = growth with { InputName = new string('x', 16385) };
                    break;
                default:
                    growth = growth with { UpperBound = "O(n)" };
                    break;
            }
            if (mutation != 0)
            { bound = bound with { Asymptotic = growth }; }
            response = response with { ClaimResults = [response.ClaimResults.Single() with { ResourceBound = bound }] };
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False, "mutation " + mutation);
        }
        bound = bound with { Asymptotic = original };
        response = response with { ClaimResults = [response.ClaimResults.Single() with { ResourceBound = bound }] };
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public async Task NativeUnknownGrowthKeepsNumericProofAndRejectsPartialOrders()
    {
        using var project = TestProject.Create("using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,long.MaxValue)] public static void Target(int n,int m) { for(int i=0;i<n;i++) {} for(int j=0;j<m;j++) {} } }");
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(response, WorkerClaimOutcome.Proven);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var bound = response.ClaimResults.Single().ResourceBound!;
        var original = bound.Asymptotic!;
        Assert.That(original.UnknownReason, Is.EqualTo("AsymptoticMultipleInputs"));
        var payload = JsonSerializer.SerializeToUtf8Bytes(original, WorkerProtocolJson.Options);
        for (var mutation = 0; mutation < 8; mutation++)
        {
            var growth = JsonSerializer.Deserialize<WorkerResourceAsymptoticEvidence>(payload, WorkerProtocolJson.Options)!;
            switch (mutation)
            {
                case 0:
                    growth = growth with { UnknownReason = "" };
                    break;
                case 1:
                    growth = growth with { PolynomialDegree = 0 };
                    break;
                case 2:
                    growth = growth with { InputOrdinal = 0 };
                    break;
                case 3:
                    growth = growth with { UpperBound = "O(1)" };
                    break;
                case 4:
                    growth = growth with { IsZero = false };
                    break;
                case 5:
                    growth = growth with { Model = "unowned" };
                    break;
                case 6:
                    growth = growth with { UnknownReason = new string((char)10, 1) };
                    break;
                default:
                    growth = growth with { UnknownReason = new string('x', 257) };
                    break;
            }
            bound = bound with { Asymptotic = growth };
            response = response with { ClaimResults = [response.ClaimResults.Single() with { ResourceBound = bound }] };
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False, "mutation " + mutation);
        }
        bound = bound with { Asymptotic = original };
        response = response with { ClaimResults = [response.ClaimResults.Single() with { ResourceBound = bound }] };
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    private static string AsymptoticConsumerSource(int kind)
    {
        var body = kind switch
        {
            0 => "for(int i=0;i<n;i++) { for(int j=0;j<n;j++) {} }",
            1 => "checked { for(int i=1;i<n;i*=2) {} }",
            2 => "checked { for(int i=0;i<n;i++) { for(int j=1;j<n;j*=2) {} } }",
            3 => "",
            4 => "var buffer=new int[1];",
            5 => "checked { for(int i=1;i<n;i*=2) { for(int j=1;j<n;j*=2) {} } }",
            6 => "for(int i=0;i<n;i++) {}",
            7 => "for(int i=0;i<text.Length;i++) {}",
            8 => "for(int i=0;i<values.Length;i++) {}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var metric = kind == 4 ? "ArrayAllocations" : "LoopBodyEntries";
        var parameters = kind == 7 ? "string text" : kind == 8 ? "int[] values" : "int n";
        return "using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric." +
            metric + ",long.MaxValue)] public static void Target(" + parameters + ") { " + body + " } }";
    }
}
