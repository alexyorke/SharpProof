using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class ClaimsPrincipalTests
{
    private static readonly ImmutableArray<MetadataReference> ClaimsFrameworkReferences =
        AnalyzerTestHost.GetMinimalFrameworkReferences().Add(
            MetadataReference.CreateFromFile(typeof(ClaimsPrincipal).Assembly.Location));

    [Test]
    public async Task ClaimsPrincipalIsInRole_Diagnostic()
    {
        var test = @"
using System.Security.Claims;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ClaimsPrincipal principal)
    {
        return principal.IsInRole(""admin"");
    }
}";

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            test,
            frameworkReferences: ClaimsFrameworkReferences,
            concurrentAnalysis: true);
        var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, "SP0002");
        Assert.That(diagnostic.GetMessage(), Does.Contain("TestMethod"));
    }
}