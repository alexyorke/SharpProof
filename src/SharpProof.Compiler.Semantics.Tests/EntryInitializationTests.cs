using System.Text;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Compiler.Semantics.Test;

// Entry initialization decides whether a callable's effects are complete at
// entry, so the module-initializer search must be exact: a missed
// initializer would admit an unmodeled entry effect, and a truncated search
// would leave every callable of a large assembly Unknown.
[TestFixture]
public sealed class EntryInitializationTests
{
    [TestCase("public static class Startup { [System.Runtime.CompilerServices.ModuleInitializer] public static void Run() { } }")]
    [TestCase("namespace Library; public static class Outer { internal static class Startup { " +
        "[System.Runtime.CompilerServices.ModuleInitializer] internal static void Run() { } } }")]
    [TestCase("using Init = System.Runtime.CompilerServices.ModuleInitializerAttribute; " +
        "public static class Startup { [Init] public static void Run() { } }")]
    [TestCase("using System.Runtime.CompilerServices; public static partial class Startup { [ModuleInitializer] public static partial void Run(); " +
        "public static partial void Run() { } }")]
    public void EveryDeclaredModuleInitializerIsFound(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("EntryInitialization", source);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(EntryInitialization.ModuleInitializers(compilation, TestContext.CurrentContext.CancellationToken)
            .Select(static method => method.Name).Single(), Is.EqualTo("Run"));
    }

    [Test]
    public void ALookalikeAttributeIsNotAModuleInitializer()
    {
        var compilation = TestCompilation.Create("EntryInitialization",
            "namespace Other { public sealed class ModuleInitializerAttribute : System.Attribute { } } " +
            "public static class Startup { [Other.ModuleInitializer] public static void Run() { } }");
        Assert.That(EntryInitialization.ModuleInitializers(compilation, TestContext.CurrentContext.CancellationToken), Is.Empty);
    }

