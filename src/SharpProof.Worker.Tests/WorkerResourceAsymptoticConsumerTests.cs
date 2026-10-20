using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
{
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
            bound.Asymptotic = parent;
            var lower = parent.Lower!;
            switch (mutation)
            {
                case 0:
                    parent.Lower = null;
                    break;
                case 1:
                    lower.Model = "foreign";
                    break;
                case 2:
                    lower.InferredBound = null;
                    break;
                case 3:
                    lower.LowerBound = "Theta(n^2)";
                    break;
                case 4:
                    lower.TightBound = null;
                    break;
                case 5:
                    lower.Growth = null;
                    break;
                case 6:
                    lower.Growth!.Lower = lower;
                    break;
                case 7:
                    lower.Growth!.IsZero = true;
                    break;
                case 8:
                    lower.Growth!.PolynomialDegree = 3;
                    lower.Growth.UpperBound = "O(n^3)";
                    break;
                case 9:
                    lower.Growth!.InputOrdinal = 1;
                    break;
                case 10:
                    lower.UnknownReason = "contradictory";
                    break;
                case 11:
                    lower.Growth!.UnknownReason = "unproved";
                    break;
                case 12:
                    lower.Growth!.LogarithmicDegree = null;
                    break;
                case 13:
                    lower.InferredBound = new string((char)10, 1);
                    break;
                case 14:
                    lower.TightBound = "Theta(1)";
                    break;
                case 15:
                    lower.Growth!.Model = "foreign";
                    break;
                default:
                    lower.TightUnknownReason = "unproved";
                    break;
            }
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False, "mutation " + mutation);
        }
        bound.Asymptotic = original;
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
            bound.Asymptotic = growth;
            switch (mutation)
            {
                case 0:
                    bound.Asymptotic = null;
                    break;
                case 1:
                    growth.Model = "unowned";
                    break;
                case 2:
                    growth.PolynomialDegree = -1;
                    break;
                case 3:
                    growth.LogarithmicDegree = WorkerResourceModels.MaximumAsymptoticDegree + 1;
                    break;
                case 4:
                    growth.IsZero = true;
                    break;
                case 5:
                    growth.UpperBound = "Theta(n^2)";
                    break;
                case 6:
                    growth.InputOrdinal = null;
                    break;
                case 7:
                    growth.InputKind = null;
                    break;
                case 8:
                    growth.InputName = null;
                    break;
                case 9:
                    growth.UnknownReason = "contradictory";
                    break;
                case 10:
                    growth.InputName = new string((char)10, 1);
                    break;
                case 11:
                    growth.InputOrdinal = -1;
                    break;
                case 12:
                    growth.InputOrdinal = null;
                    growth.InputKind = null;
                    growth.InputName = null;
                    break;
                case 13:
                    growth.IsZero = null;
                    break;
                case 14:
                    growth.PolynomialDegree = null;
                    break;
                case 15:
                    growth.UpperBound = null;
                    break;
                case 16:
                    growth.InputKind = "Unspecified";
                    break;
                case 17:
                    growth.InputName = new string('x', 16385);
                    break;
                default:
                    growth.UpperBound = "O(n)";
                    break;
            }
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False, "mutation " + mutation);
        }
        bound.Asymptotic = original;
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
            bound.Asymptotic = growth;
            switch (mutation)
            {
                case 0:
                    growth.UnknownReason = "";
                    break;
                case 1:
                    growth.PolynomialDegree = 0;
                    break;
                case 2:
                    growth.InputOrdinal = 0;
                    break;
                case 3:
                    growth.UpperBound = "O(1)";
                    break;
                case 4:
                    growth.IsZero = false;
                    break;
                case 5:
                    growth.Model = "unowned";
                    break;
                case 6:
                    growth.UnknownReason = new string((char)10, 1);
                    break;
                default:
                    growth.UnknownReason = new string('x', 257);
                    break;
            }
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False, "mutation " + mutation);
        }
        bound.Asymptotic = original;
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
