using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// The worker proves purity, allocation, capability and effect-contract claims
// only over a complete effect summary at entry. When initialization the
// runtime may run on entry keeps that summary incomplete and the body has no
// violating site to report, the analyzer names the initialization instead of
// leaving the claim silently Unknown.
[TestFixture]
public sealed class EntryInitializationReasonTests
{
    private const string Usings = "using SharpProof.Attributes; ";

    // MoreLINQ's IdFn, NodaTime's HebrewScripturalCalculator.IsLeapYear and
    // Humanizer's IcelandicNumberToWordsConverter.IsAndSplitNeeded: trivially
    // pure bodies in types with a non-constant static initializer.
    [TestCase("public static class Subject { private static readonly int[] Cache = new int[3]; " +
        "[EnforcePure] public static bool Target(int year) => ((year * 7) + 1) % 19 < 7; }")]
    [TestCase("public static class Subject { private static readonly object Gate = new object(); " +
        "[EnforcePure] public static T Target<T>(T x) => x; }")]
    // NodaTime's Era.ToString: an instance method of a type whose static
    // fields hold constructed instances.
    [TestCase("public sealed class Subject { public static readonly Subject Common = new Subject(\"CE\"); " +
        "private Subject(string name) { Name = name; } public string Name { get; } " +
        "[EnforcePure] public override string ToString() => Name; }")]
    [TestCase("public static class Subject { static Subject() { } [ZeroAllocations] public static int Target(int x) => x; }")]
    [TestCase("public static class Outer { private static readonly int[] Cache = new int[3]; " +
        "public static class Subject { [AllowedCapabilities(SharpProofCapability.None)] public static int Target(int x) => x; } }")]
    public async Task ATypeInitializerIsNamed(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(Usings + source, "effects", ["SP0002", "SP0016", "SP0045", "SP0047"]);
        AnalyzerTestHost.AssertIds(diagnostics, ["SP0047"]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "Advisory:EntryInitialization:TypeInitializer");
    }

    [Test]
    public async Task AModuleInitializerIsNamed()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(Usings +
            "public static class Startup { [System.Runtime.CompilerServices.ModuleInitializer] public static void Run() { } } " +
            "public static class Subject { [EnforcePure] public static int Target(int x) => x + 1; }",
            "effects", ["SP0002", "SP0047"]);
        AnalyzerTestHost.AssertIds(diagnostics, ["SP0047"]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "Advisory:EntryInitialization:ModuleInitializer");
    }

    // Scalar constants stored into readonly statics are not an entry effect,
    // and a violating site is reported as itself.
    [TestCase("public static class Subject { private static readonly int Limit = 7; private const int Cycle = 19; " +
        "[EnforcePure] public static bool Target(int year) => year % Cycle < 7; }", new string[0])]
    [TestCase("public static class Subject { private static readonly object Gate = new object(); " +
        "[ZeroAllocations] public static int Target(int x) { var box = new object(); return x; } }", new[] { "SP0045" })]
    public async Task OtherwiseNoEntryInitializationReasonIsReported(string source, string[] expected)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(expected);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(Usings + source, "effects", ["SP0002", "SP0045", "SP0047"]);
        AnalyzerTestHost.AssertIds(diagnostics, [.. expected]);
    }
}
