using NUnit.Framework;
using PurelySharp.Analyzer;
using System.Threading.Tasks;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class GenericEqualityDispatchTests
    {
        [Test]
        public async Task ListContainsWithUnresolvedGenericElement_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass<T>
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(List<T> values, T value)
    {
        return values.Contains(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task LinqContainsWithUnresolvedGenericElement_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using PurelySharp.Attributes;

public class TestClass<T>
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(IEnumerable<T> values, T value)
    {
        return values.Contains(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ICollectionContainsWithUnknownInterfaceReceiver_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(ICollection<int> values, int value)
    {
        return values.Contains(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task IListIndexOfWithUnknownInterfaceReceiver_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(IList<int> values, int value)
    {
        return values.IndexOf(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
