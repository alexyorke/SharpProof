using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

#nullable enable

namespace PurelySharp.Test
{
    [TestFixture]
    public class CryptographyTests
    {
        [Test]
        public async Task HashAlgorithm_ComputeHash_ConservativeImpure()
        {
            var test = @"
#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|PS0002:TestMethod|}(string data)
    {
        using (var sha256 = SHA256.Create()) // Factory creation and disposable use stay conservative here.
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data); // UTF-8 encoding materialization is now treated as impure.
            return sha256.ComputeHash(bytes); // Hash computation remains conservative here.
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Aes_Create_ConservativeImpure()
        {
            var test = @"
#nullable enable
using System;
using System.Security.Cryptography;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Aes {|PS0002:TestMethod|}()
    {
        return Aes.Create(); // Factory creation remains conservative here.
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RSA_Create_ConservativeImpure()
        {
            var test = @"
#nullable enable
using System;
using System.Security.Cryptography;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public RSA {|PS0002:TestMethod|}()
    {
        return RSA.Create(); // Factory creation remains conservative here.
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RandomNumberGenerator_GetInt32_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Security.Cryptography;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(int maxExclusive)
    {
        return RandomNumberGenerator.GetInt32(maxExclusive);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CryptographicOperations_FixedTimeEquals_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using System.Security.Cryptography;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Sha256_HashData_ByteArray_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System.Security.Cryptography;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(byte[] data)
    {
        return SHA256.HashData(data);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Md5_HashData_ByteArray_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System.Security.Cryptography;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(byte[] data)
    {
        return MD5.HashData(data);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Sha256_HashData_LocalReturnedByteArray_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System.Security.Cryptography;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return hash;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SignedCms_Decode_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Security.Cryptography.Pkcs;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(SignedCms cms, byte[] data)
    {
        cms.Decode(data);
    }
}";

            var pkcsAssemblyPath = Path.Combine(AppContext.BaseDirectory, "System.Security.Cryptography.Pkcs.dll");
            var verifier = new VerifyCS.Test
            {
                TestCode = test,
            };

            verifier.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location));
            verifier.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.PureAttribute).Assembly.Location));
            verifier.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(pkcsAssemblyPath));

            await verifier.RunAsync();
        }
    }
}
