using System.Threading.Tasks;
using NUnit.Framework;

namespace SharpProof.Test
{
    [TestFixture]
    [Parallelizable(ParallelScope.Children)]
    public class VersionTests
    {
        [Test]
        public async Task VersionConstructor_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Version TestMethod()
    {
        return new Version(1, 2);
    }
}";

            await AssertNoAnalyzerDiagnosticsAsync(test);
        }

        [Test]
        public async Task VersionComparisonOperatorsAndGetters_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        var left = new Version(1, 2, 3, 4);
        var right = new Version(1, 2, 3, 4);
        return left.CompareTo(right) == 0 &&
            left.Equals(right) &&
            left == right &&
            left >= right &&
            left <= right &&
            left.Major == 1 &&
            left.Minor == 2 &&
            left.Build == 3 &&
            left.Revision == 4 &&
            left.MajorRevision >= 0 &&
            left.MinorRevision >= 0;
    }
}";

            await AssertNoAnalyzerDiagnosticsAsync(test);
        }

        private static async Task AssertNoAnalyzerDiagnosticsAsync(string source)
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, concurrentAnalysis: true);
            Assert.That(diagnostics, Is.Empty);
        }
    }
}
