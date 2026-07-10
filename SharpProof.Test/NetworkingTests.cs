using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class NetworkingTests
{
    [Test]
    public async Task HttpContentHeadersContentLength_Diagnostic()
    {
        var test = @"
#nullable enable
using System.Net.Http.Headers;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long? {|SP0002:TestMethod|}(HttpContentHeaders headers)
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
using SharpProof.Attributes;

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
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public HttpRequestMessage {|SP0002:TestMethod|}()
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
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public StringContent {|SP0002:TestMethod|}()
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
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Cookie {|SP0002:TestMethod|}()
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
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public HttpClient {|SP0002:TestMethod|}()
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
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Socket? {|SP0002:TestMethod|}(SocketAsyncEventArgs args)
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
using SharpProof.Attributes;

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
using SharpProof.Attributes;

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
using SharpProof.Attributes;

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
using SharpProof.Attributes;

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

    [Test]
    public async Task IPEndPointConstructor_Diagnostic()
    {
        var test = @"
using System.Net;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IPEndPoint {|SP0002:TestMethod|}(IPAddress address)
    {
        return new IPEndPoint(address, 80);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}