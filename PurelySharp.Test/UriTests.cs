using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class UriTests
    {
        [Test]
        public async Task UriIsWellFormedUriString_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(string value)
    {
        return Uri.IsWellFormedUriString(value, UriKind.Absolute);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UriEscapeAndUnescapeDataString_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(string value)
    {
        return Uri.UnescapeDataString(Uri.EscapeDataString(value));
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UriToString_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(Uri value)
    {
        return value.ToString();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
