using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class ComplexSideEffectsTests
{
    [Test]
    public async Task MethodWithComplexSideEffects_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;
using System.Linq;



public class TestClass
{
    private static int counter;
    private readonly List<int> cache = new();

    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}<T>(
        IEnumerable<T> source, 
        Func<T, int> selector)
    {
        foreach (var item in source)
        {
            var value = selector(item);
            if (value % 2 == 0)
            {
                cache.Add(value); // Side effect: modifying instance state
                counter++; // Side effect: modifying static state
                yield return value;
            }
            else
            {
                var temp = new List<int> { value };
                yield return temp.First(); // Pure operation
            }
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MethodWithNestedClosures_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;
using System.Linq;



public class TestClass
{
    private int state;

    [EnforcePure]
    public Func<int, int> {|SP0002:TestMethod|}(int multiplier)
    {
        int LocalFunction(int x)
        {
            state++; // Side effect in local function
            return x * multiplier;
        }

        return x =>
        {
            var result = LocalFunction(x);
            state += result; // Side effect in lambda
            return result;
        };
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MethodWithMixedPureAndImpureOperations_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;
using System.Linq;



public class TestClass
{
    private readonly Dictionary<int, int> cache = new();

    [EnforcePure]
    public int {|SP0002:TestMethod|}(IEnumerable<int> numbers)
    {
        // Pure operations
        var result = numbers
            .Where(x => x > 0)
            .Select(x => x * x)
            .OrderBy(x => x)
            .Take(5)
            .Sum();

        // Impure operation mixed in
        if (!cache.ContainsKey(result))
        {
            cache[result] = result; // Side effect
        }

        // More pure operations
        return (int)Math.Sqrt(result);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}