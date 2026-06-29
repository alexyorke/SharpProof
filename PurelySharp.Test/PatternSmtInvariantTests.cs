using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class PatternSmtInvariantTests
    {
        [Test]
        public async Task NegatedDeclarationPatternEarlyExit_FeedsBindingFacts()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(object value)
    {
        if (value is not string text)
        {
            return;
        }

        if (text == null)
        {
            Console.WriteLine(text);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DeclarationPatternBindingReassignedBeforeGuard_RemainsReachable()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(object value)
    {
        if (value is string text)
        {
            text = null;
            if (text == null)
            {
                Console.WriteLine(text);
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RecursivePropertyPatternBindingReassignedBeforeGuard_RemainsReachable()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Box
{
    [Pure]
    public string Value { get; init; }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(Box box)
    {
        if (box is { Value: { } text })
        {
            text = null;
            if (text == null)
            {
                Console.WriteLine(text);
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementPatternBindingReassignedBeforeGuard_RemainsReachable()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(object value)
    {
        switch (value)
        {
            case string text:
                text = null;
                if (text == null)
                {
                    Console.WriteLine(text);
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
