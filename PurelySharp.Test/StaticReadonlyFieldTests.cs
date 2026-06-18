using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class StaticReadonlyFieldTests
    {
        [Test]
        public async Task FieldBackedStaticBclValues_NoDiagnostic()
        {
            var test = @"
using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public object TestMethod()
    {
        object guid = Guid.Empty;
        object duration = TimeSpan.Zero;
        object args = EventArgs.Empty;
        object dbNull = DBNull.Value;
        object any = IPAddress.Any;
        object loopback = IPAddress.Loopback;
        object version = HttpVersion.Version11;
        object missing = Missing.Value;
        return guid ?? duration ?? args ?? dbNull ?? any ?? loopback ?? version ?? missing;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
