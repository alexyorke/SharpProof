using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;
using SharpProof.Symbolic;
using static SharpProof.Test.AnalyzerTestHost;
using SymbolicCapability = SharpProof.Symbolic.SymbolicCapability;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class CapabilityContractTests
    {
        [Test]
        public async Task AllowedCapabilitiesAttributeOnAccessor_NoPlacementDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value
    {
        [Impure]
        [AllowedCapabilities(SharpProofCapability.None)]
        get => 42;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AllowedCapabilitiesAttributeOnProperty_PlacementDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0017:AllowedCapabilities(SharpProofCapability.None)|}]
    public int Value => 42;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AllowedCapabilities_None_ConsoleWrite_ReportsViolation()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void TestMethod()
    {
        {|SP0015:Console.WriteLine(""hello"")|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AllowedCapabilities_Console_AllowsConsoleWrite()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.Console)]
    public void TestMethod()
    {
        Console.WriteLine(""hello"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AllowedCapabilities_None_DynamicInvocation_ReportsUnknown()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void TestMethod(dynamic value)
    {
        {|SP0016:value.ToString()|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AllowedCapabilities_None_TransitiveSourceCallee_ReportsCallSiteViolation()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void TestMethod()
    {
        {|SP0015:Helper()|};
    }

    [Impure]
    private static void Helper()
    {
        Console.WriteLine(""hello"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AllowedCapabilities_None_OpenVirtualSourceCallee_ReportsUnknown()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class Worker
{
    public virtual void Work()
    {
    }
}

public sealed class ConsoleWorker : Worker
{
    public override void Work()
    {
        Console.WriteLine(""hello"");
    }
}

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void TestMethod(Worker worker)
    {
        {|SP0016:worker.Work()|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CapabilityViolationDiagnostic_IncludesStructuredProperties()
        {
            var diagnostics = await GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void TestMethod()
    {
        Console.WriteLine(""hello"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.CapabilityViolationId);

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.CapabilityProperty], Is.EqualTo("IO, Console"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.CapabilityOperationKindProperty], Is.EqualTo("Invocation"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.CapabilitySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public void QueryCapabilities_FileRead_ReturnsIoAndFileRead()
        {
            const string source = """
using System.IO;

public static class C
{
    public static string Read(string path)
    {
        return File.ReadAllText(path);
    }
}
""";

            var result = QueryCapabilitiesAtMarker(source, "ReadAllText");

            Assert.That(result.Capabilities.HasFlag(SymbolicCapability.IO), Is.True);
            Assert.That(result.Capabilities.HasFlag(SymbolicCapability.FileRead), Is.True);
            Assert.That(result.CapabilityText, Does.Contain("FileRead"));
        }

        [Test]
        public void QueryCapabilities_DynamicInvocation_ReturnsUnknownReason()
        {
            const string source = """
public static class C
{
    public static string Read(dynamic value)
    {
        return value.ToString();
    }
}
""";

            var result = QueryCapabilitiesAtMarker(source, "value.ToString()");

            Assert.That(result.HasUnknowns, Is.True);
            Assert.That(result.Sites, Has.Some.Matches<SymbolicCapabilitySite>(site => site.UnknownReason == SymbolicCapabilityUnknownReason.DynamicDispatch));
        }

        [Test]
        public void QueryCapabilities_TransitiveSourceCall_MarksSiteAsTransitive()
        {
            const string source = """
using System;

public static class C
{
    public static void Outer()
    {
        Helper();
    }

    private static void Helper()
    {
        Console.WriteLine("hello");
    }
}
""";

            var result = QueryCapabilitiesAtMarker(source, "Helper();");

            Assert.That(result.Capabilities.HasFlag(SymbolicCapability.Console), Is.True);
            Assert.That(result.Sites, Has.Some.Matches<SymbolicCapabilitySite>(site => site.IsTransitive && site.SymbolDisplayName.Contains("Helper", System.StringComparison.Ordinal)));
        }

        [Test]
        public void QueryCapabilities_OpenVirtualSourceCall_ReturnsDynamicDispatchUnknown()
        {
            const string source = """
using System;

public class Worker
{
    public virtual void Work()
    {
    }
}

public sealed class ConsoleWorker : Worker
{
    public override void Work()
    {
        Console.WriteLine("hello");
    }
}

public static class C
{
    public static void Outer(Worker worker)
    {
        worker.Work();
    }
}
""";

            var result = QueryCapabilitiesAtMarker(source, "worker.Work();");

            Assert.That(result.HasUnknowns, Is.True);
            Assert.That(result.Sites, Has.Some.Matches<SymbolicCapabilitySite>(site => site.UnknownReason == SymbolicCapabilityUnknownReason.DynamicDispatch));
        }

        [Test]
        public void QueryCapabilities_SealedReceiverSourceOverride_AnalyzesImplementation()
        {
            const string source = """
using System;

public abstract class Worker
{
    public abstract void Work();
}

public sealed class ConsoleWorker : Worker
{
    public override void Work()
    {
        Console.WriteLine("hello");
    }
}

public static class C
{
    public static void Outer(ConsoleWorker worker)
    {
        worker.Work();
    }
}
""";

            var result = QueryCapabilitiesAtMarker(source, "worker.Work();");

            Assert.That(result.HasUnknowns, Is.False);
            Assert.That(result.Capabilities.HasFlag(SymbolicCapability.Console), Is.True);
            Assert.That(result.Sites, Has.Some.Matches<SymbolicCapabilitySite>(site => site.IsTransitive && site.SymbolDisplayName.Contains("ConsoleWorker.Work", System.StringComparison.Ordinal)));
        }

        private static SymbolicCapabilityResult QueryCapabilitiesAtMarker(
            string source,
            string marker)
        {
            var position = source.IndexOf(marker, System.StringComparison.Ordinal);
            if (position < 0)
            {
                throw new System.InvalidOperationException("Marker was not found in source.");
            }

            return new SymbolicQueryService().QueryCapabilities(
                new SymbolicCapabilityRequest(
                    SymbolicSourceInput.FromText(source, "CapabilityTests.cs"),
                    SymbolicQueryTarget.Position(position)));
        }

        [Test]
        public void QueryCapabilities_AllLinesTarget_ThrowsNotSupportedException()
        {
            var ex = Assert.Throws<NotSupportedException>(() => new SymbolicQueryService().QueryCapabilities(
                new SymbolicCapabilityRequest(
                    SymbolicSourceInput.FromText("class C { }", "CapabilityTests.cs"),
                    SymbolicQueryTarget.AllLines())));

            Assert.That(ex!.Message, Is.EqualTo("Capability queries support point, position, line, or node targets only."));
        }
    }
}
