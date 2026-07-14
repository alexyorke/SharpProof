using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class DelegateArgumentDispatchTests
{
    [Test]
    public async Task LinqWhereWithUnresolvedPredicateParameter_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}(IEnumerable<int> values, Func<int, bool> predicate)
    {
        return values.Where(predicate);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ListExistsWithUnresolvedPredicateParameter_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(List<int> values, Predicate<int> predicate)
    {
        return values.Exists(predicate);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayExistsWithUnresolvedPredicateParameter_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(int[] values, Predicate<int> predicate)
    {
        return Array.Exists(values, predicate);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayExistsWithPureLambda_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(int[] values)
    {
        return Array.Exists(values, static value => value > 0);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayFindIndexWithUnresolvedPredicateParameter_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(int[] values, Predicate<int> predicate)
    {
        return Array.FindIndex(values, predicate);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayFindIndexWithPureLambda_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] values)
    {
        return Array.FindIndex(values, static value => value > 0);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayTrueForAllWithUnresolvedPredicateParameter_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(int[] values, Predicate<int> predicate)
    {
        return Array.TrueForAll(values, predicate);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayTrueForAllWithPureLambda_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(int[] values)
    {
        return Array.TrueForAll(values, static value => value > 0);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ListExistsWithPureLambda_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(List<int> values)
    {
        return values.Exists(static value => value > 0);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ListTrueForAllWithUnresolvedPredicateParameter_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(List<int> values, Predicate<int> predicate)
    {
        return values.TrueForAll(predicate);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ListTrueForAllWithPureLambda_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(List<int> values)
    {
        return values.TrueForAll(static value => value > 0);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}