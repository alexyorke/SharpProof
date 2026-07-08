using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using static SharpProof.Test.AnalyzerTestHost;

namespace SharpProof.Test
{
    [TestFixture]
    [Parallelizable(ParallelScope.Children)]
    public class UnsafeTests
    {
        private static readonly ImmutableArray<MetadataReference> UnsafeFrameworkReferences =
            GetMinimalFrameworkReferences().Add(
                MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.Unsafe).Assembly.Location));

        [Test]
        public async Task UnsafeReadUnaligned_NoDiagnostic()
        {
            var test = @"
using System.Runtime.CompilerServices;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        byte[] bytes = new byte[] { 1, 0, 0, 0 };
        return Unsafe.ReadUnaligned<int>(ref bytes[0]);
    }
}";

            await AssertUnsafeNoDiagnosticsAsync(test);
        }

        [Test]
        public async Task UnsafeAs_NoDiagnostic()
        {
            var test = @"
using System.Runtime.CompilerServices;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ref int value)
    {
        ref int alias = ref Unsafe.As<int, int>(ref value);
        return alias;
    }
}";

            await AssertUnsafeNoDiagnosticsAsync(test);
        }

        [Test]
        public async Task UnsafeSizeOf_NoDiagnostic()
        {
            var test = @"
using System.Runtime.CompilerServices;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Unsafe.SizeOf<int>();
    }
}";

            await AssertUnsafeNoDiagnosticsAsync(test);
        }

        [Test]
        public async Task UnsafeWriteUnaligned_Diagnostic()
        {
            var test = @"
using System.Runtime.CompilerServices;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(ref byte value)
    {
        Unsafe.WriteUnaligned(ref value, 42);
    }
}";

            await AssertUnsafeSinglePurityDiagnosticAsync(test);
        }

        private static async Task AssertUnsafeNoDiagnosticsAsync(string source)
        {
            var diagnostics = await GetDiagnosticsAsync(
                source,
                frameworkReferences: UnsafeFrameworkReferences,
                concurrentAnalysis: true);
            Assert.That(diagnostics, Is.Empty);
        }

        private static async Task AssertUnsafeSinglePurityDiagnosticAsync(string markedSource)
        {
            var (source, expectedSpanText) = AnalyzerTestHost.StripSp0002Markup(markedSource);
            Assert.That(expectedSpanText, Is.Not.Null);

            var diagnostics = await GetDiagnosticsAsync(
                source,
                frameworkReferences: UnsafeFrameworkReferences,
                concurrentAnalysis: true);
            var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostics, Has.Length.EqualTo(1));
            var actualSpanText = source.Substring(
                diagnostic.Location.SourceSpan.Start,
                diagnostic.Location.SourceSpan.Length);
            Assert.That(actualSpanText, Is.EqualTo(expectedSpanText));
        }
    }
}
