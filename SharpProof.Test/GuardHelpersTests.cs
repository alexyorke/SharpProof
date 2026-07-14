using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Test;

[TestFixture]
public class GuardHelpersTests
{
    [Test]
    public async Task ArgumentException_And_ArgumentOutOfRange_ThrowHelpers_ArePure()
    {
        var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void CheckObject(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
    }

    [EnforcePure]
    public void CheckString(string s)
    {
        ArgumentException.ThrowIfNullOrEmpty(s);
        ArgumentException.ThrowIfNullOrWhiteSpace(s);
    }

    [EnforcePure]
    public void CheckNumber(int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        ArgumentOutOfRangeException.ThrowIfZero(n);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(n, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(n, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(n, 0);
    }
}";

        var t = new VerifyCS.Test
        {
            TestCode = testCode
        };
        t.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        t.TestState.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));
        t.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(PureAttribute).Assembly.Location));
        await t.RunAsync();
    }
}