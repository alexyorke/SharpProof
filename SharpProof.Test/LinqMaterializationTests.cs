using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class LinqMaterializationTests
{
    [Test]
    public async Task EnumerableToList_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public List<int> {|SP0002:TestMethod|}(IEnumerable<int> numbers)
        {
            return numbers.Where(x => x > 0).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumerableToArray_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public int[] {|SP0002:TestMethod|}(IEnumerable<int> numbers)
        {
            return numbers.Select(x => x * 2).ToArray();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumerableToHashSet_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public HashSet<int> {|SP0002:TestMethod|}(IEnumerable<int> numbers)
        {
            return numbers.Where(x => x > 0).ToHashSet();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumerableToDictionary_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Dictionary<int, string> {|SP0002:TestMethod|}(IEnumerable<string> numbers)
        {
            return numbers.ToDictionary(x => x.Length);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumerableToHashSetStableLocal_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public HashSet<int> {|SP0002:TestMethod|}(IEnumerable<int> numbers)
        {
            var materialized = numbers.Where(x => x > 0).ToHashSet();
            return materialized;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}