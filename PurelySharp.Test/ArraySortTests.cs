using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ArraySortTests
    {
        [Test]
        public async Task ArraySortWithComparer_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(int[] values, IComparer<int> comparer)
    {
        Array.Sort(values, comparer);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ArraySortRangeWithComparer_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(int[] values, IComparer<int> comparer)
    {
        Array.Sort(values, 0, values.Length, comparer);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ArraySortWithComparison_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(int[] values, Comparison<int> comparison)
    {
        Array.Sort(values, comparison);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
