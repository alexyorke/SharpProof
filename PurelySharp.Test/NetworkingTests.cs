using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

#nullable enable

namespace PurelySharp.Test
{
    [TestFixture]
    public class NetworkingTests
    {
        [Test]
        public async Task HttpContentHeadersContentLength_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Net.Http.Headers;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long? {|PS0002:TestMethod|}(HttpContentHeaders headers)
    {
        return headers.ContentLength;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task HttpResponseMessageIsSuccessStatusCode_NoDiagnostic()
        {
            var test = @"
using System.Net.Http;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(HttpResponseMessage response)
    {
        return response.IsSuccessStatusCode;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task HttpRequestMessageConstructor_Diagnostic()
        {
            var test = @"
using System.Net.Http;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public HttpRequestMessage {|PS0002:TestMethod|}()
    {
        return new HttpRequestMessage(HttpMethod.Get, ""https://example.com"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringContentConstructor_Diagnostic()
        {
            var test = @"
using System.Net.Http;
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public StringContent {|PS0002:TestMethod|}()
    {
        return new StringContent(""payload"", Encoding.UTF8, ""text/plain"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CookieConstructor_Diagnostic()
        {
            var test = @"
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Cookie {|PS0002:TestMethod|}()
    {
        return new Cookie(""name"", ""value"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task HttpClientConstructor_Diagnostic()
        {
            var test = @"
using System.Net.Http;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public HttpClient {|PS0002:TestMethod|}()
    {
        return new HttpClient();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SocketAsyncEventArgsAcceptSocket_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Net.Sockets;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Socket? {|PS0002:TestMethod|}(SocketAsyncEventArgs args)
    {
        return args.AcceptSocket;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task IPAddressLoopback_NoDiagnostic()
        {
            var test = @"
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IPAddress TestMethod()
    {
        return IPAddress.Loopback;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task IPAddressParse_NoDiagnostic()
        {
            var test = @"
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IPAddress TestMethod(string value)
    {
        return IPAddress.Parse(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task IPAddressParseReadOnlySpan_NoDiagnostic()
        {
            var test = @"
using System;
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IPAddress TestMethod(ReadOnlySpan<char> value)
    {
        return IPAddress.Parse(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task IPAddressIsLoopback_NoDiagnostic()
        {
            var test = @"
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(IPAddress address)
    {
        return IPAddress.IsLoopback(address);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }











    }
}
