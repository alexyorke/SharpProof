using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using PurelySharp.Analyzer.Engine;
using PurelySharp.Analyzer;

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
        public void GuidCatalog_UsesGeneratedEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownImpureTypeNames, Does.Not.Contain("System.Guid"));
            AssertNotInManualCatalogs("System.Guid.NewGuid()");
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.CompareTo(System.Guid)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.Equals(System.Guid)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.Guid(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.Guid(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.Parse(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.ParseExact(string, string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.TryParse(string?, out System.Guid)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.TryParseExact(string?, string?, out System.Guid)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.ToString()"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.ToString(string?)"));
        }

        [Test]
        public void ArrayEmpty_IsSourcedFromGeneratedPurityEvidence_NotTheStaticPureCatalog()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Array.Empty<T>()"));
        }

        [Test]
        public void GuidToByteArray_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.ToByteArray()"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.ToByteArray(bool)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Guid.ToByteArray()"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Guid.ToByteArray(bool)"));
        }

        [Test]
        public void GeneratedPathHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.IO.Path.Combine(string, string)",
                "System.IO.Path.HasExtension(string)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void UnresolvedPathWrappers_AreNotBackedByStaticCatalogs()
        {
            var members = new[]
            {
                "System.IO.Path.ChangeExtension(string, string)",
                "System.IO.Path.ChangeExtension(string?, string?)",
                "System.IO.Path.GetDirectoryName(string)",
                "System.IO.Path.GetDirectoryName(string?)",
                "System.IO.Path.GetExtension(string)",
                "System.IO.Path.GetExtension(string?)",
                "System.IO.Path.GetFileName(string)",
                "System.IO.Path.GetFileName(string?)",
                "System.IO.Path.GetFileNameWithoutExtension(string)",
                "System.IO.Path.GetFileNameWithoutExtension(string?)",
                "System.IO.Path.HasExtension(string)",
                "System.IO.Path.HasExtension(string?)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void GuidNewGuidAndPathEnvironmentHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Guid.NewGuid()",
                "System.IO.Path.GetFullPath(string)",
                "System.IO.Path.GetRandomFileName()",
                "System.IO.Path.GetTempFileName()",
                "System.IO.Path.GetTempPath()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void WebUtilityHelpers_AreNotBackedByStaticCatalogs()
        {
            var members = new[]
            {
                "System.Net.WebUtility.HtmlDecode(string)",
                "System.Net.WebUtility.HtmlEncode(string)",
                "System.Net.WebUtility.UrlDecode(string)",
                "System.Net.WebUtility.UrlEncode(string)",
                "System.Net.WebUtility.UrlDecodeToBytes(byte[], int, int)",
                "System.Net.WebUtility.UrlEncodeToBytes(byte[], int, int)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void CryptographyHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan<byte>, System.ReadOnlySpan<byte>)");
        }

        [Test]
        public void ListHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Generic.List<T>.Contains(T)",
                "System.Collections.Generic.List<T>.Count.get",
                "System.Collections.Generic.List<T>.Find(System.Predicate<T>)",
                "System.Collections.Generic.List<T>.Exists(System.Predicate<T>)",
                "System.Collections.Generic.List<T>.TrueForAll(System.Predicate<T>)",
                "System.Collections.Generic.List<T>.this[int].get",
                "System.Collections.Generic.List<T>.get_Count()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void MutableCollectionReadHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class MutableCollectionCatalogSignatureSamples
{
    public static int Sample(
        Dictionary<string, int> dictionary,
        HashSet<int> set,
        Queue<int> queue,
        Stack<int> stack,
        List<int> list,
        string key,
        int value)
    {
        _ = dictionary.ContainsKey(key);
        _ = dictionary.Count;
        _ = set.Contains(value);
        _ = queue.Contains(value);
        _ = queue.Peek();
        _ = stack.Contains(value);
        _ = stack.Peek();
        _ = list.BinarySearch(value);
        _ = list.IndexOf(value);
        return list.LastIndexOf(value);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "MutableCollectionCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "dictionary.ContainsKey(key)"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "dictionary.Count"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "set.Contains(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "queue.Contains(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "queue.Peek()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "stack.Contains(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "stack.Peek()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "list.BinarySearch(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "list.IndexOf(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "list.LastIndexOf(value)"));
        }

        [Test]
        public void FileNotFoundExceptionStringConstructor_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.IO.FileNotFoundException.FileNotFoundException(string?)");
        }

        [Test]
        public void PureExceptionAndAttributeConstructors_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.ArgumentOutOfRangeException.ArgumentOutOfRangeException(string)");
            AssertNotInManualCatalogs("System.AttributeUsageAttribute.AttributeUsageAttribute(System.AttributeTargets)");
            AssertNotInManualCatalogs("System.BadImageFormatException.BadImageFormatException(string)");
        }

        [Test]
        public void TupleArraySegmentAndReferenceEqualsHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("object.ReferenceEquals(object, object)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Tuple.Create"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.ValueTuple.Create"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.ArraySegment<T>.ArraySegment(T[], int, int)"));
        }

        [Test]
        public void PureCoreConstructorsAndValueTypes_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.ArgumentException.ArgumentException(string, string)",
                "System.DivideByZeroException.DivideByZeroException()",
                "System.FlagsAttribute.FlagsAttribute()",
                "System.FormatException.FormatException(string)",
                "System.Index.Index(int, bool)",
                "System.IO.EndOfStreamException.EndOfStreamException()",
                "System.InvalidOperationException.InvalidOperationException(string)",
                "System.NotImplementedException.NotImplementedException()",
                "System.NotSupportedException.NotSupportedException(string)",
                "System.ObsoleteAttribute.ObsoleteAttribute(string)",
                "System.OverflowException.OverflowException()",
                "System.PlatformNotSupportedException.PlatformNotSupportedException()",
                "System.Range.Range(System.Index, System.Index)",
                "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute.CallerArgumentExpressionAttribute(string)",
                "System.Runtime.CompilerServices.MethodImplAttribute.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions)",
                "System.SerializableAttribute.SerializableAttribute()",
                "System.UIntPtr.UIntPtr(uint)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringNullHelpers_AndOrdinalComparerGetter_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.IsNullOrEmpty(string)",
                "string.IsNullOrWhiteSpace(string)",
                "System.StringComparer.Ordinal.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringLengthAndTrimHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.Length.get",
                "string.Trim()",
                "string.TrimEnd()",
                "string.TrimStart()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringEqualsHelpers_AreSourcedFromGeneratedPurityEvidence_AndSemanticRules_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.Equals(object)",
                "string.Equals(string)",
                "string.Equals(string, string)",
                "string.Equals(string, System.StringComparison)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringInvariantCasingAndHashCodeHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.GetHashCode()",
                "string.ToLowerInvariant()",
                "string.ToUpperInvariant()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringConcatReplaceSubstringHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.Concat(string, string)",
                "string.Concat(params string[])",
                "string.Replace(string, string)",
                "string.Substring(int)",
                "string.Substring(int, int)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringSplitAndArrayJoinHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.String.Split(char)",
                "System.String.Split(char, System.StringSplitOptions)",
                "System.String.Split(params char[])",
                "System.String.Split(char[])",
                "System.String.Split(char[], System.StringSplitOptions)",
                "System.String.Split(string[], System.StringSplitOptions)",
                "System.String.Split(char[], int, System.StringSplitOptions)",
                "System.String.Split(string[], int, System.StringSplitOptions)",
                "System.String.Join(string, string[])",
                "System.String.Join(string, params string[])",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringEnumerableJoinHelpers_AreNotBackedByStaticPureCatalogs()
        {
            AssertNotInManualCatalogs("System.String.Join(string, System.Collections.Generic.IEnumerable<string>)");
            AssertNotInManualCatalogs("System.String.Join<T>(string, System.Collections.Generic.IEnumerable<T>)");
        }

        [Test]
        public void StringIndexOfCloneCompareToAndToStringHelpers_AreSourcedFromGeneratedPurityEvidence_AndSemanticRules_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.Clone()",
                "string.CompareTo(string)",
                "string.IndexOf(char)",
                "string.IndexOf(string)",
                "string.ToString()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringInsertPadLeftAndRemoveHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.Insert(int, string)",
                "string.PadLeft(int)",
                "string.Remove(int)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringPrefixSuffix_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.String.StartsWith(System.String)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.String.StartsWith(char)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.String.EndsWith(char)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.StartsWith(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.EndsWith(string)"));
        }

        [Test]
        public void StringContainsHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.Contains(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.Contains(char)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.Contains(char, System.StringComparison)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.Contains(string, System.StringComparison)"));
        }

        [Test]
        public void EncodingLookupHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.Encoding.UTF8.get"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.Encoding.GetString(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.Encoding.GetBytes(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.Encoding.GetEncoding(string)"));
        }

        [Test]
        public void StringBuilderToString_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Text.StringBuilder.ToString()");
        }

        [Test]
        public void BooleanAndCharToStringHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "bool.ToString()",
                "char.ToString()",
                "char.ToString(char)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void BooleanCompareAndCharClassificationHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "bool.CompareTo(bool)",
                "char.ConvertToUtf32(char, char)",
                "char.GetNumericValue(char)",
                "char.IsControl(char)",
                "char.IsLower(char)",
                "char.IsNumber(char)",
                "char.IsPunctuation(char)",
                "char.IsSeparator(char)",
                "char.IsSymbol(char)",
                "char.IsUpper(char)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void IndexAndHashCodeHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.HashCode.ToHashCode()",
                "System.Index.End.get",
                "System.Index.Start.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void SpanAndMemoryMarshalHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Runtime.InteropServices.MemoryMarshal.AsBytes<T>(System.Span<T>)",
                "System.ReadOnlySpan<T>.Length.get",
                "System.ReadOnlySpan<T>.IsEmpty.get",
                "System.ReadOnlySpan<T>.Slice(int, int)",
                "System.Span<T>.Length.get",
                "System.Span<T>.IsEmpty.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ReadOnlySequenceHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Buffers.ReadOnlySequence<T>.IsEmpty.get",
                "System.Buffers.ReadOnlySequence<T>.Length.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ListCapacity_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Collections.Generic.List<T>.Capacity.get");
        }

        [Test]
        public void EmailAddressConstructor_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.ComponentModel.DataAnnotations.EmailAddressAttribute.EmailAddressAttribute()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void DecimalNegate_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("decimal.Negate(decimal)");
        }

        [Test]
        public void BooleanParseHelpers_AreHandledSemantically_NotStaticCatalogs()
        {
            var members = new[]
            {
                "bool.Parse(string)",
                "bool.TryParse(string, out bool)",
                "bool.TryParse(string?, out bool)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void EnumTryParseHelpers_AreHandledSemantically_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Enum.TryParse<TEnum>(string, out TEnum)",
                "System.Enum.TryParse<TEnum>(string, bool, out TEnum)",
                "System.Enum.TryParse<TEnum>(string?, out TEnum)",
                "System.Enum.TryParse<TEnum>(string?, bool, out TEnum)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void EnumParseHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Enum.Parse(System.Type, string)");
        }

        [Test]
        public void UriIsWellFormedUriString_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Uri.IsWellFormedUriString(string, System.UriKind)");
        }

        [Test]
        public void UriEscapeAndUnescapeDataString_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Uri.EscapeDataString(string)");
            AssertNotInManualCatalogs("System.Uri.UnescapeDataString(string)");
        }

        [Test]
        public void AppContextImpureHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.AppContext.BaseDirectory.get");
            AssertNotInManualCatalogs("System.AppContext.GetData(string)");
            AssertNotInManualCatalogs("System.AppContext.SetData(string, object?)");
            AssertNotInManualCatalogs("System.AppContext.TryGetSwitch(string, out bool)");
            AssertNotInManualCatalogs("System.AppContext.SetSwitch(string, bool)");
        }

        [Test]
        public void AppDomainCurrentDomainAndBaseDirectory_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.AppDomain.CurrentDomain.get");
            AssertNotInManualCatalogs("System.AppDomain.BaseDirectory.get");
        }

        [Test]
        public void OperatingSystemAndApplicationModelPureHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.AppContext.TargetFrameworkName.get",
                "System.AppDomain.Id.get",
                "System.OperatingSystem.IsAndroid()",
                "System.OperatingSystem.IsAndroidVersionAtLeast(int, int, int, int)",
                "System.OperatingSystem.IsBrowser()",
                "System.OperatingSystem.IsFreeBSD()",
                "System.OperatingSystem.IsFreeBSDVersionAtLeast(int, int, int, int)",
                "System.OperatingSystem.IsIOS()",
                "System.OperatingSystem.IsIOSVersionAtLeast(int, int, int)",
                "System.OperatingSystem.IsLinux()",
                "System.OperatingSystem.IsMacCatalyst()",
                "System.OperatingSystem.IsMacCatalystVersionAtLeast(int, int, int)",
                "System.OperatingSystem.IsMacOS()",
                "System.OperatingSystem.IsMacOSVersionAtLeast(int, int, int)",
                "System.OperatingSystem.IsOSPlatform(string)",
                "System.OperatingSystem.IsOSPlatformVersionAtLeast(string, int, int, int, int)",
                "System.OperatingSystem.Platform.get",
                "System.OperatingSystem.IsTvOS()",
                "System.OperatingSystem.IsTvOSVersionAtLeast(int, int, int)",
                "System.OperatingSystem.IsWasi()",
                "System.OperatingSystem.IsWatchOS()",
                "System.OperatingSystem.IsWatchOSVersionAtLeast(int, int, int)",
                "System.OperatingSystem.IsWindows()",
                "System.OperatingSystem.IsWindowsVersionAtLeast(int, int, int, int)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void EnvironmentStablePureGetters_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Environment.Is64BitOperatingSystem.get",
                "System.Environment.Is64BitProcess.get",
                "System.Environment.NewLine.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void VersionPureMembers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Version.Version(int, int)",
                "System.Version.Version(int, int, int)",
                "System.Version.Version(int, int, int, int)",
                "System.Version.CompareTo(System.Version?)",
                "System.Version.Equals(System.Version?)",
                "System.Version.Major.get",
                "System.Version.Minor.get",
                "System.Version.Build.get",
                "System.Version.Revision.get",
                "System.Version.MajorRevision.get",
                "System.Version.MinorRevision.get",
                "System.Version.GetHashCode()",
                "System.Version.op_Equality(System.Version?, System.Version?)",
                "System.Version.op_Inequality(System.Version?, System.Version?)",
                "System.Version.op_GreaterThan(System.Version?, System.Version?)",
                "System.Version.op_GreaterThanOrEqual(System.Version?, System.Version?)",
                "System.Version.op_LessThan(System.Version?, System.Version?)",
                "System.Version.op_LessThanOrEqual(System.Version?, System.Version?)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void DateTimeAndDateTimeOffsetStableMembers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.DateTime.DateTime(long)",
                "System.DateTime.DateTime(int, int, int)",
                "System.DateTime.IsLeapYear(int)",
                "System.DateTime.Day.get",
                "System.DateTime.DayOfWeek.get",
                "System.DateTime.DayOfYear.get",
                "System.DateTime.Hour.get",
                "System.DateTime.Kind.get",
                "System.DateTime.Millisecond.get",
                "System.DateTime.Minute.get",
                "System.DateTime.Month.get",
                "System.DateTime.Second.get",
                "System.DateTime.Ticks.get",
                "System.DateTime.TimeOfDay.get",
                "System.DateTimeOffset.DateTimeOffset(long, System.TimeSpan)",
                "System.DateTimeOffset.DateTime.get",
                "System.DateTimeOffset.Day.get",
                "System.DateTimeOffset.DayOfWeek.get",
                "System.DateTimeOffset.DayOfYear.get",
                "System.DateTimeOffset.Hour.get",
                "System.DateTimeOffset.Millisecond.get",
                "System.DateTimeOffset.Minute.get",
                "System.DateTimeOffset.Month.get",
                "System.DateTimeOffset.Second.get",
                "System.DateTimeOffset.Ticks.get",
                "System.DateTimeOffset.UtcDateTime.get",
                "System.DateTimeOffset.UtcTicks.get",
                "System.DateTimeOffset.Year.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void TimeProviderAndTimeZoneInfoGlobals_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.TimeProvider.LocalTimeZone.get");
            AssertNotInManualCatalogs("System.TimeProvider.System.get");
            AssertNotInManualCatalogs("System.TimeProvider.TimestampFrequency.get");
            AssertNotInManualCatalogs("System.TimeZoneInfo.Local.get");
            AssertNotInManualCatalogs("System.TimeZoneInfo.FindSystemTimeZoneById(string)");
            AssertNotInManualCatalogs("System.TimeZoneInfo.ClearCachedData()");
        }

        [Test]
        public void IPAddressParseHelpers_AreHandledSemantically_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Net.IPAddress.Parse(string)",
                "System.Net.IPAddress.Parse(System.ReadOnlySpan<char>)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void JsonSerializerSerialize_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, System.Text.Json.JsonSerializerOptions?)"));
        }

        [Test]
        public void RegexHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.Regex(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.IsMatch(string, string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.Match(string, string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.Replace(string, string, string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.IsMatch(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.Match(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.Replace(string, string)"));
        }

        [Test]
        public void UnsafeUnalignedHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.ReadUnaligned(ref byte)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.ReadUnaligned(void*)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.WriteUnaligned(ref byte, !!0)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.WriteUnaligned(void*, !!0)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.As<TFrom, TTo>(ref TFrom)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.SizeOf<T>()"));
        }

        [Test]
        public void BitOperationsCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Numerics.BitOperations.LeadingZeroCount(uint)",
                "System.Numerics.BitOperations.LeadingZeroCount(ulong)",
                "System.Numerics.BitOperations.Log2(uint)",
                "System.Numerics.BitOperations.Log2(ulong)",
                "System.Numerics.BitOperations.PopCount(uint)",
                "System.Numerics.BitOperations.PopCount(ulong)",
                "System.Numerics.BitOperations.RotateLeft(uint, int)",
                "System.Numerics.BitOperations.RotateLeft(ulong, int)",
                "System.Numerics.BitOperations.RotateRight(uint, int)",
                "System.Numerics.BitOperations.RotateRight(ulong, int)",
                "System.Numerics.BitOperations.RoundUpToPowerOf2(uint)",
                "System.Numerics.BitOperations.RoundUpToPowerOf2(ulong)",
                "System.Numerics.BitOperations.TrailingZeroCount(int)",
                "System.Numerics.BitOperations.TrailingZeroCount(uint)",
                "System.Numerics.BitOperations.TrailingZeroCount(long)",
                "System.Numerics.BitOperations.TrailingZeroCount(ulong)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
                Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void BitConverterReadHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.BitConverter.ToInt32(byte[], int)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.BitConverter.ToInt32(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.BitConverter.ToDouble(byte[], int)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.BitConverter.ToDouble(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.BitConverter.ToInt32(byte[], int)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.BitConverter.ToInt32(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.BitConverter.ToDouble(byte[], int)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.BitConverter.ToDouble(System.ReadOnlySpan<byte>)"));
        }

        [Test]
        public void MathCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Math.Abs",
                "System.Math.Ceiling(decimal)",
                "System.Math.Clamp",
                "System.Math.Max",
                "System.Math.Min",
                "System.Math.Round",
                "System.Math.Truncate(double)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
                Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void MathAbsCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Math.Abs(System.Decimal)",
                "System.Math.Abs(double)",
                "System.Math.Abs(float)",
                "System.Math.Abs(int)",
                "System.Math.Abs(long)",
                "System.Math.Abs(nint)",
                "System.Math.Abs(sbyte)",
                "System.Math.Abs(short)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
                Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void MathConstants_AreSourcedFromFieldSemantics_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Math.E");
            AssertNotInManualCatalogs("System.Math.PI");
            AssertNotInManualCatalogs("System.Math.Tau");
        }

        [Test]
        public void MathDoubleHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Ceiling(double)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Floor(double)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Sin(double)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Sqrt(double)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Truncate(double)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Sign(decimal)"));
        }

        [Test]
        public void ArrayEmpty_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Array.Empty()"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Array.Empty()"));
        }

        [Test]
        public void MemoryExtensionsCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.MemoryExtensions.AsSpan(string)",
                "System.MemoryExtensions.SequenceEqual<T>(System.ReadOnlySpan<T>, System.ReadOnlySpan<T>)",
                "System.MemoryExtensions.Trim<T>(System.ReadOnlySpan<T>)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringToCharArray_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.ToCharArray()"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("string.ToCharArray()"));
        }

        [Test]
        public void ConvertCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.ToBase64String(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.ToBase64String(byte[], int, int)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.ToHexString(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.ToHexString(byte[], int, int)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.ToHexString(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.FromBase64String(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.FromBase64CharArray(char[], int, int)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.FromHexString(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.FromHexString(System.ReadOnlySpan<char>)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.ToBase64String(byte[])"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.ToBase64String(byte[], int, int)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.ToHexString(byte[])"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.ToHexString(byte[], int, int)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.ToHexString(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.FromBase64String(string)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.FromBase64CharArray(char[], int, int)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.FromHexString(string)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.FromHexString(System.ReadOnlySpan<char>)"));
        }

        [Test]
        public void ConvertCurrentCultureHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Convert.ToByte(object?)",
                "System.Convert.ToByte(string?)",
                "System.Convert.ToDateTime(object?)",
                "System.Convert.ToDateTime(string?)",
                "System.Convert.ToDecimal(object?)",
                "System.Convert.ToDecimal(string?)",
                "System.Convert.ToDouble(object?)",
                "System.Convert.ToDouble(string?)",
                "System.Convert.ToInt16(object?)",
                "System.Convert.ToInt16(string?)",
                "System.Convert.ToInt32(object?)",
                "System.Convert.ToInt32(string?)",
                "System.Convert.ToInt64(object?)",
                "System.Convert.ToInt64(string?)",
                "System.Convert.ToSByte(object?)",
                "System.Convert.ToSByte(string?)",
                "System.Convert.ToSingle(object?)",
                "System.Convert.ToSingle(string?)",
                "System.Convert.ToString(object?)",
                "System.Convert.ToUInt16(object?)",
                "System.Convert.ToUInt16(string?)",
                "System.Convert.ToUInt32(object?)",
                "System.Convert.ToUInt32(string?)",
                "System.Convert.ToUInt64(object?)",
                "System.Convert.ToUInt64(string?)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void CurrentCultureNumericParseAndFormatHelpers_AreHandledSemantically_NotStaticCatalogs()
        {
            var members = new[]
            {
                "byte.Parse(string)",
                "byte.TryParse(System.ReadOnlySpan<char>, out byte)",
                "byte.ToString(string?)",
                "byte.ToString()",
                "decimal.Parse(string)",
                "decimal.TryParse(System.ReadOnlySpan<char>, out decimal)",
                "decimal.ToString(string?)",
                "decimal.ToString()",
                "double.Parse(string)",
                "double.TryParse(string, out double)",
                "double.TryParse(System.ReadOnlySpan<char>, out double)",
                "double.ToString(string?)",
                "double.ToString()",
                "float.Parse(string)",
                "float.TryParse(string, out float)",
                "float.TryParse(System.ReadOnlySpan<char>, out float)",
                "float.ToString(string?)",
                "float.ToString()",
                "int.Parse(string)",
                "int.TryParse(string, out int)",
                "int.TryParse(System.ReadOnlySpan<char>, out int)",
                "int.ToString(string?)",
                "int.ToString()",
                "long.Parse(string)",
                "long.TryParse(System.ReadOnlySpan<char>, out long)",
                "long.ToString(string?)",
                "long.ToString()",
                "short.Parse(string)",
                "short.TryParse(string, out short)",
                "short.TryParse(System.ReadOnlySpan<char>, out short)",
                "short.ToString(string?)",
                "short.ToString()",
                "sbyte.Parse(string)",
                "sbyte.TryParse(string, out sbyte)",
                "sbyte.TryParse(System.ReadOnlySpan<char>, out sbyte)",
                "sbyte.ToString(string?)",
                "sbyte.ToString()",
                "ushort.Parse(string)",
                "ushort.TryParse(string, out ushort)",
                "ushort.TryParse(System.ReadOnlySpan<char>, out ushort)",
                "ushort.ToString(string?)",
                "ushort.ToString()",
                "uint.Parse(string)",
                "uint.TryParse(string, out uint)",
                "uint.TryParse(System.ReadOnlySpan<char>, out uint)",
                "uint.ToString(string?)",
                "uint.ToString()",
                "ulong.Parse(string)",
                "ulong.TryParse(string, out ulong)",
                "ulong.TryParse(System.ReadOnlySpan<char>, out ulong)",
                "ulong.ToString(string?)",
                "ulong.ToString()",
                "System.Half.Parse(string)",
                "System.Half.TryParse(string, out System.Half)",
                "System.Half.TryParse(System.ReadOnlySpan<char>, out System.Half)",
                "System.Half.ToString(string?)",
                "System.Half.ToString()",
                "System.Numerics.BigInteger.Parse(string)",
                "System.Numerics.BigInteger.TryParse(string, out System.Numerics.BigInteger)",
                "System.Numerics.BigInteger.TryParse(System.ReadOnlySpan<char>, out System.Numerics.BigInteger)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void CurrentCultureDateAndTimeHelpers_AreNotBackedBySpecificMethodCatalogEntries()
        {
            var members = new[]
            {
                "System.DateOnly.Parse(string)",
                "System.DateOnly.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?)",
                "System.DateOnly.ParseExact(string, string)",
                "System.DateOnly.ParseExact(string, string[])",
                "System.DateOnly.ParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateOnly.ParseExact(System.ReadOnlySpan<char>, string[])",
                "System.DateOnly.ToLongDateString()",
                "System.DateOnly.ToShortDateString()",
                "System.DateOnly.TryParse(string, out System.DateOnly)",
                "System.DateOnly.TryParse(System.ReadOnlySpan<char>, out System.DateOnly)",
                "System.DateOnly.TryParseExact(string, string, out System.DateOnly)",
                "System.DateOnly.TryParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, out System.DateOnly)",
                "System.DateOnly.TryParseExact(string, string[], out System.DateOnly)",
                "System.DateOnly.TryParseExact(System.ReadOnlySpan<char>, string?[]?, out System.DateOnly)",
                "System.DateOnly.ToString(string?)",
                "System.DateOnly.ToString()",
                "System.DateTime.Parse(string)",
                "System.DateTime.TryParse(string, out System.DateTime)",
                "System.DateTime.TryParse(System.ReadOnlySpan<char>, out System.DateTime)",
                "System.DateTime.ToLongDateString()",
                "System.DateTime.ToLongTimeString()",
                "System.DateTime.ToShortDateString()",
                "System.DateTime.ToShortTimeString()",
                "System.DateTime.ToString(string)",
                "System.DateTime.ToString()",
                "System.DateTimeOffset.Parse(string)",
                "System.DateTimeOffset.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?)",
                "System.DateTimeOffset.TryParse(string, out System.DateTimeOffset)",
                "System.DateTimeOffset.TryParse(System.ReadOnlySpan<char>, out System.DateTimeOffset)",
                "System.DateTimeOffset.ToString(string?)",
                "System.DateTimeOffset.ToString()",
                "System.TimeOnly.Parse(string)",
                "System.TimeOnly.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?)",
                "System.TimeOnly.ParseExact(string, string)",
                "System.TimeOnly.ParseExact(string, string[])",
                "System.TimeOnly.ParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.TimeOnly.ParseExact(System.ReadOnlySpan<char>, string[])",
                "System.TimeOnly.ToLongTimeString()",
                "System.TimeOnly.ToShortTimeString()",
                "System.TimeOnly.ToString(string?)",
                "System.TimeOnly.ToString()",
                "System.TimeOnly.TryParse(string, out System.TimeOnly)",
                "System.TimeOnly.TryParse(System.ReadOnlySpan<char>, out System.TimeOnly)",
                "System.TimeOnly.TryParseExact(string, string, out System.TimeOnly)",
                "System.TimeOnly.TryParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, out System.TimeOnly)",
                "System.TimeOnly.TryParseExact(string, string[], out System.TimeOnly)",
                "System.TimeOnly.TryParseExact(System.ReadOnlySpan<char>, string?[]?, out System.TimeOnly)",
                "System.TimeSpan.Parse(string)",
                "System.TimeSpan.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?)",
                "System.TimeSpan.ToString()",
                "System.TimeSpan.TryParse(string, out System.TimeSpan)",
                "System.TimeSpan.TryParse(System.ReadOnlySpan<char>, out System.TimeSpan)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void TimeSpanComparisonAndFactoryHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.TimeSpan.CompareTo(System.TimeSpan)");
            AssertNotInManualCatalogs("System.TimeSpan.FromDays(double)");
        }

        [Test]
        public void FieldBackedStaticBclValues_AreHandledSemantically_NotStaticCatalogs()
        {
            var source = @"
using System;
using System.Net;
using System.Net.Http;
using System.Reflection;

public static class StaticFieldSamples
{
    public static object Sample()
    {
        _ = Guid.Empty;
        _ = TimeSpan.Zero;
        _ = EventArgs.Empty;
        _ = DBNull.Value;
        _ = IPAddress.Any;
        _ = IPAddress.Loopback;
        _ = HttpVersion.Version11;
        return Missing.Value;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "StaticFieldCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Guid.Empty"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "TimeSpan.Zero"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "EventArgs.Empty"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "DBNull.Value"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "IPAddress.Any"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "IPAddress.Loopback"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "HttpVersion.Version11"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Missing.Value"));
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
using System.Reflection;
using System.Runtime.CompilerServices;
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
        var values = new[] { 1, 2, 3 };
        object leftObject = new object();
        object rightObject = new object();
        list.Add(1);
        var now = DateTime.Now;
        _ = IPAddress.Loopback;
        _ = names.Contains(""alpha"");
        _ = new FileNotFoundException(""missing.txt"");
        ReadOnlySpan<char> chars = ""alpha"".AsSpan();
        byte[] bytes = new byte[1];
        ReadOnlySpan<byte> spanBytes = bytes;
        ReadOnlySpan<byte> left = stackalloc byte[1];
        ReadOnlySpan<byte> right = stackalloc byte[1];
        _ = new string(chars);
        _ = SHA1.HashData(bytes);
        _ = SHA256.HashData(bytes);
        _ = SHA384.HashData(bytes);
        _ = SHA512.HashData(bytes);
        _ = MD5.HashData(bytes);
        _ = SHA1.HashData(spanBytes);
        _ = SHA256.HashData(spanBytes);
        _ = SHA384.HashData(spanBytes);
        _ = SHA512.HashData(spanBytes);
        _ = MD5.HashData(spanBytes);
        _ = CryptographicOperations.FixedTimeEquals(left, right);
        _ = new ArgumentOutOfRangeException(""value"");
        _ = new BadImageFormatException(""bad image"");
        _ = new AttributeUsageAttribute(AttributeTargets.Method);
        _ = object.ReferenceEquals(leftObject, rightObject);
        _ = new ArraySegment<int>(values);
        _ = new ArraySegment<int>(values, 0, 1);
        _ = Tuple.Create(1, 2);
        _ = ValueTuple.Create(1, 2);
        _ = new DivideByZeroException();
        _ = new InvalidOperationException(""bad operation"");
        _ = new ObsoleteAttribute(""legacy"");
        _ = new Index(2, false);
        _ = new Range(new Index(0, false), new Index(1, false));
        _ = new UIntPtr(1u);
        _ = new CallerArgumentExpressionAttribute(""value"");
        _ = new MethodImplAttribute(MethodImplOptions.AggressiveInlining);
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
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetPropertySignature(compilation, syntaxTree, "IPAddress.Loopback")));

            Assert.That(GetPropertySignature(compilation, syntaxTree, "list.Count"), Is.EqualTo("System.Collections.Generic.List<T>.Count.get"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetPropertySignature(compilation, syntaxTree, "list.Count")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "names.Contains(\"alpha\")"), Is.EqualTo("System.Collections.ObjectModel.KeyedCollection<TKey, TItem>.Contains(TKey)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetInvocationSignature(compilation, syntaxTree, "names.Contains(\"alpha\")")));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new FileNotFoundException(\"missing.txt\")"), Is.EqualTo("System.IO.FileNotFoundException.FileNotFoundException(string?)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetObjectCreationSignature(compilation, syntaxTree, "new FileNotFoundException(\"missing.txt\")")));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new string(chars)"), Is.EqualTo("string.String(System.ReadOnlySpan<char>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Contain(GetObjectCreationSignature(compilation, syntaxTree, "new string(chars)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "CryptographicOperations.FixedTimeEquals(left, right)"), Is.EqualTo("System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan<byte>, System.ReadOnlySpan<byte>)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "CryptographicOperations.FixedTimeEquals(left, right)"));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new ArgumentOutOfRangeException(\"value\")"), Is.EqualTo("System.ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new ArgumentOutOfRangeException(\"value\")"));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new BadImageFormatException(\"bad image\")"), Is.EqualTo("System.BadImageFormatException.BadImageFormatException(string?)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new BadImageFormatException(\"bad image\")"));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new AttributeUsageAttribute(AttributeTargets.Method)"), Is.EqualTo("System.AttributeUsageAttribute.AttributeUsageAttribute(System.AttributeTargets)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new AttributeUsageAttribute(AttributeTargets.Method)"));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "object.ReferenceEquals(leftObject, rightObject)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new ArraySegment<int>(values)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new ArraySegment<int>(values, 0, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Tuple.Create(1, 2)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ValueTuple.Create(1, 2)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new DivideByZeroException()"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new InvalidOperationException(\"bad operation\")"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new ObsoleteAttribute(\"legacy\")"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new Index(2, false)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new Range(new Index(0, false), new Index(1, false))"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new UIntPtr(1u)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new CallerArgumentExpressionAttribute(\"value\")"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new MethodImplAttribute(MethodImplOptions.AggressiveInlining)"));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA1.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA256.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.MD5.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA384.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA512.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(spanBytes)"), Is.EqualTo("System.Security.Cryptography.SHA1.HashData(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(spanBytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(spanBytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(spanBytes)"), Is.EqualTo("System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(spanBytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(spanBytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(spanBytes)"), Is.EqualTo("System.Security.Cryptography.MD5.HashData(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(spanBytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(spanBytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(spanBytes)"), Is.EqualTo("System.Security.Cryptography.SHA384.HashData(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(spanBytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(spanBytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(spanBytes)"), Is.EqualTo("System.Security.Cryptography.SHA512.HashData(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(spanBytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(spanBytes)")));
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
            AssertCatalogMembership(GetPropertySignature(compilation, syntaxTree, "IPAddress.Loopback"), expectedPure: false, expectedImpure: false);
            AssertCatalogMembership(GetPropertySignature(compilation, syntaxTree, "list.Count"), expectedPure: false, expectedImpure: false);
        }

        [Test]
        public void RecentGuidAndDateTimeOffsetGeneratedPurityEntriesResolveAgainstNet80References()
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
        _ = guid.Equals(Guid.Empty);
        _ = guid.CompareTo(Guid.Empty);
        _ = guid.ToString(""N"");
        var chars = text.ToCharArray();
        ReadOnlySpan<char> charSpan = text.AsSpan();
        _ = new string(charSpan);
        _ = guid.ToByteArray();
        _ = guid.ToByteArray(true);
        _ = BitConverter.GetBytes(true);
        _ = BitConverter.GetBytes('a');
        _ = BitConverter.GetBytes((Half)1);
        _ = BitConverter.GetBytes((short)1);
        _ = BitConverter.GetBytes(1);
        _ = BitConverter.GetBytes(1f);
        _ = BitConverter.GetBytes(1L);
        _ = BitConverter.GetBytes((ushort)1);
        _ = BitConverter.GetBytes(1u);
        _ = BitConverter.GetBytes(1ul);
        _ = chars;
        _ = BitOperations.IsPow2(1);
        _ = BitOperations.IsPow2(1u);
        _ = BitOperations.IsPow2(1L);
        _ = BitOperations.IsPow2(1ul);
        _ = BitOperations.IsPow2((nint)1);
        _ = BitOperations.IsPow2((nuint)1);
        _ = BitOperations.LeadingZeroCount(1u);
        _ = BitOperations.LeadingZeroCount(1ul);
        _ = BitOperations.Log2(1u);
        _ = BitOperations.Log2(1ul);
        _ = BitOperations.PopCount(1u);
        _ = BitOperations.PopCount(1ul);
        _ = BitOperations.PopCount((nuint)1);
        _ = BitOperations.RotateLeft(1u, 1);
        _ = BitOperations.RotateLeft(1ul, 1);
        _ = BitOperations.RotateLeft((nuint)1, 1);
        _ = BitOperations.RotateRight(1u, 1);
        _ = BitOperations.RotateRight(1ul, 1);
        _ = BitOperations.RotateRight((nuint)1, 1);
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
        _ = BinaryPrimitives.ReverseEndianness((sbyte)1);
        _ = BinaryPrimitives.ReverseEndianness((byte)1);
        _ = BinaryPrimitives.ReverseEndianness((short)1);
        _ = BinaryPrimitives.ReverseEndianness((ushort)1);
        _ = BinaryPrimitives.ReverseEndianness('a');
        _ = BinaryPrimitives.ReverseEndianness(1);
        _ = BinaryPrimitives.ReverseEndianness(1u);
        _ = BinaryPrimitives.ReverseEndianness(1L);
        _ = BinaryPrimitives.ReverseEndianness(1ul);
        _ = BinaryPrimitives.ReverseEndianness((nint)1);
        _ = BinaryPrimitives.ReverseEndianness((nuint)1);
        _ = BinaryPrimitives.ReverseEndianness((Int128)1);
        _ = BinaryPrimitives.ReverseEndianness((UInt128)1);
        var fromMilliseconds = DateTimeOffset.FromUnixTimeMilliseconds(0);
        var fromSeconds = DateTimeOffset.FromUnixTimeSeconds(0);
        var add = value.Add(TimeSpan.FromHours(1));
        var addHours = value.AddHours(2);
        var addMilliseconds = value.AddMilliseconds(3);
        var addMinutes = value.AddMinutes(4);
        var addMonths = value.AddMonths(5);
        var addSeconds = value.AddSeconds(6);
        var addTicks = value.AddTicks(7);
        var addYears = value.AddYears(8);
        var added = value.AddDays(1);
        _ = DateTimeOffset.Compare(value, added);
        _ = value.CompareTo(added);
        _ = value.Equals(added);
        _ = DateTimeOffset.Equals(value, added);
        _ = added.Subtract(value);
        var seconds = added.ToUnixTimeSeconds();
        return added.ToUnixTimeMilliseconds() + seconds + value.Offset.Ticks;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "RecentCatalogSignatureResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Guid.ParseExact(text, \"D\")"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Guid.TryParse(text, out var parsed)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Guid.TryParseExact(text, \"D\", out parsed)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "guid.Equals(Guid.Empty)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "guid.CompareTo(Guid.Empty)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "guid.ToString(\"N\")"));
            AssertCatalogMembership(GetObjectCreationSignature(compilation, syntaxTree, "new string(charSpan)"), expectedPure: true, expectedImpure: false);
            var bitConverterGetBytesExpressions = new[]
            {
                "BitConverter.GetBytes(true)",
                "BitConverter.GetBytes('a')",
                "BitConverter.GetBytes((Half)1)",
                "BitConverter.GetBytes((short)1)",
                "BitConverter.GetBytes(1)",
                "BitConverter.GetBytes(1f)",
                "BitConverter.GetBytes(1L)",
                "BitConverter.GetBytes((ushort)1)",
                "BitConverter.GetBytes(1u)",
                "BitConverter.GetBytes(1ul)",
            };

            foreach (var expression in bitConverterGetBytesExpressions)
            {
                AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, expression));
            }

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1L)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1ul)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2((nint)1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2((nuint)1)"));
            var binaryPrimitivesReadExpressions = new[]
            {
                "BinaryPrimitives.ReadInt16BigEndian(bytes)",
                "BinaryPrimitives.ReadInt16LittleEndian(bytes)",
                "BinaryPrimitives.ReadInt32BigEndian(bytes)",
                "BinaryPrimitives.ReadInt32LittleEndian(bytes)",
                "BinaryPrimitives.ReadInt64BigEndian(bytes)",
                "BinaryPrimitives.ReadInt64LittleEndian(bytes)",
                "BinaryPrimitives.ReadUInt16BigEndian(bytes)",
                "BinaryPrimitives.ReadUInt16LittleEndian(bytes)",
                "BinaryPrimitives.ReadUInt32BigEndian(bytes)",
                "BinaryPrimitives.ReadUInt32LittleEndian(bytes)",
                "BinaryPrimitives.ReadUInt64BigEndian(bytes)",
                "BinaryPrimitives.ReadUInt64LittleEndian(bytes)",
            };

            foreach (var expression in binaryPrimitivesReadExpressions)
            {
                AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, expression));
            }

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.LeadingZeroCount(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.LeadingZeroCount(1ul)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.Log2(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.Log2(1ul)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.PopCount(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.PopCount(1ul)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.PopCount((nuint)1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateLeft(1u, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateLeft(1ul, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateLeft((nuint)1, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateRight(1u, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateRight(1ul, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateRight((nuint)1, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RoundUpToPowerOf2(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RoundUpToPowerOf2(1ul)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1L)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1ul)"));
            var binaryPrimitivesReverseEndiannessExpressions = new[]
            {
                "BinaryPrimitives.ReverseEndianness((sbyte)1)",
                "BinaryPrimitives.ReverseEndianness((byte)1)",
                "BinaryPrimitives.ReverseEndianness((short)1)",
                "BinaryPrimitives.ReverseEndianness((ushort)1)",
                "BinaryPrimitives.ReverseEndianness('a')",
                "BinaryPrimitives.ReverseEndianness(1)",
                "BinaryPrimitives.ReverseEndianness(1u)",
                "BinaryPrimitives.ReverseEndianness(1L)",
                "BinaryPrimitives.ReverseEndianness(1ul)",
                "BinaryPrimitives.ReverseEndianness((nint)1)",
                "BinaryPrimitives.ReverseEndianness((nuint)1)",
                "BinaryPrimitives.ReverseEndianness((Int128)1)",
                "BinaryPrimitives.ReverseEndianness((UInt128)1)",
            };

            foreach (var expression in binaryPrimitivesReverseEndiannessExpressions)
            {
                AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, expression));
            }

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTimeOffset.FromUnixTimeMilliseconds(0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTimeOffset.FromUnixTimeSeconds(0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Add(TimeSpan.FromHours(1))"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddHours(2)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMilliseconds(3)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMinutes(4)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMonths(5)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddSeconds(6)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddTicks(7)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddYears(8)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddDays(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTimeOffset.Compare(value, added)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.CompareTo(added)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Equals(added)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTimeOffset.Equals(value, added)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "added.Subtract(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "added.ToUnixTimeMilliseconds()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "added.ToUnixTimeSeconds()"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Offset"));
        }

        [Test]
        public void DateTimeGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;

public static class DateTimeCatalogSignatureSamples
{
    public static DateTime Sample(DateTime value, TimeSpan offset)
    {
        _ = value.Add(offset);
        _ = value.AddDays(1);
        _ = value.AddHours(2);
        _ = value.AddMilliseconds(3);
        _ = value.AddMinutes(4);
        _ = value.AddMonths(5);
        _ = value.AddSeconds(6);
        _ = value.AddTicks(7);
        _ = value.AddYears(8);
        _ = DateTime.FromBinary(value.ToBinary());
        _ = DateTime.FromOADate(value.ToOADate());
        _ = DateTime.Compare(value, value);
        _ = value.CompareTo(value);
        _ = value.Equals(value);
        _ = DateTime.Equals(value, value);
        _ = DateTime.DaysInMonth(2000, 2);
        _ = value.Day;
        _ = value.DayOfWeek;
        _ = value.DayOfYear;
        _ = value.Hour;
        _ = value.Kind;
        _ = value.Millisecond;
        _ = value.Minute;
        _ = value.Month;
        _ = value.Second;
        _ = value.Ticks;
        _ = value.TimeOfDay;
        _ = value.Subtract(value);
        _ = value.ToBinary();
        return value;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "DateTimeGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Add(offset)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddDays(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddHours(2)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMilliseconds(3)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMinutes(4)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMonths(5)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddSeconds(6)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddTicks(7)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddYears(8)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTime.FromBinary(value.ToBinary())"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTime.FromOADate(value.ToOADate())"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTime.Compare(value, value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.CompareTo(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Equals(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTime.Equals(value, value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTime.DaysInMonth(2000, 2)"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Day"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.DayOfWeek"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.DayOfYear"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Hour"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Kind"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Millisecond"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Minute"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Month"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Second"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Ticks"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.TimeOfDay"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Subtract(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.ToBinary()"));
        }

        [Test]
        public void BooleanAndCharGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;

public static class BooleanCharCatalogSignatureSamples
{
    public static int Sample(bool left, bool right, char value, char other)
    {
        _ = left.CompareTo(right);
        _ = char.ConvertToUtf32(value, other);
        _ = char.GetNumericValue(value);
        _ = char.IsControl(value);
        _ = char.IsLower(value);
        _ = char.IsNumber(value);
        _ = char.IsPunctuation(value);
        _ = char.IsSeparator(value);
        _ = char.IsSymbol(value);
        _ = char.IsUpper(value);
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "BooleanCharGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "left.CompareTo(right)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.ConvertToUtf32(value, other)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.GetNumericValue(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsControl(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsLower(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsNumber(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsPunctuation(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsSeparator(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsSymbol(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsUpper(value)"));
        }

        [Test]
        public void IndexAndHashCodeGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;

public static class IndexHashCodeCatalogSignatureSamples
{
    public static int Sample()
    {
        HashCode hash = default;
        var end = Index.End;
        var start = Index.Start;
        _ = hash.ToHashCode();
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "IndexHashCodeGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "hash.ToHashCode()"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Index.End"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Index.Start"));
        }

        [Test]
        public void SpanAndMemoryMarshalGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;
using System.Runtime.InteropServices;

public static class SpanMemoryMarshalCatalogSignatureSamples
{
    public static int Sample(ReadOnlySpan<int> readOnly, Span<int> writable)
    {
        var head = readOnly.Slice(0, 0);
        var readOnlyBytes = MemoryMarshal.AsBytes(readOnly);
        var writableBytes = MemoryMarshal.AsBytes(writable);
        return readOnly.Length + writable.Length + head.Length + readOnlyBytes.Length + writableBytes.Length + (readOnly.IsEmpty ? 0 : 1) + (writable.IsEmpty ? 0 : 1);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "SpanMemoryMarshalGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "readOnly.Slice(0, 0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "MemoryMarshal.AsBytes(writable)"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "readOnly.Length"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "writable.Length"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "readOnly.IsEmpty"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "writable.IsEmpty"));
        }

        [Test]
        public void ReadOnlySequenceGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System.Buffers;

public static class ReadOnlySequenceCatalogSignatureSamples
{
    public static long Sample(ReadOnlySequence<int> value)
    {
        return value.Length + (value.IsEmpty ? 0L : 1L);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ReadOnlySequenceGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Length"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.IsEmpty"));
        }

        [Test]
        public void ListCapacityGeneratedPurityEntryResolvesAgainstNet80References()
        {
            var source = @"
using System.Collections.Generic;

public static class ListCapacityCatalogSignatureSamples
{
    public static int Sample(List<int> values)
    {
        return values.Capacity;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ListCapacityGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "values.Capacity"));
        }

        [Test]
        public void EmailAddressConstructorGeneratedPurityEntryResolvesAgainstNet80References()
        {
            var source = @"
using System.ComponentModel.DataAnnotations;

public static class EmailAddressCatalogSignatureSamples
{
    public static int Sample()
    {
        var attribute = new EmailAddressAttribute();
        return attribute is null ? 0 : 1;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "EmailAddressGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new EmailAddressAttribute()"));
        }

        [Test]
        public void DecimalNegateGeneratedPurityEntryResolvesAgainstNet80References()
        {
            var source = @"
public static class DecimalNegateCatalogSignatureSamples
{
    public static int Sample(decimal value)
    {
        var negated = decimal.Negate(value);
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "DecimalNegateGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "decimal.Negate(value)"));
        }

        private static void AssertCatalogMembership(string signature, bool expectedPure, bool expectedImpure)
        {
            Assert.That(Constants.KnownPureBCLMembers.Contains(signature), Is.EqualTo(expectedPure), signature);
            Assert.That(Constants.KnownImpureMethods.Contains(signature), Is.EqualTo(expectedImpure), signature);
            Assert.That(expectedPure && expectedImpure, Is.False, "Test sample should not intentionally expect a catalog conflict: " + signature);
        }

        private static void AssertNotInManualCatalogs(string signature)
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(signature), signature);
            Assert.That(Constants.KnownImpureMethods, Does.Not.Contain(signature), signature);
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(signature), signature);
        }

        private static string GetInvocationSignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText)
        {
            var invocations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node => node.ToString() == expressionText)
                .ToArray();
            Assert.That(invocations, Is.Not.Empty, "Invocation should exist: " + expressionText);
            var invocation = invocations[^1];
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
