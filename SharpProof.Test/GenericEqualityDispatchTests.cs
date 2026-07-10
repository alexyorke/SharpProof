using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class GenericEqualityDispatchTests
{
    [Test]
    public async Task ListContainsWithUnresolvedGenericElement_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass<T>
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(List<T> values, T value)
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
using SharpProof.Attributes;

public class TestClass<T>
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IEnumerable<T> values, T value)
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
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ICollection<int> values, int value)
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
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(IList<int> values, int value)
    {
        return values.IndexOf(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ICollectionCountWithUnknownInterfaceReceiver_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(ICollection<int> values)
    {
        return values.Count;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}