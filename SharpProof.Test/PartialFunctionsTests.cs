using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
[Category("Partial Functions")]
public class PartialFunctionsTests
{
    [Test]
    public async Task TestPartialFunction_ThrowsException_ReportsDiagnostics()
    {
        var testCode = @"
#nullable enable
using System; // Added for ArgumentNullException
using SharpProof.Attributes; // Added for [EnforcePure]

public class TestClass
{
    [EnforcePure]
    // Throwing an exception is an impure operation (side effect)
    public int {|SP0002:IdentityOrThrowIfNull|}(int? input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }
        return input.Value;
    }

    // Example usage propagates the throw impurity from the helper.
    [EnforcePure]
    public void {|SP0002:UseMethod|}()
    {
        try
        {
           var result = IdentityOrThrowIfNull(5);
           var result2 = IdentityOrThrowIfNull(null); // This line would throw at runtime
        }
        catch (ArgumentNullException)
        {
           // Expected for null input
        }
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }
}