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

    [TestCase("public static class Subject { private static readonly int[] Cache = new int[3]; }")]
    [TestCase("public static class Subject { static Subject() { } }")]
    [TestCase("public static class Subject { public static int State = 1; }")]
    [TestCase("public static class Outer { private static readonly object Gate = new object(); public static class Subject { } }")]
    public void ATypeInitializerThatIsNotAScalarConstantIsAGap(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("EntryInitialization", source);
        var subject = compilation.GetSymbolsWithName("Subject").OfType<INamedTypeSymbol>().Single();
        Assert.That(EntryInitialization.FindEffectGap(compilation, subject, TestContext.CurrentContext.CancellationToken),
            Is.EqualTo(EntryInitializationGap.TypeInitializer));
    }

    [Test]
    public void AReadonlyScalarConstantIsNotAGap()
    {
        var compilation = TestCompilation.Create("EntryInitialization",
            "public static class Subject { private static readonly int Limit = 7; private const int Cycle = 19; }");
        Assert.That(EntryInitialization.FindEffectGap(compilation, compilation.GetTypeByMetadataName("Subject")!,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(EntryInitializationGap.None));
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
