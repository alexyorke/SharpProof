using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

// The initializer shapes the 16-source census found most often behind
// static reads and source calls, classified directly. Near misses that can
// throw, read ambient state or load another assembly stay Effectful.
[TestFixture]
internal sealed class TypeInitializerAdmissionTests
{
    private const string Shared = """
        public static class Other
        {
            public static int[] Table { get; } = new int[2];
        }
        public static class Settable
        {
            public static int[] Mutable { get; set; } = new int[2];
        }
        public static class Ambient
        {
            public static readonly string Name = System.Environment.MachineName;
        }
        """;

    [TestCase("private static readonly string Text = string.Empty;", InitializationEffect.None)]
    [TestCase("private static readonly string[] Texts = [string.Empty, \"a\"];", InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.Type Key = typeof(C);", InitializationEffect.FreshValues)]
    [TestCase("private static readonly object Key = typeof(int[]);", InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.Type Key = typeof(System.Collections.Generic.List<C>);", InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.Type[] Types = System.Type.EmptyTypes;", InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.Collections.Generic.IEqualityComparer<string> Comparer = System.StringComparer.Ordinal;",
        InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.StringComparer Comparer = System.StringComparer.OrdinalIgnoreCase;", InitializationEffect.FreshValues)]
    [TestCase("private static readonly object Zero = decimal.Zero;", InitializationEffect.FreshValues)]
    [TestCase("private static readonly decimal Half = 0.5m;", InitializationEffect.FreshValues)]
    [TestCase("private static readonly int[] None = [];", InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.Globalization.IdnMapping Mapping = new();", InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.Buffers.SearchValues<char> Chars = System.Buffers.SearchValues.Create(\"<>&\");",
        InitializationEffect.FreshValues)]
    [TestCase("private static readonly int[] Copy = Other.Table;", InitializationEffect.FreshValues)]
    [TestCase("public static int[] Own { get; } = new int[1]; private static readonly int[] Copy = Own;", InitializationEffect.FreshValues)]
    [TestCase("private static readonly string Separator = System.Environment.NewLine;", InitializationEffect.None)]
    [TestCase("private static readonly System.Collections.Generic.IEnumerable<string[]> None = System.Linq.Enumerable.Empty<string[]>();",
        InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.Collections.Concurrent.ConcurrentQueue<string[]> Queue = new();", InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.Collections.Generic.Dictionary<System.Type, int> Codes = " +
        "new(4) { { typeof(int), 1 }, { typeof(int?), 2 }, { typeof(string[]), 3 } };", InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.Collections.Generic.Dictionary<System.Type, int> Codes = new() { [typeof(int)] = 1, [typeof(int)] = 2 };",
        InitializationEffect.FreshValues)]
    [TestCase("private static readonly System.Collections.Generic.HashSet<System.Type> Scalars = new() { typeof(bool), typeof(bool), typeof(Other) };",
        InitializationEffect.FreshValues)]
    public void AdmittedShapes(string members, InitializationEffect expected)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        Assert.That(Classify("public static class C { " + members + " }", "C"), Is.EqualTo(expected));
    }

    // A culture comparer reads the ambient culture; a mutable static is an
    // ambient read; a non-constant span and a culture object read state; a
    // type of another assembly may fail to load; a type parameter is not a
    // fixed type; and another type's initializer must itself be admitted.
    [TestCase("private static readonly System.StringComparer Comparer = System.StringComparer.CurrentCulture;")]
    [TestCase("private static readonly System.StringComparer Comparer = System.StringComparer.InvariantCulture;")]
    [TestCase("private static readonly int[] Copy = Settable.Mutable;")]
    [TestCase("private static readonly string Copy = Ambient.Name;")]
    [TestCase("private static readonly System.Buffers.SearchValues<char> Chars = System.Buffers.SearchValues.Create(Ambient.Name);")]
    [TestCase("private static readonly System.Globalization.CultureInfo Culture = new(\"en-US\");")]
    [TestCase("private static readonly System.Type Key = typeof(System.Uri);")]
    [TestCase("private static readonly System.Type Key = typeof(Missing.Type);")]
    [TestCase("private static readonly string Machine = System.Environment.MachineName;")]
    [TestCase("private static readonly System.Collections.Generic.Dictionary<System.Type, int> Codes = new() { { typeof(int), 1 }, { typeof(int), 2 } };")]
    [TestCase("private static readonly System.Collections.Generic.Dictionary<System.Type, int> Codes = new() { { typeof(nint), 1 }, { typeof(System.IntPtr), 2 } };")]
    [TestCase("private static readonly System.Collections.Generic.Dictionary<System.Type, int> Codes = " +
        "new() { { typeof((int A, int B)), 1 }, { typeof((int, int)), 2 } };")]
    [TestCase("private static readonly System.Collections.Generic.Dictionary<System.Type, int> Codes = new() { { typeof(System.Uri), 1 } };")]
    [TestCase("private static readonly System.Collections.Generic.HashSet<object> Keys = new() { typeof(int) };")]
    [TestCase("private static readonly System.Collections.Concurrent.ConcurrentQueue<int> Queue = new(new[] { 1 });")]
    public void NearMissesStayEffectful(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        Assert.That(Classify("public static class C { " + members + " }", "C"), Is.EqualTo(InitializationEffect.Effectful));
    }

    [Test]
    public void TypeOfATypeParameterStaysEffectful()
    {
        Assert.That(Classify("public static class C<T> { private static readonly System.Type Key = typeof(T); }", "C`1"),
            Is.EqualTo(InitializationEffect.Effectful));
    }

    private const string Size = """
        public struct Size
        {
            public static readonly Size MinValue = FromBits(long.MinValue);
            public static readonly Size MaxValue = FromBits(long.MaxValue);
            public Size(double bytes) : this()
            {
                Bits = (long)System.Math.Ceiling(bytes * 8);
                Bytes = bytes;
                Kilobytes = -bytes / 1024;
            }
            public long Bits { get; private set; }
            public double Bytes { get; private set; }
            public double Kilobytes { get; private set; }
            public static Size FromBits(long value) { return new Size(value / (double)8); }
        }
        """;

    // Humanizer's ByteSize bounds: floating-point arithmetic, an unchecked
    // conversion to long and Math.Ceiling never throw, so the run finishes.
    [Test]
    public void FloatingPointClosedComputationIsAdmitted()
    {
        Assert.That(Classify(Size, "Size"), Is.EqualTo(InitializationEffect.None));
    }

    // A checked conversion out of range, and a conversion to decimal, throw.
    [TestCase("(long)System.Math.Ceiling(bytes * 8)", "checked((long)System.Math.Ceiling(bytes * 8))")]
    [TestCase("Bytes = bytes;", "Bytes = (double)(decimal)bytes;")]
    [TestCase("(long)System.Math.Ceiling(bytes * 8)", "(long)System.Math.Log(bytes)")]
    [TestCase("Bytes = bytes;", "Bytes = bytes > 0 ? bytes : 0;")]
    public void FloatingPointNearMissesStayEffectful(string original, string replacement)
    {
        System.ArgumentNullException.ThrowIfNull(original);
        System.ArgumentNullException.ThrowIfNull(replacement);
        Assert.That(Classify(Size.Replace(original, replacement, StringComparison.Ordinal), "Size"), Is.EqualTo(InitializationEffect.Effectful));
    }

    private const string Layouts = """
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        public struct BadLayout
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public object Reference;
            [System.Runtime.InteropServices.FieldOffset(0)] public int Number;
        }
        public struct Holder { public BadLayout Inner; }
        public static class Outer { [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] public struct Nested { } public sealed class Inside { } }
        public struct Plain { public int Value; public string Name; }
        public sealed class Constrained<T> where T : struct { }
        """;

    // A type the runtime may refuse to load: an explicit layout, directly,
    // in a field, an element or type argument, or an enclosing type; and a
    // violated generic constraint. A sequential struct loads.
    [TestCase("typeof(BadLayout)")]
    [TestCase("typeof(BadLayout[])")]
    [TestCase("typeof(Holder)")]
    [TestCase("typeof(System.Collections.Generic.IEnumerable<BadLayout>)")]
    [TestCase("typeof(Outer.Nested)")]
    [TestCase("typeof(Constrained<string>)")]
    public void TypeOfAnUnloadableTypeStaysEffectful(string typeOf)
    {
        System.ArgumentNullException.ThrowIfNull(typeOf);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Classify(Layouts + "public static class C { private static readonly System.Type Key = " + typeOf + "; }", "C"),
                Is.EqualTo(InitializationEffect.Effectful));
            Assert.That(Classify(Layouts + "public static class C { private static readonly System.Collections.Generic.HashSet<System.Type> Keys = new() { " +
                typeOf + " }; }", "C"), Is.EqualTo(InitializationEffect.Effectful));
        }
    }

    [TestCase("typeof(Plain)")]
    [TestCase("typeof(Outer.Inside)")]
    [TestCase("typeof(Constrained<int>)")]
    public void TypeOfALoadableSourceTypeIsAdmitted(string typeOf)
    {
        System.ArgumentNullException.ThrowIfNull(typeOf);
        Assert.That(Classify(Layouts + "public static class C { private static readonly System.Type Key = " + typeOf + "; }", "C"),
            Is.EqualTo(InitializationEffect.FreshValues));
    }

    // A synchronized getter takes a lock, so its read is not a field read.
    // Both the structural rules and a closed computation must see the lock.
    [TestCase("System.Array.Empty<int>()")]
    [TestCase("new int[2]")]
    public void SynchronizedGetterStaysEffectful(string value)
    {
        System.ArgumentNullException.ThrowIfNull(value);
        var source = "public static class Locked { public static int[] Table { " +
            "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)] get; } = " + value + "; } " +
            "public static class C { private static readonly int[] Copy = Locked.Table; }";
        Assert.That(Classify(source, "C"), Is.EqualTo(InitializationEffect.Effectful));
    }

    // A referenced assembly's own System.Math or System.Environment is not
    // the framework's, even when an extern alias names it unambiguously.
    [TestCase("public static class C { private static readonly double Scale = 2.0; private static readonly long Bits = (long)Fake::System.Math.Ceiling(Scale); }")]
    [TestCase("public static class C { private static readonly string Separator = Fake::System.Environment.NewLine; }")]
    [TestCase("public static class C { private static readonly System.Type[] Types = Fake::System.Type.EmptyTypes; }")]
    public void AliasedFrameworkLookalikesStayEffectful(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var token = TestContext.CurrentContext.CancellationToken;
        var fake = CSharpCompilation.Create("Fake", [CSharpSyntaxTree.ParseText("""
            namespace System
            {
                public static class Math { public static double Ceiling(double value) { throw new InvalidOperationException(); } }
                public static class Environment { public static string NewLine => throw new InvalidOperationException(); }
                public static class Type { public static readonly object[] EmptyTypes = new object[1 << 30]; }
            }
            """, cancellationToken: token)], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var reference = fake.ToMetadataReference(aliases: ["Fake"]);
        Assert.That(Classify("extern alias Fake;\n" + source, "C", reference), Is.EqualTo(InitializationEffect.Effectful));
    }

    private static InitializationEffect Classify(string source, string typeName, MetadataReference? extra = null)
    {
        var token = TestContext.CurrentContext.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(source.StartsWith("extern alias", StringComparison.Ordinal) ? source + "\n" + Shared : Shared + "\n" + source,
            cancellationToken: token);
        var compilation = CSharpCompilation.Create("TypeInitializerAdmission", [tree],
            extra == null ? TestMetadataReferences.Platform : TestMetadataReferences.Platform.Add(extra),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = compilation.GetTypeByMetadataName(typeName);
        Assert.That(type, Is.Not.Null);
        return TypeInitialization.Classify(compilation, type!, token);
    }
}
