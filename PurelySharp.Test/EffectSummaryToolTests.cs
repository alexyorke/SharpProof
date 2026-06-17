using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace PurelySharp.Test
{
    [TestFixture]
    public class EffectSummaryToolTests
    {
        [Test]
        public async Task EffectSummaryTool_CollectsCommonDirectExceptionTypes()
        {
            var source = """
using System;

public static class ExceptionFixture
{
    public static void ThrowIndexOutOfRange() => throw new IndexOutOfRangeException();

    public static void ThrowInvalidCast() => throw new InvalidCastException();

    public static void ThrowObjectDisposed() => throw new ObjectDisposedException("stream");

    public static void ThrowFormat() => throw new FormatException();

    public static void ThrowOverflow() => throw new OverflowException();
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryCommonExceptions", source);
            using var summary = await RunEffectSummaryAsync(fixture.AssemblyPath, includeTransitiveRoots: true);

            AssertThrownExceptions(summary, "ExceptionFixture.ThrowIndexOutOfRange()", "System.IndexOutOfRangeException");
            AssertThrownExceptions(summary, "ExceptionFixture.ThrowInvalidCast()", "System.InvalidCastException");
            AssertThrownExceptions(summary, "ExceptionFixture.ThrowObjectDisposed()", "System.ObjectDisposedException");
            AssertThrownExceptions(summary, "ExceptionFixture.ThrowFormat()", "System.FormatException");
            AssertThrownExceptions(summary, "ExceptionFixture.ThrowOverflow()", "System.OverflowException");
        }

        [Test]
        public async Task EffectSummaryTool_SuppressesCaughtThrows_And_PreservesRethrowAndTransitiveExceptions()
        {
            var source = """
using System;

public static class ExceptionFixture
{
    public static void ThrowDirect()
    {
        throw new InvalidOperationException("boom");
    }

    public static void ThrowViaLocal()
    {
        var ex = new ObjectDisposedException("stream");
        throw ex;
    }

    public static void ThrowViaCallee()
    {
        ThrowDirect();
    }

    public static int HandleLocally()
    {
        try
        {
            throw new FormatException();
        }
        catch (FormatException)
        {
            return 1;
        }
    }

    public static void RethrowOverflow()
    {
        try
        {
            throw new OverflowException();
        }
        catch (OverflowException)
        {
            throw;
        }
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryControlFlow", source);
            using var summary = await RunEffectSummaryAsync(fixture.AssemblyPath, includeTransitiveRoots: true);

            AssertThrownExceptions(summary, "ExceptionFixture.ThrowDirect()", "System.InvalidOperationException");
            AssertThrownExceptions(summary, "ExceptionFixture.ThrowViaLocal()", "System.ObjectDisposedException");
            AssertTransitiveExceptions(summary, "ExceptionFixture.ThrowViaCallee()", "System.InvalidOperationException");
            AssertThrownExceptions(summary, "ExceptionFixture.HandleLocally()");
            AssertThrownExceptions(summary, "ExceptionFixture.RethrowOverflow()", "System.OverflowException");
        }

        [Test]
        public async Task EffectSummaryTool_Produces_ReportOnly_Purity_Classifications()
        {
            var source = """
using System;

public interface IWorker
{
    int Get();
}

public abstract class AbstractWorker
{
    public abstract int Get();
}

public static class PurityFixture
{
    private static int _state;

    public static int PureLeaf() => 42;

    public static int PureViaCallee() => PureLeaf();

    public static int ImpureWrite()
    {
        _state++;
        return _state;
    }

    public static int ImpureViaCallee() => ImpureWrite();

    public static int UnknownViaInterface(IWorker worker) => worker.Get();

    public static byte[] PureFreshArray()
    {
        var bytes = new byte[4];
        bytes[0] = 1;
        return bytes;
    }

    public static void MutateCallerArray(byte[] bytes)
    {
        bytes[0] = 1;
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryPurityClassification", source);
            using var summary = await RunEffectSummaryAsync(
                fixture.AssemblyPath,
                includeTransitiveRoots: true,
                classifyPurity: true,
                compareManualCatalogs: true);

            Assert.That(summary.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(3));
            AssertPurityClassification(summary, "PurityFixture.PureLeaf()", "pure");
            AssertPurityClassification(summary, "PurityFixture.PureViaCallee()", "pure");
            AssertPurityClassification(summary, "PurityFixture.ImpureWrite()", "impure", "global_state_write");
            AssertPurityClassification(summary, "PurityFixture.ImpureViaCallee()", "impure", "impure_callee");
            AssertPurityClassification(summary, "PurityFixture.UnknownViaInterface(IWorker)", "conservative_unknown", "dynamic_dispatch");
            AssertPurityClassification(summary, "AbstractWorker.Get()", "conservative_unknown", "metadata_only_or_external");
            AssertPurityClassification(summary, "PurityFixture.PureFreshArray()", "pure");
            AssertPurityClassification(summary, "PurityFixture.MutateCallerArray(byte[])", "impure", "caller_visible_memory_write");
            AssertEffectVisibilityClassification(summary, "PurityFixture.PureLeaf()", "none");
            AssertEffectVisibilityClassification(summary, "PurityFixture.PureViaCallee()", "none");
            AssertEffectVisibilityClassification(summary, "PurityFixture.ImpureWrite()", "caller_visible");
            AssertEffectVisibilityClassification(summary, "PurityFixture.ImpureViaCallee()", "caller_visible");
            AssertEffectVisibilityClassification(summary, "PurityFixture.UnknownViaInterface(IWorker)", "unknown");
            AssertEffectVisibilityClassification(summary, "AbstractWorker.Get()", "unknown");
            AssertEffectVisibilityClassification(summary, "PurityFixture.PureFreshArray()", "internal_only");
            AssertEffectVisibilityClassification(summary, "PurityFixture.MutateCallerArray(byte[])", "caller_visible");
            AssertFreshnessClassification(summary, "PurityFixture.PureFreshArray()", "fresh_owned_array_write");

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(3));
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThanOrEqualTo(8));
            Assert.That(report.GetProperty("PureCount").GetInt32(), Is.GreaterThanOrEqualTo(3));
            Assert.That(report.GetProperty("ImpureCount").GetInt32(), Is.GreaterThanOrEqualTo(3));

            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBitConverterSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.BitConverter.GetBytes", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            Assert.That(generatedCatalog.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
            var generatedRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row => row.GetProperty("Symbol").GetString()?.StartsWith("System.BitConverter.GetBytes", StringComparison.Ordinal) == true)
                .ToArray();

            Assert.That(generatedRows, Has.Length.EqualTo(11));
            Assert.That(
                generatedRows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.BitConverter.GetBytes(bool)",
                    "System.BitConverter.GetBytes(char)",
                    "System.BitConverter.GetBytes(short)",
                    "System.BitConverter.GetBytes(int)",
                    "System.BitConverter.GetBytes(long)",
                    "System.BitConverter.GetBytes(ushort)",
                    "System.BitConverter.GetBytes(uint)",
                    "System.BitConverter.GetBytes(ulong)",
                    "System.BitConverter.GetBytes(System.Half)",
                    "System.BitConverter.GetBytes(float)",
                    "System.BitConverter.GetBytes(double)",
                }));

            foreach (var row in generatedRows)
            {
                Assert.That(row.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
                Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("fresh_owned_array_write"));
                Assert.That(row.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("internal_only"));
                Assert.That(row.GetProperty("HasFreshArrayAllocationEvidence").GetBoolean(), Is.True);
                Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBitOperationsIsPow2Slice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Numerics.BitOperations.IsPow2", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var generatedRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row => row.GetProperty("Symbol").GetString()?.StartsWith("System.Numerics.BitOperations.IsPow2", StringComparison.Ordinal) == true)
                .ToArray();

            Assert.That(generatedRows, Has.Length.EqualTo(6));
            Assert.That(
                generatedRows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.Numerics.BitOperations.IsPow2(int)",
                    "System.Numerics.BitOperations.IsPow2(uint)",
                    "System.Numerics.BitOperations.IsPow2(long)",
                    "System.Numerics.BitOperations.IsPow2(ulong)",
                    "System.Numerics.BitOperations.IsPow2(nint)",
                    "System.Numerics.BitOperations.IsPow2(nuint)",
                }));

            foreach (var row in generatedRows)
            {
                Assert.That(row.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
                Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
                Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBinaryPrimitivesReadSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Buffers.Binary.BinaryPrimitives.Read", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var generatedRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row => row.GetProperty("Symbol").GetString()?.StartsWith("System.Buffers.Binary.BinaryPrimitives.Read", StringComparison.Ordinal) == true)
                .ToArray();

            Assert.That(generatedRows, Has.Length.EqualTo(12));
            Assert.That(
                generatedRows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(System.ReadOnlySpan<byte>)",
                    "System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(System.ReadOnlySpan<byte>)",
                }));

            foreach (var row in generatedRows)
            {
                Assert.That(row.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
                Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
                Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBinaryPrimitivesReverseEndiannessSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Buffers.Binary.BinaryPrimitives.ReverseEndianness", limit: 40);

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var generatedPureRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row =>
                    row.GetProperty("Symbol").GetString()?.StartsWith("System.Buffers.Binary.BinaryPrimitives.ReverseEndianness", StringComparison.Ordinal) == true &&
                    string.Equals(row.GetProperty("Classification").GetString(), "pure", StringComparison.Ordinal))
                .ToArray();

            Assert.That(generatedPureRows, Has.Length.EqualTo(13));
            Assert.That(
                generatedPureRows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(sbyte)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(short)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(int)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(long)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(nint)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(System.Int128)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(byte)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(ushort)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(char)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(uint)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(ulong)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(nuint)",
                    "System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(System.UInt128)",
                }));

            foreach (var row in generatedPureRows)
            {
                Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
                Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBitOperationsFastHelpersSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Numerics.BitOperations", limit: 80);

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

            var catalogComparison = report.GetProperty("CatalogComparison");
            var knownPureRows = catalogComparison.GetProperty("KnownPureMembers").EnumerateArray().ToArray();
            Assert.That(knownPureRows.Any(row =>
                string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.PopCount(uint)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.PopCount(ulong)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.PopCount(nuint)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.RotateLeft(uint, int)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.RotateLeft(ulong, int)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.RotateLeft(nuint, int)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.RotateRight(uint, int)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.RotateRight(ulong, int)", StringComparison.Ordinal) ||
                string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.RotateRight(nuint, int)", StringComparison.Ordinal)),
                Is.False);

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var generatedPureRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row =>
                    (row.GetProperty("Symbol").GetString()?.StartsWith("System.Numerics.BitOperations.PopCount", StringComparison.Ordinal) == true ||
                     row.GetProperty("Symbol").GetString()?.StartsWith("System.Numerics.BitOperations.RotateLeft", StringComparison.Ordinal) == true ||
                     row.GetProperty("Symbol").GetString()?.StartsWith("System.Numerics.BitOperations.RotateRight", StringComparison.Ordinal) == true) &&
                    string.Equals(row.GetProperty("Classification").GetString(), "pure", StringComparison.Ordinal))
                .ToArray();

            Assert.That(generatedPureRows, Has.Length.EqualTo(9));
            Assert.That(
                generatedPureRows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.Numerics.BitOperations.PopCount(uint)",
                    "System.Numerics.BitOperations.PopCount(ulong)",
                    "System.Numerics.BitOperations.PopCount(nuint)",
                    "System.Numerics.BitOperations.RotateLeft(uint, int)",
                    "System.Numerics.BitOperations.RotateLeft(ulong, int)",
                    "System.Numerics.BitOperations.RotateLeft(nuint, int)",
                    "System.Numerics.BitOperations.RotateRight(uint, int)",
                    "System.Numerics.BitOperations.RotateRight(ulong, int)",
                    "System.Numerics.BitOperations.RotateRight(nuint, int)",
                }));

            foreach (var row in generatedPureRows)
            {
                Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
                Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeMathSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Math", limit: 120);

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var generatedPureRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row =>
                    row.GetProperty("Classification").GetString() == "pure" &&
                    row.GetProperty("Symbol").GetString()?.StartsWith("System.Math.", StringComparison.Ordinal) == true)
                .ToArray();

            Assert.That(generatedPureRows, Has.Length.EqualTo(58));

            var representativePureSymbols = new[]
            {
                "System.Math.Abs(double)",
                "System.Math.Abs(int)",
                "System.Math.Clamp(byte, byte, byte)",
                "System.Math.Clamp(System.Decimal, System.Decimal, System.Decimal)",
                "System.Math.Ceiling(System.Decimal)",
                "System.Math.Floor(System.Decimal)",
                "System.Math.Max(System.Decimal, System.Decimal)",
                "System.Math.Min(System.Decimal, System.Decimal)",
                "System.Math.Round(System.Decimal)",
                "System.Math.Truncate(double)",
            };

            foreach (var symbol in representativePureSymbols)
            {
                Assert.That(
                    generatedPureRows.Any(row => string.Equals(row.GetProperty("Symbol").GetString(), symbol, StringComparison.Ordinal)),
                    Is.True,
                    symbol);
            }

            AssertPurityClassification(summary, "System.Math.Ceiling(double)", "conservative_unknown", "metadata_only_or_external");
            AssertPurityClassification(summary, "System.Math.Sqrt(double)", "conservative_unknown", "metadata_only_or_external");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeMemoryExtensionsSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.MemoryExtensions", limit: 80);

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var generatedPureRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row =>
                    row.GetProperty("Classification").GetString() == "pure" &&
                    row.GetProperty("Symbol").GetString()?.StartsWith("System.MemoryExtensions.", StringComparison.Ordinal) == true)
                .ToArray();

            Assert.That(generatedPureRows, Has.Length.EqualTo(38));

            var representativePureSymbols = new[]
            {
                "System.MemoryExtensions.SequenceEqual(System.ReadOnlySpan`1<!!0>, System.ReadOnlySpan`1<!!0>)",
                "System.MemoryExtensions.SequenceEqual(System.Span`1<!!0>, System.ReadOnlySpan`1<!!0>)",
                "System.MemoryExtensions.Trim(System.ReadOnlySpan`1<char>)",
                "System.MemoryExtensions.TrimStart(System.ReadOnlySpan`1<char>, char)",
                "System.MemoryExtensions.TrimEnd(System.ReadOnlySpan`1<char>, char)",
                "System.MemoryExtensions.TrimSplitEntry(System.ReadOnlySpan`1<char>, int, int)",
            };

            foreach (var symbol in representativePureSymbols)
            {
                Assert.That(
                    generatedPureRows.Any(row => string.Equals(row.GetProperty("Symbol").GetString(), symbol, StringComparison.Ordinal)),
                    Is.True,
                    symbol);
            }

            AssertPurityClassification(summary, "System.MemoryExtensions.Trim(System.ReadOnlySpan`1<char>)", "pure");
            AssertPurityClassification(summary, "System.MemoryExtensions.SequenceEqual(System.ReadOnlySpan`1<!!0>, System.ReadOnlySpan`1<!!0>)", "pure");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBitOperationsDeBruijnHelpersSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Numerics.BitOperations", limit: 80);

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

            var catalogComparison = report.GetProperty("CatalogComparison");
            var knownPureRows = catalogComparison.GetProperty("KnownPureMembers").EnumerateArray().ToArray();
            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var generatedRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row =>
                    row.GetProperty("Classification").GetString() == "pure" &&
                    (
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.LeadingZeroCount(uint)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.LeadingZeroCount(ulong)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.Log2(uint)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.Log2(ulong)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.TrailingZeroCount(int)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.TrailingZeroCount(uint)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.TrailingZeroCount(long)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.TrailingZeroCount(ulong)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.RoundUpToPowerOf2(uint)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.Numerics.BitOperations.RoundUpToPowerOf2(ulong)", StringComparison.Ordinal)))
                .ToArray();

            Assert.That(generatedRows, Has.Length.EqualTo(10));
            Assert.That(
                generatedRows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.Numerics.BitOperations.LeadingZeroCount(uint)",
                    "System.Numerics.BitOperations.LeadingZeroCount(ulong)",
                    "System.Numerics.BitOperations.Log2(uint)",
                    "System.Numerics.BitOperations.Log2(ulong)",
                    "System.Numerics.BitOperations.TrailingZeroCount(int)",
                    "System.Numerics.BitOperations.TrailingZeroCount(uint)",
                    "System.Numerics.BitOperations.TrailingZeroCount(long)",
                    "System.Numerics.BitOperations.TrailingZeroCount(ulong)",
                    "System.Numerics.BitOperations.RoundUpToPowerOf2(uint)",
                    "System.Numerics.BitOperations.RoundUpToPowerOf2(ulong)",
                }));

            foreach (var symbol in new[]
            {
                "System.Numerics.BitOperations.LeadingZeroCount(uint)",
                "System.Numerics.BitOperations.LeadingZeroCount(ulong)",
                "System.Numerics.BitOperations.Log2(uint)",
                "System.Numerics.BitOperations.Log2(ulong)",
                "System.Numerics.BitOperations.TrailingZeroCount(int)",
                "System.Numerics.BitOperations.TrailingZeroCount(uint)",
                "System.Numerics.BitOperations.TrailingZeroCount(long)",
                "System.Numerics.BitOperations.TrailingZeroCount(ulong)",
                "System.Numerics.BitOperations.RoundUpToPowerOf2(uint)",
                "System.Numerics.BitOperations.RoundUpToPowerOf2(ulong)",
            })
            {
                Assert.That(knownPureRows.Any(row => string.Equals(row.GetProperty("Symbol").GetString(), symbol, StringComparison.Ordinal)), Is.False);
            }

            foreach (var row in generatedRows)
            {
                Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
                Assert.That(row.GetProperty("HasUnsupportedEffects").GetBoolean(), Is.False);
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeConvertBase64Slice_TreatsRuntimeHelpersAsImpure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Convert.FromBase64", limit: 20);

            var methods = FindMethodsByPrefix(summary, "System.Convert.FromBase64");
            Assert.That(methods.Length, Is.GreaterThan(0));

            AssertPurityClassification(summary, "System.Convert.FromBase64CharArray(char[], int, int)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Convert.FromBase64String(string)", "impure", "impure_callee");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeConvertHexSlice_TreatsRuntimeHelpersAsImpure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Convert.FromHexString", limit: 20);

            var methods = FindMethodsByPrefix(summary, "System.Convert.FromHexString");
            Assert.That(methods.Length, Is.GreaterThan(0));

            AssertPurityClassification(summary, "System.Convert.FromHexString(System.ReadOnlySpan`1<char>)", "impure", "throw");
            AssertPurityClassification(summary, "System.Convert.FromHexString(string)", "impure", "impure_callee");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBitConverterReadSlice_TreatsIntrinsicHelpersAsPure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.BitConverter.ToInt32", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            var knownPureRows = catalogComparison.GetProperty("KnownPureMembers").EnumerateArray().ToArray();

            Assert.That(
                knownPureRows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.BitConverter.ToInt32(byte[], int)",
                    "System.BitConverter.ToInt32(System.ReadOnlySpan`1<byte>)",
                }));

            foreach (var row in knownPureRows)
            {
                Assert.That(row.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
                Assert.That(row.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("none"));
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBitConverterDoubleSlice_TreatsIntrinsicHelpersAsPure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.BitConverter.ToDouble", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            var knownPureRows = catalogComparison.GetProperty("KnownPureMembers").EnumerateArray().ToArray();

            Assert.That(
                knownPureRows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.BitConverter.ToDouble(byte[], int)",
                    "System.BitConverter.ToDouble(System.ReadOnlySpan`1<byte>)",
                }));

            foreach (var row in knownPureRows)
            {
                Assert.That(row.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
                Assert.That(row.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("none"));
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeArrayEmptySlice_TreatsSafeStaticCacheReadsAsPure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Array.Empty", limit: 10);

            var methods = FindMethodsByPrefix(summary, "System.Array.Empty");
            Assert.That(methods.Length, Is.GreaterThan(0));

            foreach (var method in methods)
            {
                var classification = method.GetProperty("PurityClassification");
                Assert.That(classification.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
                Assert.That(classification.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("internal_only"));
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeGuidToByteArraySlice_TreatsRuntimeHelpersAndEndianReadsAsPure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Guid.ToByteArray", limit: 20);

            var methods = FindMethodsByPrefix(summary, "System.Guid.ToByteArray");
            Assert.That(methods.Length, Is.EqualTo(2));

            foreach (var method in methods)
            {
                var symbol = method.GetProperty("Symbol").GetString();
                var classification = method.GetProperty("PurityClassification");
                Assert.That(classification.GetProperty("Classification").GetString(), Is.EqualTo("pure"), symbol);
                Assert.That(classification.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("fresh_array_candidate_via_local_helpers"), symbol);
                Assert.That(classification.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("internal_only"), symbol);
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeUnsafeSlice_TreatsReadUnalignedAsPureAndWriteUnalignedAsImpure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Runtime.CompilerServices.Unsafe", limit: 20);

            var methods = FindMethodsByPrefix(summary, "System.Runtime.CompilerServices.Unsafe");
            var readMethods = methods.Where(method =>
                method.GetProperty("Symbol").GetString() is "System.Runtime.CompilerServices.Unsafe.ReadUnaligned(ref byte)" or
                    "System.Runtime.CompilerServices.Unsafe.ReadUnaligned(void*)")
                .ToArray();
            var writeMethods = methods.Where(method =>
                method.GetProperty("Symbol").GetString() is "System.Runtime.CompilerServices.Unsafe.WriteUnaligned(ref byte, !!0)" or
                    "System.Runtime.CompilerServices.Unsafe.WriteUnaligned(void*, !!0)")
                .ToArray();

            Assert.That(readMethods.Length, Is.EqualTo(2));
            Assert.That(readMethods.All(method => method.GetProperty("PurityClassification").GetProperty("Classification").GetString() == "pure"), Is.True);
            Assert.That(writeMethods.Length, Is.EqualTo(2));
            Assert.That(writeMethods.All(method => method.GetProperty("PurityClassification").GetProperty("Classification").GetString() == "impure"), Is.True);
            Assert.That(writeMethods.All(method => method.GetProperty("PurityClassification").GetProperty("PrimaryCategory").GetString() == "caller_visible_memory_write"), Is.True);

            var asMethods = methods.Where(method =>
                method.GetProperty("Symbol").GetString() is "System.Runtime.CompilerServices.Unsafe.As(object)" or
                    "System.Runtime.CompilerServices.Unsafe.As(ref !!0)")
                .ToArray();
            var sizeOfMethods = methods.Where(method =>
                method.GetProperty("Symbol").GetString() == "System.Runtime.CompilerServices.Unsafe.SizeOf()")
                .ToArray();

            Assert.That(asMethods.Length, Is.EqualTo(2));
            Assert.That(asMethods.All(method => method.GetProperty("PurityClassification").GetProperty("Classification").GetString() == "pure"), Is.True);
            Assert.That(sizeOfMethods.Length, Is.EqualTo(1));
            Assert.That(sizeOfMethods[0].GetProperty("PurityClassification").GetProperty("Classification").GetString(), Is.EqualTo("pure"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringSlice_TreatsToCharArrayAsGeneratedPurityEvidence()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.ToCharArray", limit: 10);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var toCharArrayRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(entry => string.Equals(
                    entry.GetProperty("Symbol").GetString(),
                    "System.String.ToCharArray()",
                    StringComparison.Ordinal) ||
                    string.Equals(
                        entry.GetProperty("Symbol").GetString(),
                        "System.String.ToCharArray(int, int)",
                        StringComparison.Ordinal))
                .ToArray();

            Assert.That(toCharArrayRows.Length, Is.EqualTo(2));
            Assert.That(toCharArrayRows.All(row => row.GetProperty("Classification").GetString() == "pure"), Is.True);
            Assert.That(toCharArrayRows.All(row => row.GetProperty("FreshnessClassification").GetString() == "fresh_array_candidate_via_local_helpers"), Is.True);
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringPrefixSuffixSlice_TreatsStartsWithAndEndsWithAsImpure()
        {
            using var startsWithSummary = await RunRuntimeEffectSummaryAsync("System.String.StartsWith", limit: 20);
            using var endsWithSummary = await RunRuntimeEffectSummaryAsync("System.String.EndsWith", limit: 20);

            AssertPurityClassification(startsWithSummary, "System.String.StartsWith(string)", "impure", "impure_callee");
            AssertEffectVisibilityClassification(startsWithSummary, "System.String.StartsWith(string)", "caller_visible");

            AssertPurityClassification(endsWithSummary, "System.String.EndsWith(string)", "impure", "impure_callee");
            AssertEffectVisibilityClassification(endsWithSummary, "System.String.EndsWith(string)", "caller_visible");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringContainsSlice_TreatsSelectedOverloadsAsPureAndStringSearchAsConservative()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.Contains", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.Contains(string)", "conservative_unknown", "dynamic_dispatch");
            AssertEffectVisibilityClassification(summary, "System.String.Contains(string)", "unknown");
            AssertPurityClassification(summary, "System.String.Contains(char)", "pure");
            AssertPurityClassification(summary, "System.String.Contains(char, System.StringComparison)", "pure");
            AssertPurityClassification(summary, "System.String.Contains(string, System.StringComparison)", "pure");

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var rows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row => row.GetProperty("Symbol").GetString()?.StartsWith("System.String.Contains", StringComparison.Ordinal) == true)
                .ToArray();

            Assert.That(rows, Has.Length.EqualTo(4));
            Assert.That(
                rows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.String.Contains(char)",
                    "System.String.Contains(char, System.StringComparison)",
                    "System.String.Contains(string)",
                    "System.String.Contains(string, System.StringComparison)",
                }));
            Assert.That(rows.Count(row => row.GetProperty("Classification").GetString() == "pure"), Is.EqualTo(3));
            Assert.That(rows.Count(row => row.GetProperty("Classification").GetString() == "conservative_unknown"), Is.EqualTo(1));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeConvertToBase64Slice_TreatsRuntimeHelpersAsImpure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Convert.ToBase64String", limit: 20);

            var methods = FindMethodsByPrefix(summary, "System.Convert.ToBase64String");
            Assert.That(methods.Length, Is.EqualTo(5));

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.Convert.ToBase64String(byte[])", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Convert.ToBase64String(byte[], System.Base64FormattingOptions)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Convert.ToBase64String(byte[], int, int)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Convert.ToBase64String(byte[], int, int, System.Base64FormattingOptions)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Convert.ToBase64String(System.ReadOnlySpan`1<byte>, System.Base64FormattingOptions)", "impure", "global_state_read", "throw");

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var symbols = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.Convert.ToBase64String", StringComparison.Ordinal))
                .ToArray();

            Assert.That(symbols, Is.EqualTo(new[]
            {
                "System.Convert.ToBase64String(System.ReadOnlySpan`1<byte>, System.Base64FormattingOptions)",
                "System.Convert.ToBase64String(byte[])",
                "System.Convert.ToBase64String(byte[], System.Base64FormattingOptions)",
                "System.Convert.ToBase64String(byte[], int, int)",
                "System.Convert.ToBase64String(byte[], int, int, System.Base64FormattingOptions)",
            }));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeConvertToHexSlice_TreatsRuntimeHelpersAsImpure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Convert.ToHexString", limit: 20);

            var methods = FindMethodsByPrefix(summary, "System.Convert.ToHexString");
            Assert.That(methods.Length, Is.EqualTo(3));

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.Convert.ToHexString(byte[])", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Convert.ToHexString(byte[], int, int)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Convert.ToHexString(System.ReadOnlySpan`1<byte>)", "impure", "global_state_read");

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var symbols = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.Convert.ToHexString", StringComparison.Ordinal))
                .ToArray();

            Assert.That(symbols, Is.EqualTo(new[]
            {
                "System.Convert.ToHexString(System.ReadOnlySpan`1<byte>)",
                "System.Convert.ToHexString(byte[])",
                "System.Convert.ToHexString(byte[], int, int)",
            }));
        }

        [Test]
        public async Task EffectSummaryTool_GeneratedPurityCatalog_UsesDistinctExactKeys_ForDuplicateDisplaySymbols()
        {
            var source = """
public readonly struct ConversionFixture
{
    private readonly int _value;

    public ConversionFixture(int value)
    {
        _value = value;
    }

    public static explicit operator int(ConversionFixture value) => value._value;

    public static explicit operator long(ConversionFixture value) => value._value;
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryDuplicateDisplaySymbols", source);
            using var summary = await RunEffectSummaryAsync(
                fixture.AssemblyPath,
                includeTransitiveRoots: true,
                classifyPurity: true,
                compareManualCatalogs: false);

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var operatorEntries = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(entry => string.Equals(
                    entry.GetProperty("Symbol").GetString(),
                    "ConversionFixture.op_Explicit(ConversionFixture)",
                    StringComparison.Ordinal))
                .ToArray();

            Assert.That(operatorEntries.Length, Is.EqualTo(2));
            Assert.That(
                operatorEntries
                    .Select(entry => entry.GetProperty("ExactSymbolKey").GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(2));
        }

        [Test]
        public async Task EffectSummaryTool_ClassifiesFreshObjectConstructionAsInternalOnly()
        {
            var source = """
public sealed class Box
{
    private readonly int _value;

    public Box(int value)
    {
        _value = value;
    }
}

public sealed class MutableBox
{
    public int Value;
}

public static class FreshObjectFixture
{
    public static Box MakeConstructedBox()
    {
        return new Box(42);
    }

    public static MutableBox MakeAssignedBox()
    {
        var box = new MutableBox();
        box.Value = 5;
        return box;
    }

    public static void MutateExistingBox(MutableBox box)
    {
        box.Value = 7;
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryFreshObjectWrites", source);
            using var summary = await RunEffectSummaryAsync(
                fixture.AssemblyPath,
                includeTransitiveRoots: true,
                classifyPurity: true,
                compareManualCatalogs: false);

            AssertPurityClassification(summary, "Box..ctor(int)", "pure");
            AssertFreshnessClassification(summary, "Box..ctor(int)", "fresh_owned_object_write");
            AssertPurityClassification(summary, "FreshObjectFixture.MakeConstructedBox()", "pure");
            AssertPurityClassification(summary, "FreshObjectFixture.MakeAssignedBox()", "pure");
            AssertFreshnessClassification(summary, "FreshObjectFixture.MakeAssignedBox()", "fresh_owned_object_write");
            AssertPurityClassification(summary, "FreshObjectFixture.MutateExistingBox(MutableBox)", "impure", "object_state_write");
        }

        private static void AssertThrownExceptions(JsonDocument summary, string methodSymbol, params string[] expectedExceptions)
        {
            var method = FindMethod(summary, methodSymbol);
            var thrownExceptions = method.GetProperty("ThrownExceptionTypes")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.That(thrownExceptions, Is.EqualTo(expectedExceptions));
        }

        private static void AssertTransitiveExceptions(JsonDocument summary, string methodSymbol, params string[] expectedExceptions)
        {
            var method = FindMethod(summary, methodSymbol);
            var transitiveExceptions = method.GetProperty("TransitiveThrownExceptionTypes")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.That(transitiveExceptions, Is.EqualTo(expectedExceptions));
        }

        private static void AssertPurityClassification(
            JsonDocument summary,
            string methodSymbol,
            string expectedClassification,
            params string[] expectedCategories)
        {
            var method = FindMethod(summary, methodSymbol);
            var classification = method.GetProperty("PurityClassification");
            Assert.That(classification.GetProperty("Classification").GetString(), Is.EqualTo(expectedClassification));

            var categories = classification.GetProperty("Categories")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            foreach (var expectedCategory in expectedCategories)
            {
                Assert.That(categories, Does.Contain(expectedCategory));
            }
        }

        private static void AssertFreshnessClassification(
            JsonDocument summary,
            string methodSymbol,
            string expectedFreshnessClassification)
        {
            var method = FindMethod(summary, methodSymbol);
            var classification = method.GetProperty("PurityClassification");
            Assert.That(
                classification.GetProperty("FreshnessClassification").GetString(),
                Is.EqualTo(expectedFreshnessClassification));
        }

        private static void AssertEffectVisibilityClassification(
            JsonDocument summary,
            string methodSymbol,
            string expectedEffectVisibilityClassification)
        {
            var method = FindMethod(summary, methodSymbol);
            var classification = method.GetProperty("PurityClassification");
            Assert.That(
                classification.GetProperty("EffectVisibilityClassification").GetString(),
                Is.EqualTo(expectedEffectVisibilityClassification));
        }

        private static JsonElement FindMethod(JsonDocument summary, string methodSymbol)
        {
            var methods = summary.RootElement
                .GetProperty("Assemblies")[0]
                .GetProperty("Methods")
                .EnumerateArray()
                .ToArray();

            return methods.Single(method => string.Equals(
                method.GetProperty("Symbol").GetString(),
                methodSymbol,
                StringComparison.Ordinal));
        }

        private static JsonElement[] FindMethodsByPrefix(JsonDocument summary, string methodSymbolPrefix)
        {
            return summary.RootElement
                .GetProperty("Assemblies")[0]
                .GetProperty("Methods")
                .EnumerateArray()
                .Where(method =>
                {
                    var symbol = method.GetProperty("Symbol").GetString();
                    return !string.IsNullOrWhiteSpace(symbol) &&
                        symbol.StartsWith(methodSymbolPrefix, StringComparison.Ordinal);
                })
                .ToArray();
        }

        private static async Task<JsonDocument> RunEffectSummaryAsync(
            string assemblyPath,
            bool includeTransitiveRoots,
            bool classifyPurity = false,
            bool compareManualCatalogs = false)
        {
            var outputPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, Guid.NewGuid().ToString("N") + ".json");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = GetRepositoryRoot(),
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add("Tools\\PurelySharp.EffectSummary\\PurelySharp.EffectSummary.csproj");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--assembly");
            startInfo.ArgumentList.Add(assemblyPath);
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(outputPath);
            if (includeTransitiveRoots)
            {
                startInfo.ArgumentList.Add("--transitive-roots");
            }
            if (classifyPurity)
            {
                startInfo.ArgumentList.Add("--classify-purity");
            }
            if (compareManualCatalogs)
            {
                startInfo.ArgumentList.Add("--compare-manual-catalogs");
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start effect summary tool.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));
            }
            catch (TimeoutException)
            {
                TryKillProcess(process);
                throw new AssertionException("Effect summary tool timed out after 120 seconds.");
            }
            if (process.ExitCode != 0)
            {
                throw new AssertionException(
                    "Effect summary tool failed with exit code " + process.ExitCode + "." + Environment.NewLine +
                    "Assembly: " + assemblyPath + Environment.NewLine +
                    "Output: " + outputPath);
            }

            return JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        }

        private static async Task<JsonDocument> RunRuntimeEffectSummaryAsync(string symbolPrefix, int limit)
        {
            var outputPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "runtime-effect-summary-" + Guid.NewGuid().ToString("N") + ".json");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = GetRepositoryRoot(),
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add("Tools\\PurelySharp.EffectSummary\\PurelySharp.EffectSummary.csproj");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--framework");
            startInfo.ArgumentList.Add("net8.0");
            startInfo.ArgumentList.Add("--symbol-prefix");
            startInfo.ArgumentList.Add(symbolPrefix);
            startInfo.ArgumentList.Add("--include-callees");
            startInfo.ArgumentList.Add("--classify-purity");
            startInfo.ArgumentList.Add("--compare-manual-catalogs");
            startInfo.ArgumentList.Add("--limit");
            startInfo.ArgumentList.Add(limit.ToString());
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(outputPath);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start effect summary tool.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));
            }
            catch (TimeoutException)
            {
                TryKillProcess(process);
                throw new AssertionException("Effect summary tool timed out after 120 seconds.");
            }
            if (process.ExitCode != 0)
            {
                throw new AssertionException(
                    "Effect summary tool failed with exit code " + process.ExitCode + "." + Environment.NewLine +
                    "Symbol prefix: " + symbolPrefix + Environment.NewLine +
                    "Output: " + outputPath);
            }

            return JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        }

        private static async Task<FixtureAssembly> CreateFixtureAssemblyAsync(string assemblyName, string source)
        {
            var tempDirectory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "effect-summary-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var assemblyPath = Path.Combine(tempDirectory, assemblyName + ".dll");

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            await using var stream = File.Create(assemblyPath);
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                throw new AssertionException(string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            }

            return new FixtureAssembly(tempDirectory, assemblyPath);
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

        private static string GetRepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }

        private sealed class FixtureAssembly : IAsyncDisposable
        {
            public FixtureAssembly(string directoryPath, string assemblyPath)
            {
                DirectoryPath = directoryPath;
                AssemblyPath = assemblyPath;
            }

            public string DirectoryPath { get; }

            public string AssemblyPath { get; }

            public ValueTask DisposeAsync()
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}
