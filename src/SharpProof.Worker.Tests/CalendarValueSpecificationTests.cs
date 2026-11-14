using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Date and time values: a user-defined operator declared in another assembly
// lowers as a call to its operator method, and the default catalog bounds the
// documented pure getters and comparison operators of DateTime, DateOnly,
// TimeOnly, TimeSpan and DateTimeOffset.
[TestFixture]
public sealed class CalendarValueSpecificationTests
{
    private const string Prefix = "using System; using SharpProof.Attributes; public static class C { private static int s_mark; ";

    [TestCase("[EnforcePure] public static bool Target(DateTime a, DateTime b) => a > b;")]
    [TestCase("[EnforcePure] public static bool Target(DateTime a, DateTime b) => a == b || a <= b;")]
    [TestCase("[EnforcePure] public static bool Target(DateOnly a, DateOnly b) => a > b;")]
    [TestCase("[EnforcePure] public static bool Target(TimeOnly a, TimeOnly b) => a >= b;")]
    [TestCase("[EnforcePure] public static bool Target(TimeSpan a, TimeSpan b) => a != b;")]
    [TestCase("[EnforcePure] public static int Target(DateOnly date) => date.Day + date.Month;")]
    [TestCase("[EnforcePure] public static long Target(DateTime date) => date.Ticks + date.Year;")]
    [TestCase("[EnforcePure] public static bool Target(DateTime date) => date.Kind != DateTimeKind.Local;")]
    [TestCase("[EnforcePure] public static int Target(DateTimeOffset input) => input.UtcDateTime.Day;")]
    [TestCase("[EnforcePure] public static int Target(DateTime a, DateTime b) { var later = a > b; return later ? a.Hour : b.Minute; }")]
    public async Task DocumentedPureCalendarMembersProvePurity(string member)
    {
        var result = await VerifyPurityAsync(member);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    // An operator no longer closes the body, so a definite violation beside
    // it is refuted. A replayed witness stops at the first violating site; it
    // never runs through an opaque call site, cataloged or not, so a write
    // after the comparison stays unreplayable.
    [TestCase("[EnforcePure] public static bool Target(DateTime a, DateTime b) { s_mark = 1; return a > b; }", true)]
    [TestCase("[EnforcePure] public static bool Target(DateTime a, DateTime b) { var later = a > b; s_mark = 1; return later; }", false)]
    public async Task AStaticWriteBesideACatalogedComparisonIsAViolation(string member, bool replayable)
    {
        var result = await VerifyPurityAsync(member);
        Assert.That(result.Outcome, replayable ? Is.TypeOf<RefutedOutcome>() : Is.Null, result.Reason.ToString());
    }

    // Each of these reaches a member that is not modeled: an operator the
    // catalog does not list, a validating constructor, the clock, culture
    // formatting, a lifted operator, and a generic-math
    // operator. None may prove, and none has a definite violation.
    [TestCase("[EnforcePure] public static bool Target(DateTimeOffset a, DateTimeOffset b) => a > b;")]
    [TestCase("[EnforcePure] public static bool Target(DateTimeOffset a, DateTimeOffset b) { var later = a > b; s_mark = 1; return later; }")]
    [TestCase("[EnforcePure] public static DateTime Target(int year) => new DateTime(year, 1, 1);")]
    [TestCase("[EnforcePure] public static TimeSpan Target(DateTime a, DateTime b) => a - b;")]
    [TestCase("[EnforcePure] public static int Target() => DateTime.UtcNow.Day;")]
    [TestCase("[EnforcePure] public static string Target(DateTime date) => date.ToString(\"d\");")]
    [TestCase("[EnforcePure] public static bool Target(DateTime? a, DateTime? b) => a > b;")]
    [TestCase("[EnforcePure] public static bool Target<T>(T a, T b) where T : System.Numerics.IComparisonOperators<T, T, bool> => a > b;")]
    public async Task UnmodeledCalendarAndOperatorFormsStayUnknown(string member)
    {
        var result = await VerifyPurityAsync(member);
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>().And.Not.TypeOf<RefutedOutcome>(), result.Reason.ToString());
    }

    // An operator declared on a source readonly struct is inlined.
    [Test]
    public async Task SourceReadonlyStructOperatorIsInlined()
    {
        var result = await VerifyPurityAsync("[EnforcePure] public static bool Target(Money a, Money b) => a > b; } public readonly struct Money { public readonly int Cents; " +
            "public static bool operator >(Money a, Money b) => a.Cents > b.Cents; public static bool operator <(Money a, Money b) => a.Cents < b.Cents;");
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    private static async Task<PassiveCallableCheckResult> VerifyPurityAsync(string member)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Prefix + member + " }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
        return await NativeEffectSiteVerifier.VerifyPurityAsync(preparations.Single(), new WorkerBudgets(),
            cancellationToken: TestContext.CurrentContext.CancellationToken);
    }
}
