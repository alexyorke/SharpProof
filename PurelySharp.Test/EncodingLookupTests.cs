using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class EncodingLookupTests
    {
        [Test]
        public async Task EncodingGetEncoding_Diagnostic()
        {
            var test = @"
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Encoding {|PS0002:TestMethod|}()
    {
        return Encoding.GetEncoding(""utf-8"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EncodingGetBytes_Diagnostic()
        {
            var test = @"
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|PS0002:TestMethod|}(string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EncodingUtf8Getter_Diagnostic()
        {
            var test = @"
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Encoding {|PS0002:TestMethod|}()
    {
        return Encoding.UTF8;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EncodingUtf8GetString_Diagnostic()
        {
            var test = @"
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
