using System.CodeDom.Compiler;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Test;

[TestFixture]
public class CodeDomProviderTests
{
    [Test]
    public async Task CodeDomProvider_CreateProvider_Diagnostic()
    {
        var test = @"
#nullable enable
using System.CodeDom.Compiler;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public CodeDomProvider {|SP0002:TestMethod|}()
    {
        return CodeDomProvider.CreateProvider(""CSharp"");
    }
}";

        var verifier = new VerifyCS.Test
        {
            TestCode = test
        };

        verifier.TestState.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));
        verifier.TestState.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(typeof(PureAttribute).Assembly.Location));
        verifier.TestState.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(typeof(CodeDomProvider).Assembly.Location));

        await verifier.RunAsync();
    }

    [Test]
    public async Task CompilerResults_ErrorsGetter_Diagnostic()
    {
        var test = @"
#nullable enable
using System.CodeDom.Compiler;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public CompilerErrorCollection {|SP0002:TestMethod|}(CompilerResults results)
    {
        return results.Errors;
    }
}";

        var verifier = new VerifyCS.Test
        {
            TestCode = test
        };

        verifier.TestState.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));
        verifier.TestState.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(typeof(PureAttribute).Assembly.Location));
        verifier.TestState.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(typeof(CodeDomProvider).Assembly.Location));

        await verifier.RunAsync();
    }
}