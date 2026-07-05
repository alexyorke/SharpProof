using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ListSortTests
    {
        [Test]
        public async Task ListSortWithComparison_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(List<int> values, Comparison<int> comparison)
    {
        values.Sort(comparison);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ListSortWithComparer_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(List<int> values, IComparer<int> comparer)
    {
        values.Sort(comparer);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ListSortRangeWithComparer_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(List<int> values, IComparer<int> comparer)
    {
        values.Sort(0, values.Count, comparer);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
