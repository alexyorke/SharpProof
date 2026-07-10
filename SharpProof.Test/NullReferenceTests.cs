using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class NullReferenceTests
{
    [Test]
    public async Task NullReferenceCheck_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool IsNull(object? obj)
    {
        return obj == null;
    }
}
#nullable disable";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task NullReferenceAssignment_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public object? GetNull()
    {
        object? temp = null;
        return temp;
    }
}
#nullable disable";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task NullReferenceWithThrow_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(object? obj)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));
    }
}
#nullable disable";
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task NullReferenceException_ConditionalAccess_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string? s)
    {
        // ?. operator: Safe null access
        int length = s?.Length ?? 0;
        return length;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task NullReferenceException_NullCoalescing_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string? s1, string s2)
    {
        // ?? operator: Safe null handling
        string result = s1 ?? s2;
        return result;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task NullReferenceException_NullForgivingOperator_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string? s)
    {
        // ! operator itself is pure, Length is pure.
        int length = s!.Length;
        return length;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}