    // NodaTime has about 4,500 symbols; a symbol walk capped at 4,096 used to
    // give up and report every NodaTime callable's entry as incomplete.
    [TestCase(false)]
    [TestCase(true)]
    public void TheSearchCoversAnAssemblyOfAnySize(bool declared)
    {
        var compilation = TestCompilation.Create("EntryInitialization", LargeSource(declared));
        var token = TestContext.CurrentContext.CancellationToken;
        var subject = compilation.GetTypeByMetadataName("Subject")!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(EntryInitialization.ModuleInitializers(compilation, token).Length, Is.EqualTo(declared ? 1 : 0));
            Assert.That(EntryInitialization.FindEffectGap(compilation, subject, token),
                Is.EqualTo(declared ? EntryInitializationGap.ModuleInitializer : EntryInitializationGap.None));
        }
    }

    // Constants stored into the type's own readonly statics: no code runs
    // and nothing is allocated.
    [TestCase("public static class Subject { private static readonly int Limit = 7; private const int Cycle = 19; }")]
    [TestCase("public static class Subject { private static readonly string Name = \"CE\"; private static readonly object Nothing = null; " +
        "private static readonly System.DayOfWeek Day = System.DayOfWeek.Monday; private static readonly double Ratio = 0.5; }")]
    [TestCase("public static class Subject { public static int Value { get; } = 7; }")]
    [TestCase("public static class Subject { static Subject() { } }")]
    [TestCase("public static class Subject { private static readonly int Limit; static Subject() { Limit = 3; } }")]
    [TestCase("public static class Subject { private static readonly int Limit = Other; private const int Other = 4; }")]
    public void ConstantsAreNoInitializationEffect(string source)
    { AssertClassified(source, (int)InitializationEffect.None); }

    // Each admitted shape that constructs fresh values.
    [TestCase("public static class Subject { private static readonly int[] Cache = new int[3]; }")]
    [TestCase("public static class Subject { private static readonly string[] Names = { \"First\", \"Second\" }; }")]
    [TestCase("public static class Subject { private static readonly string[] Names = [\"First\", \"Second\"]; }")]
    [TestCase("public static class Subject { private static readonly object Gate = new object(); }")]
    [TestCase("public static class Subject { private static readonly object Boxed = 5; }")]
    [TestCase("public sealed class Subject { public static Subject Common { get; } = new Subject(\"CE\", \"Eras_Common\"); " +
        "internal Subject(string name, string resource) { this.Name = name; Resource = resource; } " +
        "public string Name { get; } internal string Resource { get; } }")]
    [TestCase("public static class Subject { public static System.Func<int, int> Twice { get; } = x => x * 2; }")]
    [TestCase("public static class Subject { private static readonly System.Func<int, int> Same = Identity; private static int Identity(int x) => x; }")]
    [TestCase("public struct Point { private readonly int x; public Point(int x) { this.x = x; } } " +
        "public static class Subject { private static readonly Point Origin = new Point(0); }")]
    [TestCase("public sealed class Box { private readonly int[] items = new int[1]; private long size = 2; } " +
        "public static class Subject { private static readonly Box Empty = new Box(); }")]
    [TestCase("public static class Subject { private static readonly object Gate; static Subject() { Gate = new object(); } }")]
    [TestCase("public static class Other { public static readonly int[] Data = new int[2]; } " +
        "public static class Subject { private static readonly int[] Copy = Other.Data; }")]
    [TestCase("public static class Subject { private static readonly int[] First = new int[1]; private static readonly int[] Copy = First; }")]
    [TestCase("public static class Outer { private static readonly int[] Cache = new int[3]; public static class Subject { } }")]
    public void FreshValuesAreAdmitted(string source)
    { AssertClassified(source, (int)InitializationEffect.FreshValues); }

    // Each excluded shape stays an entry effect. First, a call in the
    // initializer.
    [TestCase("public static class Subject { private static readonly int[] Cache = Create(); private static int[] Create() => new int[3]; }")]
    [TestCase("public static class Subject { private static readonly System.Collections.Generic.Dictionary<int, int> Map = new() { { 1, 2 } }; }")]
    [TestCase("public static class Subject { private static readonly System.Collections.Generic.List<int> Items = new(); }")]
    [TestCase("public static class Subject { private static readonly System.Func<string> Text = \"x\".ToString; }")]
    // A read of another type's static whose initializer is not admitted.
    [TestCase("public static class Other { public static readonly int[] Data = Make(); private static int[] Make() => null; } " +
        "public static class Subject { private static readonly int[] Copy = Other.Data; }")]
    [TestCase("public static class Subject { private static readonly string Empty = string.Empty; }")]
    // A read of mutable or per-thread state is an ambient read, whatever its
    // type's initializer.
    [TestCase("public static class Input { public static int Current; } " +
        "public static class Subject { private static readonly int Snapshot; static Subject() { Snapshot = Input.Current; } }")]
    [TestCase("public static class Subject { public static int Count; private static readonly int Copy = Count; }")]
    [TestCase("public static class Input { [System.ThreadStatic] public static readonly int[] PerThread; } " +
        "public static class Subject { private static readonly int[] Copy = Input.PerThread; }")]
    // A throw.
    [TestCase("public static class Subject { private static readonly object Value = (object)null ?? throw new System.InvalidOperationException(); }")]
    [TestCase("public static class Subject { static Subject() { throw new System.InvalidOperationException(); } }")]
    [TestCase("public static class Subject { private static readonly byte[] Huge = new byte[1 << 20]; }")]
    // A cross-type cycle, through a read or a construction.
    [TestCase("public static class Other { public static readonly object Back = Subject.Gate; } " +
        "public static class Subject { public static readonly object Gate = Other.Back; }")]
    [TestCase("public sealed class Other { private static readonly Subject Back = new Subject(); } " +
        "public sealed class Subject { private static readonly Other Forward = new Other(); }")]
    // A static constructor with logic.
    [TestCase("public static class Subject { private static readonly int Limit; " +
        "static Subject() { if (System.Environment.ProcessorCount > 1) { Limit = 1; } } }")]
    [TestCase("public static class Subject { private static readonly int Limit; static Subject() { var local = 1; Limit = local; } }")]
    [TestCase("public static class Subject { public static int Count; static Subject() { Count = 1; } }")]
    // Mutable or per-thread state.
    [TestCase("public static class Subject { public static int State = 1; }")]
    [TestCase("public static class Subject { private static int[] Cache = new int[3]; }")]
    [TestCase("public static class Subject { public static int Value { get; set; } = 7; }")]
    [TestCase("public static class Subject { private static event System.Action Ready = () => { }; }")]
    [TestCase("public static class Subject { [System.ThreadStatic] private static readonly int[] PerThread = new int[1]; }")]
    // Constructors that do more than store into the new instance.
    [TestCase("public sealed class Counter { public static int Count; public Counter() { Count++; } } " +
        "public static class Subject { private static readonly Counter Instance = new Counter(); }")]
    [TestCase("public sealed class Counter { private static int last; public Counter() { last = 1; } } " +
        "public static class Subject { private static readonly Counter Instance = new Counter(); }")]
    [TestCase("public sealed class Counter { private readonly int value; public Counter(int value) { this.value = System.Math.Abs(value); } } " +
        "public static class Subject { private static readonly Counter Instance = new Counter(1); }")]
    [TestCase("public sealed class Counter { public int Value { get; set; } } " +
        "public static class Subject { private static readonly Counter Instance = new Counter { Value = 1 }; }")]
    [TestCase("public sealed class Handle { ~Handle() { } } public static class Subject { private static readonly Handle Instance = new Handle(); }")]
    [TestCase("public class Base { } public sealed class Derived : Base { } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    [TestCase("public sealed class Pair { private readonly int left; public Pair(int left) { this.left = left; } public Pair() : this(0) { } } " +
        "public static class Subject { private static readonly Pair Instance = new Pair(); }")]
    [TestCase("public struct Meters { public static implicit operator Meters(int value) => default; } " +
        "public static class Subject { private static readonly Meters Length = 5; }")]
    [TestCase("public static class Outer { private static readonly int[] Cache = Make(); private static int[] Make() => null; public static class Subject { } }")]
    public void EffectfulInitializersAreNotAdmitted(string source)
    { AssertClassified(source, (int)InitializationEffect.Effectful); }

    // An allocation claim counts initializer allocation; a purity claim does not.
    [TestCase(false)]
    [TestCase(true)]
    public void FreshValuesAreAGapOnlyForClaimsThatCountAllocation(bool admitFreshValues)
    {
        var compilation = TestCompilation.Create("EntryInitialization",
            "public static class Subject { private static readonly int[] Cache = new int[3]; }");
        Assert.That(EntryInitialization.FindEffectGap(compilation, compilation.GetTypeByMetadataName("Subject")!,
            TestContext.CurrentContext.CancellationToken, admitFreshValues),
            Is.EqualTo(admitFreshValues ? EntryInitializationGap.None : EntryInitializationGap.TypeInitializer));
    }

    private static void AssertClassified(string source, int expected)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("EntryInitialization", source);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var subject = compilation.GetSymbolsWithName("Subject").OfType<INamedTypeSymbol>().Single();
        Assert.That(EntryInitialization.ClassifyTypeInitialization(compilation, subject, TestContext.CurrentContext.CancellationToken),
            Is.EqualTo((InitializationEffect)expected));
    }

    // More than 4,096 symbols, with any module initializer declared last.
    internal static string LargeSource(bool moduleInitializer)
    {
        var source = new StringBuilder("public static class Subject { public static int Target(int x) => x + 1; } ");
        for (var type = 0; type < 60; type++)
        {
            source.Append("public static class Filler").Append(type).Append(" { ");
            for (var member = 0; member < 80; member++)
            { source.Append("public static int M").Append(member).Append("() => ").Append(member).Append("; "); }
            source.Append("} ");
        }
        if (moduleInitializer)
        { source.Append("public static class Startup { [System.Runtime.CompilerServices.ModuleInitializer] public static void Run() { } }"); }
        return source.ToString();
    }
}
