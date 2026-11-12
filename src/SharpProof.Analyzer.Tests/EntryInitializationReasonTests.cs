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

    // NodaTime's HebrewScripturalCalculator.IsLeapYear and Humanizer's
    // IcelandicNumberToWordsConverter.IsAndSplitNeeded: trivially pure bodies
    // in types whose static initializer calls code.
    [TestCase("public static class Subject { private static readonly int[] Cache = Create(); private static int[] Create() => new int[3]; " +
        "[EnforcePure] public static bool Target(int year) => ((year * 7) + 1) % 19 < 7; }")]
    [TestCase("public static class Subject { private static readonly System.Collections.Generic.Dictionary<int, int> Map = new() { { 1, 2 } }; " +
        "[EnforcePure] public static bool Target(int number) => number <= 20; }")]
    // An admitted initializer still allocates, which an allocation claim counts.
    [TestCase("public static class Subject { private static readonly object Gate = new object(); " +
        "[ZeroAllocations] public static int Target(int x) => x; }")]
    [TestCase("public static class Subject { public static int State = 1; " +
        "[AllowedCapabilities(SharpProofCapability.None)] public static int Target(int x) => x; }")]
    [TestCase("public static class Outer { private static readonly int[] Cache = Create(); private static int[] Create() => null; " +
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

    // Constants stored into readonly statics are not an entry effect, an
    // initializer that only constructs fresh values is none for purity and
    // capability claims, and a violating site is reported as itself.
    [TestCase("public static class Subject { private static readonly int Limit = 7; private const int Cycle = 19; " +
        "[EnforcePure] public static bool Target(int year) => year % Cycle < 7; }", new string[0])]
    [TestCase("public static class Subject { static Subject() { } [ZeroAllocations] public static int Target(int x) => x; }", new string[0])]
    // MoreLINQ's IdFn.
    [TestCase("public static class Subject { private static readonly string[] OrdinalNumbers = [\"First\", \"Second\"]; " +
        "[EnforcePure] public static T Target<T>(T x) => x; }", new string[0])]
    // NodaTime's Era.ToString: an instance method of a type whose static
    // properties hold constructed instances.
    [TestCase("public sealed class Subject { public static Subject Common { get; } = new Subject(\"CE\"); " +
        "private Subject(string name) { Name = name; } public string Name { get; } " +
        "[EnforcePure] public override string ToString() => Name; }", new string[0])]
    [TestCase("public static class Outer { private static readonly int[] Cache = new int[3]; " +
        "public static class Subject { [AllowedCapabilities(SharpProofCapability.None)] public static int Target(int x) => x; } }", new string[0])]
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
