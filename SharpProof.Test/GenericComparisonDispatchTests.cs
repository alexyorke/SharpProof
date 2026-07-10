using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class GenericComparisonDispatchTests
{
    [Test]
    public async Task SortedSetContainsWithUnresolvedGenericElement_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass<T>
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(SortedSet<T> values, T value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ComparerDefaultWithUnresolvedGenericElement_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass<T>
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(T left, T right)
    {
        return Comparer<T>.Default.Compare(left, right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqOrderByWithUnresolvedGenericElement_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass<T>
{
    [EnforcePure]
    public IOrderedEnumerable<T> {|SP0002:TestMethod|}(IEnumerable<T> values)
    {
        return values.OrderBy(static value => value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}