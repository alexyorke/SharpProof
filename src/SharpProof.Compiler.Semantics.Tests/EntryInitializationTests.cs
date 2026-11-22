using System.Text;
using SharpProof.Frontend;
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
    // The runtime sets the empty string before any managed code runs.
    [TestCase("public static class Subject { private static readonly string Empty = string.Empty; }")]
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
    // A target-typed `new(...)` is the same construction as the explicit one.
    [TestCase("public sealed class Subject { public static Subject Common { get; } = new(\"CE\"); " +
        "internal Subject(string name) { Name = name; } public string Name { get; } }")]
    // Framework collections whose constructors and Add calls run no user code.
    [TestCase("public static class Subject { private static readonly System.Collections.Generic.Dictionary<int, int> Map = new() { { 1, 2 } }; }")]
    [TestCase("public static class Subject { private static readonly System.Collections.Generic.List<int> Items = new(); }")]
    [TestCase("public static class Subject { private static readonly System.Collections.Generic.HashSet<string> Names = new(System.StringComparer.Ordinal) { \"a\" }; }")]
    // A chain of source base constructors that only store into the new
    // instance: implicit, explicit with arguments, through a generic base,
    // and from the base class's own initializer.
    [TestCase("public class Base { } public sealed class Derived : Base { } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    [TestCase("public abstract class Base { protected readonly int[] Items; protected Base() { Items = new int[2]; } } " +
        "public sealed class Derived : Base { } public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    [TestCase("public abstract class Base { private readonly int size; protected Base(int size) { this.size = size; } } " +
        "public class Middle : Base { private readonly string name = \"m\"; public Middle(int size) : base(size) { } } " +
        "public sealed class Derived : Middle { public Derived() : base(4) { } } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    [TestCase("public abstract class Accessor<TKey, TValue> { private readonly TKey key; protected Accessor(TKey key) { this.key = key; } } " +
        "public sealed class Derived : Accessor<string, object> { public Derived() : base(\"k\") { } } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    [TestCase("public abstract class Subject { public static readonly Subject Unit = new Square(); } public sealed class Square : Subject { }")]
    [TestCase("public class Base { public int Value { get; } public Base() { Value = 1; } } public sealed class Derived : Base { } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    // A store into the type's own mutable static: nothing reads it before the
    // initializer finishes. It is classed with fresh values even for a
    // constant, so claims that count every static write still see one.
    [TestCase("public static class Subject { public static int State = 1; }")]
    [TestCase("public static class Subject { private static int[] Cache = new int[3]; }")]
    [TestCase("public static class Subject { public static int Value { get; set; } = 7; }")]
    [TestCase("public static class Subject { public static int Count; static Subject() { Count = 1; } }")]
    [TestCase("public static class Subject { private static System.Collections.Generic.Dictionary<int, int> map; " +
        "static Subject() { map = new System.Collections.Generic.Dictionary<int, int> { [1] = 2 }; } }")]
    public void FreshValuesAreAdmitted(string source)
    { AssertClassified(source, (int)InitializationEffect.FreshValues); }

    // Each excluded shape stays an entry effect. First, a call the
    // evaluator cannot run.
    // Dictionary.Add throws for a key it already holds.
    [TestCase("public static class Subject { private static readonly System.Collections.Generic.Dictionary<int, int> Map = new() { { 1, 2 }, { 1, 3 } }; }")]
    [TestCase("public static class Subject { private static readonly System.Collections.Generic.List<int> Items = new(System.Linq.Enumerable.Range(0, 2)); }")]
    [TestCase("public static class Subject { private static readonly System.Func<string> Text = \"x\".ToString; }")]
    // A read of another type's static whose initializer is not admitted.
    [TestCase("public static class Subject { private static readonly object Missing = System.DBNull.Value; }")]
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
    // A field-like event, or per-thread state.
    [TestCase("public static class Subject { private static event System.Action Ready = () => { }; }")]
    [TestCase("public static class Subject { [System.ThreadStatic] private static readonly int[] PerThread = new int[1]; }")]
    [TestCase("public static class Subject { [System.ThreadStatic] private static int[] perThread = new int[1]; }")]
    [TestCase("public static class Subject { [System.ThreadStatic] private static int counter; static Subject() { counter = 1; } }")]
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
    // A base constructor chain that does more than store into the new
    // instance, or that this file does not declare.
    [TestCase("public class Base { public static int Count; public Base() { Count++; } } public sealed class Derived : Base { } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    [TestCase("public sealed class Derived : System.Collections.Generic.List<int> { } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    [TestCase("public class Base { ~Base() { } } public sealed class Derived : Base { } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    [TestCase("public abstract class Base { private readonly int size; protected Base(int size) { this.size = size; } } " +
        "public sealed class Derived : Base { public Derived() : base(System.Environment.ProcessorCount) { } } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    // A base constructor's store to a virtual property runs the override.
    [TestCase("public static class Counter { public static int Count; } " +
        "public class Base { public virtual int Value { get; set; } public Base() { Value = 1; } } " +
        "public sealed class Derived : Base { public override int Value { get => 0; set => Counter.Count++; } } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    // Running the base constructor may run the base class's initializer.
    [TestCase("public class Base { [System.ThreadStatic] public static int State = 1; } public sealed class Derived : Base { } " +
        "public static class Subject { private static readonly Derived Instance = new Derived(); }")]
    public void EffectfulInitializersAreNotAdmitted(string source)
    { AssertClassified(source, (int)InitializationEffect.Effectful); }

    // Closed computations the structural rules do not admit, which the
    // evaluator runs to completion without allocating.
    [TestCase("public static class Subject { private static readonly int Limit; static Subject() { var local = 1; Limit = local; } }")]
    [TestCase("public static class Other { public static readonly int[] Data = Make(); private static int[] Make() => null; } " +
        "public static class Subject { private static readonly int[] Copy = Other.Data; }")]
    [TestCase("public struct Meters { public static implicit operator Meters(int value) => default; } " +
        "public static class Subject { private static readonly Meters Length = 5; }")]
    [TestCase("public static class Outer { private static readonly int[] Cache = Make(); private static int[] Make() => null; public static class Subject { } }")]
    // The shape of NodaTime's value types: constants checked by a helper,
    // stored through a validating constructor, with debug-only checks.
    [TestCase(Checks + "public readonly struct Subject { private const int Max = 18 * 3600; private readonly int seconds; " +
        "public static readonly Subject Zero = FromSeconds(0); public static readonly Subject MaxValue = FromSeconds(Max); " +
        "public static Subject FromSeconds(int seconds) { Checks.Range(seconds, -Max, Max); return new Subject(seconds); } " +
        "private Subject(int seconds) { Checks.Debug(seconds); this.seconds = seconds; } }")]
    [TestCase("public readonly struct Duration { public const int MinDays = -(1 << 24); private readonly int days; private readonly long nanos; " +
        "internal Duration(int days, long nanos) { if (days < MinDays) { throw new System.ArgumentOutOfRangeException(); } this.days = days; this.nanos = nanos; } } " +
        "public readonly struct Subject { private readonly Duration duration; public static Subject MinValue { get; } = new Subject(Duration.MinDays, 0); " +
        "internal Subject(int days, long nanos) => duration = new Duration(days, nanos); }")]
    [TestCase("public readonly struct Subject { private readonly long ticks; public static Subject Midnight { get; } = new Subject(0, 0); " +
        "public static Subject Noon { get; } = new Subject(12, 0); public Subject(int hour, int minute) { " +
        "if (hour < 0 || hour > 23 || minute < 0 || minute > 59) { throw new System.ArgumentOutOfRangeException(); } " +
        "ticks = unchecked(hour * 3600L + minute * 60L) * 10000000L; } }")]
    // Integer arithmetic at the edges of its range: an unchecked wrap, a
    // shift, and checked operations that fit.
    [TestCase("public static class Subject { private static readonly int Wrapped = Make(); " +
        "private static int Make() { var x = int.MaxValue; x = unchecked(x + 1); return x >> 31; } }")]
    [TestCase("public static class Subject { private static readonly long Total = Sum(10); " +
        "private static long Sum(int n) { long total = 0; for (var i = 1; i <= n; i++) { total = checked(total + i * (long)i); } return total; } }")]
    public void ClosedComputationsAreEvaluated(string source)
    { AssertClassified(source, (int)InitializationEffect.None); }

    // Closed computations that allocate.
    [TestCase("public static class Subject { private static readonly int[] Cache = Create(); private static int[] Create() => new int[3]; }")]
    [TestCase("public sealed class Pair { private readonly int left; public Pair(int left) { this.left = left; } public Pair() : this(0) { } } " +
        "public static class Subject { private static readonly Pair Instance = new Pair(); }")]
    // NodaTime's Duration bounds: BigInteger arithmetic on constants, then
    // conversions that would throw only out of range.
    [TestCase("using System.Numerics; public static class Subject { private const int MinDays = -(1 << 24); private const long PerDay = 86400L * 1000000000L; " +
        "internal static BigInteger MinNanoseconds { get; } = (BigInteger)MinDays * PerDay; " +
        "internal static BigInteger MaxNanoseconds { get; } = (-MinDays + BigInteger.One) * PerDay - BigInteger.One; " +
        "internal static decimal MinDecimal { get; } = (decimal)MinNanoseconds; private static readonly double MaxDouble = (double)MaxNanoseconds; " +
        "private static readonly System.Int128 MinWide = (System.Int128)MinNanoseconds; }")]
    [TestCase("public static class Subject { private static readonly int[] Table = Build(); " +
        "private static int[] Build() { var table = new int[12]; for (var i = 0; i < table.Length; i++) { table[i] = i * 31; } return table; } }")]
    // BigInteger arithmetic past the inline range allocates.
    [TestCase("using System.Numerics; public static class Subject { private static readonly BigInteger Big = Make(); " +
        "private static BigInteger Make() { var x = BigInteger.One; for (var i = 0; i < 70; i++) { x = x + x; } return x; } }")]
    public void AllocatingClosedComputationsAreFreshValues(string source)
    { AssertClassified(source, (int)InitializationEffect.FreshValues); }

    // Each of these throws when its type initializes, or never finishes.
    [TestCase(Checks + "public readonly struct Subject { private readonly int seconds; " +
        "public static readonly Subject TooBig = FromSeconds(100000); " +
        "public static Subject FromSeconds(int seconds) { Checks.Range(seconds, -64800, 64800); return new Subject(seconds); } " +
        "private Subject(int seconds) { this.seconds = seconds; } }")]
    [TestCase("public static class Subject { private static readonly int Overflow = Make(); private static int Make() { var x = int.MaxValue; return checked(x + 1); } }")]
    [TestCase("public static class Subject { private static readonly int Quotient = Divide(0); private static int Divide(int d) => 10 / d; }")]
    [TestCase("public static class Subject { private static readonly int Quotient = Divide(-1); private static int Divide(int d) => int.MinValue / d; }")]
    [TestCase("public static class Subject { private static readonly int Remainder = Mod(-1); private static int Mod(int d) => int.MinValue % d; }")]
    [TestCase("public static class Subject { private static readonly int Narrowed = Narrow(300); private static byte Narrow(int x) => checked((byte)x); }")]
    [TestCase("public static class Subject { private static readonly int[] Table = Build(); " +
        "private static int[] Build() { var table = new int[2]; table[2] = 1; return table; } }")]
    [TestCase("using System.Numerics; public static class Subject { private static readonly decimal TooLarge = (decimal)(BigInteger.One << 100); }")]
    [TestCase("using System.Numerics; public static class Subject { private static readonly long TooLarge = (long)(BigInteger.One << 70); }")]
    [TestCase("public static class Subject { private static readonly int Loop = Spin(); private static int Spin() { var i = 0; while (true) { i++; } } }")]
    // C# resolves an assigned array element before the value, and a
    // compound assignment or increment resolves it once.
    [TestCase("public static class Subject { private static readonly int Value = Make(); " +
        "private static int Make() { var a = new int[2]; var i = 0; a[i] = (i = 1); return 1 / (a[0] - 1); } }")]
    [TestCase("public static class Subject { private static readonly int Value = Make(); " +
        "private static int Make() { var a = new int[2]; var i = 0; a[i++] += 1; return 1 / (a[0] - 1); } }")]
    [TestCase("public static class Subject { private static readonly int Value = Make(); " +
        "private static int Make() { var a = new int[2]; var i = 0; a[i++]++; return 1 / (a[0] - 1); } }")]
    // A covariant array store of another sealed type throws.
    [TestCase("public sealed class First { } public sealed class Second { } public static class Subject { " +
        "private static readonly object[] Items = Make(); private static object[] Make() { object[] a = new First[1]; a[0] = new Second(); return a; } }")]
    // A struct's type initializer runs before its instance members, and
    // this one throws.
    [TestCase("public readonly struct Fragile { static Fragile() { throw new System.InvalidOperationException(); } public int Get() => 1; } " +
        "public static class Subject { private static readonly int Value = default(Fragile).Get(); }")]
    [TestCase("public readonly struct Fragile { static Fragile() { throw new System.InvalidOperationException(); } public int Count { get; } } " +
        "public static class Subject { private static readonly int Value = default(Fragile).Count; }")]
    public void ThrowingClosedComputationsAreEffectful(string source)
    { AssertClassified(source, (int)InitializationEffect.Effectful); }

    // Each of these depends on something other than constants: floating
    // point in a branch, library state, a virtual call, or another
    // initialization's mutable object; or it boxes, handles exceptions or
    // runs generic code.
    [TestCase("public static class Subject { private static readonly int Sign = Make(0.1 + 0.2); private static int Make(double x) => x > 0.3 ? 1 : 0; }")]
    [TestCase("public static class Subject { private static readonly int Length = Make(); private static int Make() => System.Environment.NewLine.Length; }")]
    [TestCase("public static class Subject { private static readonly string Text = Make(); private static string Make() => \"a\" + 1; }")]
    [TestCase("public abstract class Shape { public abstract int Sides(); } public sealed class Square : Shape { public override int Sides() => 4; } " +
        "public static class Subject { private static readonly int Count = Make(new Square()); private static int Make(Shape shape) => shape.Sides(); }")]
    [TestCase("public static class Other { public static readonly int[] Data = new int[2]; } " +
        "public static class Subject { private static readonly int First = Other.Data[0]; }")]
    [TestCase("public static class Subject { private static readonly object Boxed = Make(); private static object Make() => 5; }")]
    [TestCase("public static class Subject { private static readonly int Value = Make(); " +
        "private static int Make() { try { return 1; } catch (System.Exception) { return 2; } } }")]
    [TestCase("public static class Subject { private static readonly int Value = Make<int>(); private static int Make<T>() => 1; }")]
    // A settable property of another initialization's object may have
    // changed since.
    [TestCase("public sealed class Holder { public int Value { get; set; } } public static class Other { public static readonly Holder Shared = new Holder(); } " +
        "public static class Subject { private static readonly int Value = 1 / (1 - Other.Shared.Value); }")]
    // Overlapping fields share storage.
    [TestCase("[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] public struct Union { " +
        "[System.Runtime.InteropServices.FieldOffset(0)] public int A; [System.Runtime.InteropServices.FieldOffset(0)] public int B; " +
        "public Union(int x) { A = 0; B = x; } } public static class Subject { private static readonly int Value = 1 / (1 - new Union(1).A); }")]
    // Operands that grow past the size bound give up instead of running out
    // of memory.
    [TestCase("using System.Numerics; public static class Subject { private static readonly BigInteger Huge = Make(); " +
        "private static BigInteger Make() { var x = BigInteger.One + BigInteger.One; for (var i = 0; i < 40; i++) { x = x * x; } return x; } }")]
    public void OpenComputationsAreEffectful(string source)
    { AssertClassified(source, (int)InitializationEffect.Effectful); }

    private const string Checks = "internal static class Checks { " +
        "internal static void Range(int value, int min, int max) { if (value < min || value > max) { Throw(value); } } " +
        "private static void Throw(int value) => throw new System.ArgumentOutOfRangeException(nameof(value)); " +
        "[System.Diagnostics.Conditional(\"DEBUG\")] internal static void Debug(int value) { if (value > 0) { throw new System.Exception(); } } } ";

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
