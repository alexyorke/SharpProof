using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using PurelySharp.Analyzer.Engine;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ConstantsTests
    {
        [Test]
        public void StaticConstructor_DoesNotThrow_WhenInitialized()
        {
            var pureMembers = Constants.KnownPureBCLMembers;
            var impureMethods = Constants.KnownImpureMethods;

            Assert.That(pureMembers, Is.Not.Null, "KnownPureBCLMembers should be loaded.");
            Assert.That(impureMethods, Is.Not.Null, "KnownImpureMethods should be loaded.");

            var overlappingMethods = pureMembers.Intersect(impureMethods).ToList();

            Assert.That(overlappingMethods, Is.Empty,
                $"KnownImpureMethods and KnownPureBCLMembers should not overlap. Found overlaps: {string.Join(", ", overlappingMethods)}");
        }

        [Test]
        public void GuidCatalog_DoesNotMarkWholeTypeImpure()
        {
            Assert.That(Constants.KnownImpureTypeNames, Does.Not.Contain("System.Guid"));
            Assert.That(Constants.KnownImpureMethods, Does.Contain("System.Guid.NewGuid()"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain("System.Guid.Parse(string)"));
        }

        [Test]
        public void RepresentativeCatalogSignaturesResolveAgainstNet80References()
        {
            var source = @"
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;

public sealed class NameCollection : KeyedCollection<string, string>
{
    protected override string GetKeyForItem(string item) => item;
}

public static class CatalogSignatureSamples
{
    public static int Sample()
    {
        var list = new List<int>();
        var names = new NameCollection();
        list.Add(1);
        var now = DateTime.Now;
        _ = IPAddress.Loopback;
        _ = names.Contains(""alpha"");
        _ = new FileNotFoundException(""missing.txt"");
        byte[] bytes = new byte[1];
        ReadOnlySpan<byte> left = stackalloc byte[1];
        ReadOnlySpan<byte> right = stackalloc byte[1];
        _ = SHA1.HashData(bytes);
        _ = SHA256.HashData(bytes);
        _ = SHA384.HashData(bytes);
        _ = SHA512.HashData(bytes);
        _ = MD5.HashData(bytes);
        _ = CryptographicOperations.FixedTimeEquals(left, right);
        return Array.Empty<int>().Length + list.Count + now.Day;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "CatalogSignatureResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "list.Add(1)"), Is.EqualTo("System.Collections.Generic.List<T>.Add(T)"));
            Assert.That(Constants.KnownImpureMethods, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "list.Add(1)")));

            Assert.That(GetPropertySignature(compilation, syntaxTree, "DateTime.Now"), Is.EqualTo("System.DateTime.Now.get"));
            Assert.That(Constants.KnownImpureMethods, Does.Contain(GetPropertySignature(compilation, syntaxTree, "DateTime.Now")));

            Assert.That(GetPropertySignature(compilation, syntaxTree, "IPAddress.Loopback"), Is.EqualTo("System.Net.IPAddress.Loopback.get"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetPropertySignature(compilation, syntaxTree, "IPAddress.Loopback")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "Array.Empty<int>()"), Is.EqualTo("System.Array.Empty<T>()"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "Array.Empty<int>()")));

            Assert.That(GetPropertySignature(compilation, syntaxTree, "list.Count"), Is.EqualTo("System.Collections.Generic.List<T>.Count.get"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetPropertySignature(compilation, syntaxTree, "list.Count")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "names.Contains(\"alpha\")"), Is.EqualTo("System.Collections.ObjectModel.KeyedCollection<TKey, TItem>.Contains(TKey)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "names.Contains(\"alpha\")")));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new FileNotFoundException(\"missing.txt\")"), Is.EqualTo("System.IO.FileNotFoundException.FileNotFoundException(string?)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetObjectCreationSignature(compilation, syntaxTree, "new FileNotFoundException(\"missing.txt\")")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "CryptographicOperations.FixedTimeEquals(left, right)"), Is.EqualTo("System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan<byte>, System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "CryptographicOperations.FixedTimeEquals(left, right)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA1.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA256.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.MD5.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA384.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA512.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(bytes)")));
        }

        [Test]
        public void SymbolResolvedCatalogSamples_DoNotConflictBetweenPureAndImpureCatalogs()
        {
            var source = @"
using System;
using System.Collections.Generic;
using System.Net;

public static class CatalogConflictSamples
{
    public static void Sample()
    {
        var list = new List<int>();
        list.Add(1);
        _ = list.Count;
        _ = Array.Empty<int>();
        _ = DateTime.Now;
        _ = IPAddress.Loopback;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "CatalogConflictResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "list.Add(1)"), expectedPure: false, expectedImpure: true);
            AssertCatalogMembership(GetPropertySignature(compilation, syntaxTree, "DateTime.Now"), expectedPure: false, expectedImpure: true);
            AssertCatalogMembership(GetPropertySignature(compilation, syntaxTree, "IPAddress.Loopback"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "Array.Empty<int>()"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetPropertySignature(compilation, syntaxTree, "list.Count"), expectedPure: true, expectedImpure: false);
        }

        [Test]
        public void RecentGuidAndDateTimeOffsetCatalogEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;
using System.Buffers.Binary;
using System.Numerics;

public static class RecentCatalogSignatureSamples
{
    public static long Sample(Guid guid, string text, DateTimeOffset value)
    {
        _ = Guid.ParseExact(text, ""D"");
        _ = Guid.TryParse(text, out var parsed);
        _ = Guid.TryParseExact(text, ""D"", out parsed);
        _ = guid.ToString(""N"");
        _ = guid.ToByteArray();
        _ = Convert.FromBase64String(text);
        _ = Convert.FromHexString(text);
        _ = BitConverter.GetBytes(1);
        _ = text.ToCharArray();
        _ = BitOperations.IsPow2(1);
        _ = BitOperations.IsPow2(1u);
        _ = BitOperations.IsPow2(1L);
        _ = BitOperations.IsPow2(1ul);
        _ = BitOperations.LeadingZeroCount(1u);
        _ = BitOperations.LeadingZeroCount(1ul);
        _ = BitOperations.Log2(1u);
        _ = BitOperations.Log2(1ul);
        _ = BitOperations.PopCount(1u);
        _ = BitOperations.PopCount(1ul);
        _ = BitOperations.RotateLeft(1u, 1);
        _ = BitOperations.RotateLeft(1ul, 1);
        _ = BitOperations.RotateRight(1u, 1);
        _ = BitOperations.RotateRight(1ul, 1);
        _ = BitOperations.RoundUpToPowerOf2(1u);
        _ = BitOperations.RoundUpToPowerOf2(1ul);
        _ = BitOperations.TrailingZeroCount(1);
        _ = BitOperations.TrailingZeroCount(1u);
        _ = BitOperations.TrailingZeroCount(1L);
        _ = BitOperations.TrailingZeroCount(1ul);
        ReadOnlySpan<byte> bytes = stackalloc byte[8];
        _ = BinaryPrimitives.ReadInt16BigEndian(bytes);
        _ = BinaryPrimitives.ReadInt16LittleEndian(bytes);
        _ = BinaryPrimitives.ReadInt32BigEndian(bytes);
        _ = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        _ = BinaryPrimitives.ReadInt64BigEndian(bytes);
        _ = BinaryPrimitives.ReadInt64LittleEndian(bytes);
        _ = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        _ = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        _ = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        _ = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        _ = BinaryPrimitives.ReadUInt64BigEndian(bytes);
        _ = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        _ = BinaryPrimitives.ReverseEndianness((short)1);
        _ = BinaryPrimitives.ReverseEndianness((ushort)1);
        _ = BinaryPrimitives.ReverseEndianness(1);
        _ = BinaryPrimitives.ReverseEndianness(1u);
        _ = BinaryPrimitives.ReverseEndianness(1L);
        _ = BinaryPrimitives.ReverseEndianness(1ul);
        var fromSeconds = DateTimeOffset.FromUnixTimeSeconds(0);
        var added = value.AddDays(1);
        return added.ToUnixTimeMilliseconds() + value.Offset.Ticks;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "RecentCatalogSignatureResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "Guid.ParseExact(text, \"D\")"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "Guid.TryParse(text, out var parsed)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "Guid.TryParseExact(text, \"D\", out parsed)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "guid.ToString(\"N\")"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "guid.ToByteArray()"), expectedPure: true, expectedImpure: false);
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "guid.ToByteArray()")));
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "Convert.FromBase64String(text)"), expectedPure: true, expectedImpure: false);
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "Convert.FromBase64String(text)")));
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "Convert.FromHexString(text)"), expectedPure: true, expectedImpure: false);
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "Convert.FromHexString(text)")));
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitConverter.GetBytes(1)"), expectedPure: true, expectedImpure: false);
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "BitConverter.GetBytes(1)")));
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "text.ToCharArray()"), expectedPure: true, expectedImpure: false);
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "text.ToCharArray()")));
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1u)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1L)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1ul)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.LeadingZeroCount(1u)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.LeadingZeroCount(1ul)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.Log2(1u)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.Log2(1ul)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.PopCount(1u)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.PopCount(1ul)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateLeft(1u, 1)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateLeft(1ul, 1)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateRight(1u, 1)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateRight(1ul, 1)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RoundUpToPowerOf2(1u)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RoundUpToPowerOf2(1ul)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1u)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1L)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1ul)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadInt16BigEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadInt16LittleEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadInt32BigEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadInt32LittleEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadInt64BigEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadInt64LittleEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadUInt16BigEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadUInt16LittleEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadUInt32BigEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadUInt32LittleEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadUInt64BigEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReadUInt64LittleEndian(bytes)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReverseEndianness((short)1)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReverseEndianness((ushort)1)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReverseEndianness(1)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReverseEndianness(1u)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReverseEndianness(1L)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "BinaryPrimitives.ReverseEndianness(1ul)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "DateTimeOffset.FromUnixTimeSeconds(0)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "value.AddDays(1)"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "added.ToUnixTimeMilliseconds()"), expectedPure: true, expectedImpure: false);
            AssertCatalogMembership(GetPropertySignature(compilation, syntaxTree, "value.Offset"), expectedPure: true, expectedImpure: false);
        }

        private static void AssertCatalogMembership(string signature, bool expectedPure, bool expectedImpure)
        {
            Assert.That(Constants.KnownPureBCLMembers.Contains(signature), Is.EqualTo(expectedPure), signature);
            Assert.That(Constants.KnownImpureMethods.Contains(signature), Is.EqualTo(expectedImpure), signature);
            Assert.That(expectedPure && expectedImpure, Is.False, "Test sample should not intentionally expect a catalog conflict: " + signature);
        }

        private static string GetInvocationSignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText)
        {
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == expressionText);
            var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol;
            Assert.That(symbol, Is.Not.Null, "Invocation should resolve: " + expressionText);
            return symbol!.OriginalDefinition.ToDisplayString();
        }

        private static string GetObjectCreationSignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText)
        {
            var objectCreation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(node => node.ToString() == expressionText);
            var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(objectCreation).Symbol;
            Assert.That(symbol, Is.Not.Null, "Object creation should resolve: " + expressionText);
            return symbol!.OriginalDefinition.ToDisplayString();
        }

        private static string GetPropertySignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText)
        {
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == expressionText);
            var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol;
            Assert.That(symbol, Is.Not.Null, "Property should resolve: " + expressionText);

            var signature = symbol!.OriginalDefinition.ToDisplayString();
            return signature.EndsWith(".get", StringComparison.Ordinal) || signature.EndsWith(".set", StringComparison.Ordinal)
                ? signature
                : signature + ".get";
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToImmutableArray();
        }
    }
}
