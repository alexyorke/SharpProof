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

            Assert.That(generatedRows.Length, Is.GreaterThanOrEqualTo(12));
            var representativeSymbols = new[]
            {
                "System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadHalfBigEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadHalfLittleEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(System.ReadOnlySpan`1<byte>)",
                "System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(System.ReadOnlySpan`1<byte>)",
            };

            foreach (var symbol in representativeSymbols)
            {
                Assert.That(
                    generatedRows.Any(row => string.Equals(row.GetProperty("Symbol").GetString(), symbol, StringComparison.Ordinal)),
                    Is.True,
                    symbol);
            }

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

            Assert.That(generatedPureRows.Length, Is.GreaterThanOrEqualTo(58));

            var representativePureSymbols = new[]
            {
                "System.Math.Abs(double)",
                "System.Math.Clamp(byte, byte, byte)",
                "System.Math.Clamp(System.Decimal, System.Decimal, System.Decimal)",
                "System.Math.Ceiling(System.Decimal)",
                "System.Math.Floor(System.Decimal)",
                "System.Math.Max(System.Decimal, System.Decimal)",
                "System.Math.Min(System.Decimal, System.Decimal)",
                "System.Math.Round(System.Decimal)",
                "System.Math.Round(double)",
            };

            foreach (var symbol in representativePureSymbols)
            {
                Assert.That(
                    generatedPureRows.Any(row => string.Equals(row.GetProperty("Symbol").GetString(), symbol, StringComparison.Ordinal)),
                    Is.True,
                    symbol);
            }

            AssertPurityClassification(summary, "System.Math.Ceiling(double)", "conservative_unknown", "metadata_only_or_external");
            AssertPurityClassification(summary, "System.Math.Floor(double)", "conservative_unknown", "metadata_only_or_external");
            AssertPurityClassification(summary, "System.Math.Sin(double)", "conservative_unknown", "metadata_only_or_external");
            AssertPurityClassification(summary, "System.Math.Sqrt(double)", "conservative_unknown", "metadata_only_or_external");
            AssertPurityClassification(summary, "System.Math.Truncate(double)", "conservative_unknown", "unknown_callee");
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

            Assert.That(generatedPureRows.Length, Is.GreaterThanOrEqualTo(38));

            var representativePureSymbols = new[]
            {
                "System.MemoryExtensions.Contains(System.ReadOnlySpan`1<!!0>, !!0)",
                "System.MemoryExtensions.Contains(System.Span`1<!!0>, !!0)",
                "System.MemoryExtensions.ContainsAny(System.ReadOnlySpan`1<!!0>, System.ReadOnlySpan`1<!!0>)",
                "System.MemoryExtensions.IndexOf(System.ReadOnlySpan`1<!!0>, !!0)",
                "System.MemoryExtensions.SequenceCompareTo(System.Span`1<!!0>, System.ReadOnlySpan`1<!!0>)",
                "System.MemoryExtensions.SequenceEqual(System.Span`1<!!0>, System.ReadOnlySpan`1<!!0>)",
            };

            foreach (var symbol in representativePureSymbols)
            {
                Assert.That(
                    generatedPureRows.Any(row => string.Equals(row.GetProperty("Symbol").GetString(), symbol, StringComparison.Ordinal)),
                    Is.True,
                    symbol);
            }

            AssertPurityClassification(summary, "System.MemoryExtensions.Contains(System.ReadOnlySpan`1<!!0>, !!0)", "pure");
            AssertPurityClassification(summary, "System.MemoryExtensions.SequenceEqual(System.Span`1<!!0>, System.ReadOnlySpan`1<!!0>)", "pure");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeMemoryExtensionsStringAsSpanSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.MemoryExtensions.AsSpan(string)", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.MemoryExtensions.AsSpan(string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.MemoryExtensions.AsSpan(string)", "none");

            var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.MemoryExtensions.AsSpan(string)", StringComparison.Ordinal))
                .ToArray();
            Assert.That(symbols, Does.Contain("System.MemoryExtensions.AsSpan(string)"));
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
        public async Task EffectSummaryTool_RuntimeSha256HashDataSlice_TreatsFreshArrayWrapperAsPure()
        {
            using var summary = await RunEffectSummaryAsync(
                typeof(System.Security.Cryptography.SHA256).Assembly.Location,
                includeTransitiveRoots: true,
                classifyPurity: true,
                compareManualCatalogs: true);

            var methods = FindMethodsByPrefix(summary, "System.Security.Cryptography.SHA256.HashData");
            Assert.That(methods.Length, Is.GreaterThanOrEqualTo(5));

            var byteArrayOverloads = methods.Where(method =>
                method.GetProperty("Symbol").GetString() is
                    "System.Security.Cryptography.SHA256.HashData(byte[])"
                    or "System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan`1<byte>)")
                .ToArray();

            Assert.That(byteArrayOverloads, Has.Length.EqualTo(2));
            Assert.That(
                byteArrayOverloads.All(method =>
                    method.GetProperty("PurityClassification").GetProperty("Classification").GetString() == "pure"),
                Is.True);
            Assert.That(
                byteArrayOverloads.All(method =>
                    method.GetProperty("PurityClassification").GetProperty("FreshnessClassification").GetString() == "fresh_owned_array_write"),
                Is.True);
            Assert.That(
                byteArrayOverloads.All(method =>
                    method.GetProperty("PurityClassification").GetProperty("EffectVisibilityClassification").GetString() == "internal_only"),
                Is.True);
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBitConverterReadSlice_TreatsIntrinsicHelpersAsPure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.BitConverter.ToInt32", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var generatedRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row =>
                    string.Equals(row.GetProperty("Classification").GetString(), "pure", StringComparison.Ordinal) &&
                    (
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.BitConverter.ToInt32(byte[], int)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.BitConverter.ToInt32(System.ReadOnlySpan`1<byte>)", StringComparison.Ordinal)))
                .ToArray();

            Assert.That(
                generatedRows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.BitConverter.ToInt32(byte[], int)",
                    "System.BitConverter.ToInt32(System.ReadOnlySpan`1<byte>)",
                }));

            foreach (var row in generatedRows)
            {
                Assert.That(row.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("none"));
                Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
            }
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBitConverterDoubleSlice_TreatsIntrinsicHelpersAsPure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.BitConverter.ToDouble", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var generatedRows = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(row =>
                    string.Equals(row.GetProperty("Classification").GetString(), "pure", StringComparison.Ordinal) &&
                    (
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.BitConverter.ToDouble(byte[], int)", StringComparison.Ordinal) ||
                        string.Equals(row.GetProperty("Symbol").GetString(), "System.BitConverter.ToDouble(System.ReadOnlySpan`1<byte>)", StringComparison.Ordinal)))
                .ToArray();

            Assert.That(
                generatedRows.Select(row => row.GetProperty("Symbol").GetString()),
                Is.EquivalentTo(new[]
                {
                    "System.BitConverter.ToDouble(byte[], int)",
                    "System.BitConverter.ToDouble(System.ReadOnlySpan`1<byte>)",
                }));

            foreach (var row in generatedRows)
            {
                Assert.That(row.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("none"));
                Assert.That(row.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("none"));
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
        public async Task EffectSummaryTool_RuntimeGuidCoreSlice_ClassifiesComparisonsParsingAndFormattingConservatively()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Guid", limit: 80);

            AssertPurityClassification(summary, "System.Guid.Equals(System.Guid)", "pure");
            AssertPurityClassification(summary, "System.Guid.CompareTo(System.Guid)", "pure");
            AssertPurityClassification(summary, "System.Guid.Parse(string)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Guid.ParseExact(string, string)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Guid.TryParse(string, ref System.Guid)", "impure", "caller_visible_memory_write", "impure_callee");
            AssertPurityClassification(summary, "System.Guid.TryParseExact(string, string, ref System.Guid)", "impure", "caller_visible_memory_write", "impure_callee");
            AssertPurityClassification(summary, "System.Guid.ToString()", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Guid.ToString(string)", "impure", "impure_callee");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimePathCoreSlice_SeparatesPureAndConservativeStringWrappers()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.IO.Path", limit: 80);

            AssertPurityClassification(summary, "System.IO.Path.Combine(string, string)", "pure");
            AssertPurityClassification(summary, "System.IO.Path.HasExtension(string)", "pure");
            AssertPurityClassification(summary, "System.IO.Path.ChangeExtension(string, string)", "pure");
            AssertPurityClassification(summary, "System.IO.Path.GetDirectoryName(string)", "pure");
            AssertPurityClassification(summary, "System.IO.Path.GetExtension(string)", "pure");
            AssertPurityClassification(summary, "System.IO.Path.GetFileName(string)", "pure");
            AssertPurityClassification(summary, "System.IO.Path.GetFileNameWithoutExtension(string)", "pure");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeDateTimeOffsetSlice_TreatsAddMethodsFactoriesAndDerivedHelpersDifferently()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.DateTimeOffset", limit: 80);

            AssertPurityClassification(summary, "System.DateTimeOffset.Add(System.TimeSpan)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTimeOffset.AddDays(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTimeOffset.AddHours(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTimeOffset.AddMilliseconds(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTimeOffset.AddMinutes(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTimeOffset.AddMonths(int)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTimeOffset.AddSeconds(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTimeOffset.AddTicks(long)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTimeOffset.AddYears(int)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTimeOffset.Compare(System.DateTimeOffset, System.DateTimeOffset)", "pure");
            AssertPurityClassification(summary, "System.DateTimeOffset.CompareTo(System.DateTimeOffset)", "pure");
            AssertPurityClassification(summary, "System.DateTimeOffset.Equals(System.DateTimeOffset)", "pure");
            AssertPurityClassification(summary, "System.DateTimeOffset.Equals(System.DateTimeOffset, System.DateTimeOffset)", "pure");
            AssertPurityClassification(summary, "System.DateTimeOffset.Subtract(System.DateTimeOffset)", "pure");
            AssertPurityClassification(summary, "System.DateTimeOffset.ToUnixTimeMilliseconds()", "pure");
            AssertPurityClassification(summary, "System.DateTimeOffset.ToUnixTimeSeconds()", "pure");
            AssertPurityClassification(summary, "System.DateTimeOffset.get_Offset()", "pure");
            AssertPurityClassification(summary, "System.DateTimeOffset.FromUnixTimeMilliseconds(long)", "impure", "global_state_read", "throw");
            AssertPurityClassification(summary, "System.DateTimeOffset.FromUnixTimeSeconds(long)", "impure", "global_state_read", "throw");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeDateTimeSlice_TreatsAddAndRoundTripHelpersDifferently()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.DateTime", limit: 120);

            AssertPurityClassification(summary, "System.DateTime.Add(System.TimeSpan)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.AddDays(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.AddHours(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.AddMilliseconds(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.AddMinutes(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.AddMonths(int)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.AddSeconds(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.AddTicks(long)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.AddYears(int)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.FromBinary(long)", "impure", "global_state_read", "throw");
            AssertPurityClassification(summary, "System.DateTime.FromOADate(double)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.ToOADate()", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.DateTime.ToBinary()", "pure");
            AssertPurityClassification(summary, "System.DateTime.Compare(System.DateTime, System.DateTime)", "pure");
            AssertPurityClassification(summary, "System.DateTime.CompareTo(System.DateTime)", "pure");
            AssertPurityClassification(summary, "System.DateTime.Equals(System.DateTime)", "pure");
            AssertPurityClassification(summary, "System.DateTime.Subtract(System.DateTime)", "pure");
            AssertPurityClassification(summary, "System.DateTime.DaysInMonth(int, int)", "pure");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeVersionSlice_TreatsIntegerConstructorsAsFreshOwnedPure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Version", limit: 40);

            AssertPurityClassification(summary, "System.Version..ctor(int, int)", "pure");
            AssertFreshnessClassification(summary, "System.Version..ctor(int, int)", "fresh_owned_object_write");
            AssertEffectVisibilityClassification(summary, "System.Version..ctor(int, int)", "internal_only");

            AssertPurityClassification(summary, "System.Version..ctor(int, int, int)", "pure");
            AssertFreshnessClassification(summary, "System.Version..ctor(int, int, int)", "fresh_owned_object_write");
            AssertEffectVisibilityClassification(summary, "System.Version..ctor(int, int, int)", "internal_only");

            AssertPurityClassification(summary, "System.Version..ctor(int, int, int, int)", "pure");
            AssertFreshnessClassification(summary, "System.Version..ctor(int, int, int, int)", "fresh_owned_object_write");
            AssertEffectVisibilityClassification(summary, "System.Version..ctor(int, int, int, int)", "internal_only");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeTimeSpanSlice_TreatsConstructorAsPureAndAddAsImpure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.TimeSpan", limit: 80);

            AssertPurityClassification(summary, "System.TimeSpan..ctor(long)", "pure");
            AssertFreshnessClassification(summary, "System.TimeSpan..ctor(long)", "fresh_owned_object_write");
            AssertEffectVisibilityClassification(summary, "System.TimeSpan..ctor(long)", "internal_only");

            AssertPurityClassification(summary, "System.TimeSpan.Add(System.TimeSpan)", "impure", "throw");
            AssertEffectVisibilityClassification(summary, "System.TimeSpan.Add(System.TimeSpan)", "caller_visible");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeUnsafeSlice_TreatsReadUnalignedAsPureAndWriteUnalignedAsImpure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Runtime.CompilerServices.Unsafe", limit: 80);

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
            Assert.That(writeMethods.All(method =>
                method.GetProperty("PurityClassification")
                    .GetProperty("Categories")
                    .EnumerateArray()
                    .Any(category => category.GetString() == "caller_visible_memory_write")), Is.True);

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
        public async Task EffectSummaryTool_RuntimeStringIsNullOrEmptySlice_TreatsHelperAsPure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.IsNullOrEmpty", limit: 10);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.IsNullOrEmpty(string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.IsNullOrEmpty(string)", "none");

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var symbols = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.IsNullOrEmpty", StringComparison.Ordinal))
                .ToArray();

            Assert.That(symbols, Is.EqualTo(new[]
            {
                "System.String.IsNullOrEmpty(string)",
            }));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringIsNullOrWhiteSpaceSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.IsNullOrWhiteSpace", limit: 10);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.IsNullOrWhiteSpace(string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.IsNullOrWhiteSpace(string)", "none");

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var symbols = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.IsNullOrWhiteSpace", StringComparison.Ordinal))
                .ToArray();

            Assert.That(symbols, Is.EqualTo(new[]
            {
                "System.String.IsNullOrWhiteSpace(string)",
            }));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringComparerSlice_TreatsOrdinalGettersAsPure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.StringComparer", limit: 20);

            AssertPurityClassification(summary, "System.StringComparer.get_Ordinal()", "pure");
            AssertEffectVisibilityClassification(summary, "System.StringComparer.get_Ordinal()", "internal_only");
            AssertPurityClassification(summary, "System.StringComparer.get_OrdinalIgnoreCase()", "pure");
            AssertEffectVisibilityClassification(summary, "System.StringComparer.get_OrdinalIgnoreCase()", "internal_only");

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var symbols = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.StringComparer.get_Ordinal", StringComparison.Ordinal))
                .ToArray();

            Assert.That(symbols, Is.EquivalentTo(new[]
            {
                "System.StringComparer.get_Ordinal()",
                "System.StringComparer.get_OrdinalIgnoreCase()",
            }));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringLengthSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.get_Length", limit: 10);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.get_Length()", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.get_Length()", "none");

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var symbols = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.get_Length", StringComparison.Ordinal))
                .ToArray();

            Assert.That(symbols, Is.EqualTo(new[]
            {
                "System.String.get_Length()",
            }));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringTrimSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.Trim", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.Trim()", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Trim()", "none");
            AssertPurityClassification(summary, "System.String.TrimStart()", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.TrimStart()", "none");
            AssertPurityClassification(summary, "System.String.TrimEnd()", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.TrimEnd()", "none");

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var symbols = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.Trim", StringComparison.Ordinal))
                .ToArray();

            Assert.That(symbols, Does.Contain("System.String.Trim()"));
            Assert.That(symbols, Does.Contain("System.String.TrimStart()"));
            Assert.That(symbols, Does.Contain("System.String.TrimEnd()"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringEqualsSlice_TreatsComparisonOverloadsAsGeneratedImpureEvidence()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.Equals", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.Equals(string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Equals(string)", "none");
            AssertPurityClassification(summary, "System.String.Equals(string, string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Equals(string, string)", "none");
            AssertPurityClassification(summary, "System.String.Equals(string, System.StringComparison)", "impure", "throw");
            AssertEffectVisibilityClassification(summary, "System.String.Equals(string, System.StringComparison)", "caller_visible");
            AssertPurityClassification(summary, "System.String.Equals(string, string, System.StringComparison)", "impure", "throw");
            AssertEffectVisibilityClassification(summary, "System.String.Equals(string, string, System.StringComparison)", "caller_visible");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringGetHashCodeSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.GetHashCode", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.GetHashCode()", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.GetHashCode()", "none");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringInvariantCasingSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var lowerSummary = await RunRuntimeEffectSummaryAsync("System.String.ToLowerInvariant", limit: 10);
            using var upperSummary = await RunRuntimeEffectSummaryAsync("System.String.ToUpperInvariant", limit: 10);

            var lowerReport = lowerSummary.RootElement.GetProperty("PurityReport");
            var lowerCatalogComparison = lowerReport.GetProperty("CatalogComparison");
            Assert.That(lowerCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));

            var upperReport = upperSummary.RootElement.GetProperty("PurityReport");
            var upperCatalogComparison = upperReport.GetProperty("CatalogComparison");
            Assert.That(upperCatalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(lowerSummary, "System.String.ToLowerInvariant()", "pure");
            AssertEffectVisibilityClassification(lowerSummary, "System.String.ToLowerInvariant()", "internal_only");
            AssertPurityClassification(upperSummary, "System.String.ToUpperInvariant()", "pure");
            AssertEffectVisibilityClassification(upperSummary, "System.String.ToUpperInvariant()", "internal_only");

            var lowerSymbols = lowerSummary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.ToLowerInvariant", StringComparison.Ordinal))
                .ToArray();
            Assert.That(lowerSymbols, Is.EqualTo(new[]
            {
                "System.String.ToLowerInvariant()",
            }));

            var upperSymbols = upperSummary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.ToUpperInvariant", StringComparison.Ordinal))
                .ToArray();
            Assert.That(upperSymbols, Is.EqualTo(new[]
            {
                "System.String.ToUpperInvariant()",
            }));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringConcatSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.Concat", limit: 60);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.Concat(string, string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Concat(string, string)", "internal_only");
            AssertPurityClassification(summary, "System.String.Concat(string[])", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Concat(string[])", "internal_only");

            var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.Concat", StringComparison.Ordinal))
                .ToArray();
            Assert.That(symbols, Does.Contain("System.String.Concat(string, string)"));
            Assert.That(symbols, Does.Contain("System.String.Concat(string[])"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringSubstringSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.Substring", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.Substring(int)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Substring(int)", "internal_only");
            AssertPurityClassification(summary, "System.String.Substring(int, int)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Substring(int, int)", "internal_only");

            var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.Substring", StringComparison.Ordinal))
                .ToArray();
            Assert.That(symbols, Does.Contain("System.String.Substring(int)"));
            Assert.That(symbols, Does.Contain("System.String.Substring(int, int)"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringReplaceSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.Replace", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.Replace(string, string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Replace(string, string)", "internal_only");

            var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.Replace", StringComparison.Ordinal))
                .ToArray();
            Assert.That(symbols, Does.Contain("System.String.Replace(string, string)"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringIndexOfSlice_TreatsDefaultStringSearchAsGeneratedImpureEvidence()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.IndexOf", limit: 80);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.IndexOf(char)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.IndexOf(char)", "none");
            AssertPurityClassification(summary, "System.String.IndexOf(string)", "impure", "impure_callee");
            AssertEffectVisibilityClassification(summary, "System.String.IndexOf(string)", "caller_visible");
            AssertPurityClassification(summary, "System.String.IndexOf(string, System.StringComparison)", "impure", "impure_callee");
            AssertEffectVisibilityClassification(summary, "System.String.IndexOf(string, System.StringComparison)", "caller_visible");
            AssertPurityClassification(summary, "System.String.IndexOf(string, int, int, System.StringComparison)", "impure", "throw");
            AssertEffectVisibilityClassification(summary, "System.String.IndexOf(string, int, int, System.StringComparison)", "caller_visible");

            var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.IndexOf", StringComparison.Ordinal))
                .ToArray();
            Assert.That(symbols, Does.Contain("System.String.IndexOf(char)"));
            Assert.That(symbols, Does.Contain("System.String.IndexOf(string)"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringCloneCompareToAndToStringSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync(
                40,
                "System.String.Clone",
                "System.String.CompareTo",
                "System.String.ToString");

            AssertPurityClassification(summary, "System.String.Clone()", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Clone()", "none");
            AssertPurityClassification(summary, "System.String.CompareTo(string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.CompareTo(string)", "none");
            AssertPurityClassification(summary, "System.String.ToString()", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.ToString()", "none");

            var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol =>
                    string.Equals(symbol, "System.String.Clone()", StringComparison.Ordinal) ||
                    string.Equals(symbol, "System.String.CompareTo(string)", StringComparison.Ordinal) ||
                    string.Equals(symbol, "System.String.ToString()", StringComparison.Ordinal))
                .ToArray();
            Assert.That(symbols, Does.Contain("System.String.Clone()"));
            Assert.That(symbols, Does.Contain("System.String.CompareTo(string)"));
            Assert.That(symbols, Does.Contain("System.String.ToString()"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringInsertPadLeftAndRemoveSlice_UsesGeneratedPurityAndImpureEvidence()
        {
            using var summary = await RunRuntimeEffectSummaryAsync(
                60,
                "System.String.Insert",
                "System.String.PadLeft",
                "System.String.Remove");

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.Insert(int, string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Insert(int, string)", "none");
            AssertPurityClassification(summary, "System.String.PadLeft(int)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.PadLeft(int)", "none");
            AssertPurityClassification(summary, "System.String.Remove(int)", "impure", "throw");
            AssertEffectVisibilityClassification(summary, "System.String.Remove(int)", "caller_visible");
            AssertPurityClassification(summary, "System.String.Remove(int, int)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Remove(int, int)", "internal_only");

            var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol =>
                    string.Equals(symbol, "System.String.Insert(int, string)", StringComparison.Ordinal) ||
                    string.Equals(symbol, "System.String.PadLeft(int)", StringComparison.Ordinal) ||
                    string.Equals(symbol, "System.String.Remove(int)", StringComparison.Ordinal) ||
                    string.Equals(symbol, "System.String.Remove(int, int)", StringComparison.Ordinal))
                .ToArray();
            Assert.That(symbols, Does.Contain("System.String.Insert(int, string)"));
            Assert.That(symbols, Does.Contain("System.String.PadLeft(int)"));
            Assert.That(symbols, Does.Contain("System.String.Remove(int)"));
            Assert.That(symbols, Does.Contain("System.String.Remove(int, int)"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringBuilderToStringSlice_UsesGeneratedImpureEvidence()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Text.StringBuilder.ToString", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.Text.StringBuilder.ToString()", "impure", "throw");
            AssertEffectVisibilityClassification(summary, "System.Text.StringBuilder.ToString()", "caller_visible");
            AssertPurityClassification(summary, "System.Text.StringBuilder.ToString(int, int)", "impure", "throw");
            AssertEffectVisibilityClassification(summary, "System.Text.StringBuilder.ToString(int, int)", "caller_visible");

            var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.Text.StringBuilder.ToString", StringComparison.Ordinal))
                .ToArray();
            Assert.That(symbols, Does.Contain("System.Text.StringBuilder.ToString()"));
            Assert.That(symbols, Does.Contain("System.Text.StringBuilder.ToString(int, int)"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringSplitSlice_UsesGeneratedFreshArrayEvidence()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.Split", limit: 80);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.Split(char[])", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Split(char[])", "internal_only");
            AssertFreshnessClassification(summary, "System.String.Split(char[])", "fresh_owned_array_write");
            AssertPurityClassification(summary, "System.String.Split(char[], System.StringSplitOptions)", "pure");
            AssertFreshnessClassification(summary, "System.String.Split(char[], System.StringSplitOptions)", "fresh_owned_array_write");
            AssertPurityClassification(summary, "System.String.Split(string[], System.StringSplitOptions)", "pure");
            AssertFreshnessClassification(summary, "System.String.Split(string[], System.StringSplitOptions)", "fresh_owned_array_write");

            var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.Split", StringComparison.Ordinal))
                .ToArray();
            Assert.That(symbols, Does.Contain("System.String.Split(char[])"));
            Assert.That(symbols, Does.Contain("System.String.Split(char[], System.StringSplitOptions)"));
            Assert.That(symbols, Does.Contain("System.String.Split(string[], System.StringSplitOptions)"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringJoinSlice_UsesGeneratedPurityForArrayOverloads()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.Join", limit: 80);

            AssertPurityClassification(summary, "System.String.Join(string, string[])", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Join(string, string[])", "none");
            AssertPurityClassification(summary, "System.String.Join(string, string[], int, int)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Join(string, string[], int, int)", "none");
            AssertPurityClassification(summary, "System.String.Join(string, System.Collections.Generic.IEnumerable`1<string>)", "conservative_unknown", "dynamic_dispatch");
            AssertEffectVisibilityClassification(summary, "System.String.Join(string, System.Collections.Generic.IEnumerable`1<string>)", "unknown");

            var symbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.String.Join", StringComparison.Ordinal))
                .ToArray();
            Assert.That(symbols, Does.Contain("System.String.Join(string, string[])"));
            Assert.That(symbols, Does.Contain("System.String.Join(string, string[], int, int)"));
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
        public async Task EffectSummaryTool_RuntimeStringContainsSlice_TreatsSelectedOverloadsAsPure()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.Contains", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.String.Contains(string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.String.Contains(string)", "none");
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
            Assert.That(rows.Count(row => row.GetProperty("Classification").GetString() == "pure"), Is.EqualTo(4));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBooleanAndCharToStringSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync(40, "System.Boolean.ToString", "System.Char.ToString");

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.Boolean.ToString()", "pure");
            AssertEffectVisibilityClassification(summary, "System.Boolean.ToString()", "none");
            AssertPurityClassification(summary, "System.Char.ToString()", "pure");
            AssertEffectVisibilityClassification(summary, "System.Char.ToString()", "none");
            AssertPurityClassification(summary, "System.Char.ToString(char)", "pure");
            AssertEffectVisibilityClassification(summary, "System.Char.ToString(char)", "none");

            var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                    (symbol.StartsWith("System.Boolean.ToString", StringComparison.Ordinal) ||
                     symbol.StartsWith("System.Char.ToString", StringComparison.Ordinal)))
                .ToArray();

            Assert.That(generatedSymbols, Is.EquivalentTo(new[]
            {
                "System.Boolean.ToString()",
                "System.Boolean.ToString(System.IFormatProvider)",
                "System.Char.ToString()",
                "System.Char.ToString(System.IFormatProvider)",
                "System.Char.ToString(char)",
            }));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeEnumTryParseSlice_UsesSemanticHandlingInsteadOfManualCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync(80, "System.Enum.TryParse");

            var knownPureRows = summary.RootElement.GetProperty("PurityReport")
                .GetProperty("CatalogComparison")
                .GetProperty("KnownPureMembers")
                .EnumerateArray()
                .Where(row => row.GetProperty("Symbol").GetString() is string symbol &&
                    symbol.StartsWith("System.Enum.TryParse", StringComparison.Ordinal))
                .ToArray();

            Assert.That(knownPureRows, Is.Empty);

            AssertPurityClassification(summary, "System.Enum.TryParse(string, ref !!0)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Enum.TryParse(string, bool, ref !!0)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Enum.TryParse(System.ReadOnlySpan`1<char>, ref !!0)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Enum.TryParse(System.ReadOnlySpan`1<char>, bool, ref !!0)", "impure", "impure_callee");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeEnumParseSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync(80, "System.Enum.Parse");

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.Enum.Parse(System.Type, string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.Enum.Parse(System.Type, string)", "none");
            AssertPurityClassification(summary, "System.Enum.Parse(System.Type, string, bool)", "pure");
            AssertEffectVisibilityClassification(summary, "System.Enum.Parse(System.Type, string, bool)", "none");
            AssertPurityClassification(summary, "System.Enum.Parse(string)", "pure");
            AssertEffectVisibilityClassification(summary, "System.Enum.Parse(string)", "none");
            AssertPurityClassification(summary, "System.Enum.Parse(string, bool)", "pure");
            AssertEffectVisibilityClassification(summary, "System.Enum.Parse(string, bool)", "none");

            var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                    symbol.StartsWith("System.Enum.Parse", StringComparison.Ordinal))
                .ToArray();

            Assert.That(generatedSymbols, Is.EquivalentTo(new[]
            {
                "System.Enum.Parse(System.ReadOnlySpan`1<char>)",
                "System.Enum.Parse(System.ReadOnlySpan`1<char>, bool)",
                "System.Enum.Parse(System.Type, System.ReadOnlySpan`1<char>)",
                "System.Enum.Parse(System.Type, System.ReadOnlySpan`1<char>, bool)",
                "System.Enum.Parse(System.Type, string)",
                "System.Enum.Parse(System.Type, string, bool)",
                "System.Enum.Parse(string)",
                "System.Enum.Parse(string, bool)",
            }));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeUriIsWellFormedSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsyncForAssembly("System.Private.Uri.dll", 40, "System.Uri.IsWellFormedUriString");

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.Uri.IsWellFormedUriString(string, System.UriKind)", "pure");
            AssertEffectVisibilityClassification(summary, "System.Uri.IsWellFormedUriString(string, System.UriKind)", "none");

            var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) &&
                    symbol.StartsWith("System.Uri.IsWellFormedUriString", StringComparison.Ordinal))
                .ToArray();

            Assert.That(generatedSymbols, Is.EqualTo(new[]
            {
                "System.Uri.IsWellFormedUriString(string, System.UriKind)",
            }));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeIPAddressParseSlice_UsesSemanticHandlingInsteadOfManualCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsyncForAssembly("System.Net.Primitives.dll", 80, "System.Net.IPAddress");

            var knownPureRows = summary.RootElement.GetProperty("PurityReport")
                .GetProperty("CatalogComparison")
                .GetProperty("KnownPureMembers")
                .EnumerateArray()
                .Where(row => row.GetProperty("Symbol").GetString() is string symbol &&
                    symbol.StartsWith("System.Net.IPAddress.Parse", StringComparison.Ordinal))
                .ToArray();

            Assert.That(knownPureRows, Is.Empty);

            AssertPurityClassification(summary, "System.Net.IPAddress.Parse(string)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Net.IPAddress.Parse(System.ReadOnlySpan`1<char>)", "impure", "impure_callee");
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
            AssertPurityClassification(summary, "System.Convert.ToBase64String(System.ReadOnlySpan`1<byte>, System.Base64FormattingOptions)", "impure", "throw");

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
        public async Task EffectSummaryTool_RuntimeConvertToHexSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Convert.ToHexString", limit: 20);

            var methods = FindMethodsByPrefix(summary, "System.Convert.ToHexString");
            Assert.That(methods.Length, Is.EqualTo(3));

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.Convert.ToHexString(byte[])", "pure");
            AssertEffectVisibilityClassification(summary, "System.Convert.ToHexString(byte[])", "none");
            AssertPurityClassification(summary, "System.Convert.ToHexString(byte[], int, int)", "pure");
            AssertEffectVisibilityClassification(summary, "System.Convert.ToHexString(byte[], int, int)", "none");
            AssertPurityClassification(summary, "System.Convert.ToHexString(System.ReadOnlySpan`1<byte>)", "pure");
            AssertEffectVisibilityClassification(summary, "System.Convert.ToHexString(System.ReadOnlySpan`1<byte>)", "internal_only");

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
        public async Task EffectSummaryTool_RuntimeConvertCurrentCultureStringSlice_UsesGeneratedPurityCatalogEntries()
        {
            var symbols = new[]
            {
                "System.Convert.ToSingle(string)",
                "System.Convert.ToDouble(string)",
                "System.Convert.ToByte(string)",
                "System.Convert.ToDateTime(string)",
                "System.Convert.ToSByte(string)",
                "System.Convert.ToInt16(string)",
                "System.Convert.ToInt32(string)",
                "System.Convert.ToInt64(string)",
                "System.Convert.ToUInt16(string)",
                "System.Convert.ToUInt32(string)",
                "System.Convert.ToUInt64(string)",
            };

            using var summary = await RunRuntimeEffectSummaryAsync(120, symbols);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            foreach (var symbol in symbols)
            {
                AssertPurityClassification(summary, symbol, "pure");
                AssertEffectVisibilityClassification(summary, symbol, "none");
            }

            var generatedSymbols = summary.RootElement.GetProperty("GeneratedPurityCatalog")
                .GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbols.Contains(symbol, StringComparer.Ordinal))
                .ToArray();

            Assert.That(generatedSymbols, Is.EquivalentTo(symbols));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeWebUtilitySlice_TreatsHelpersAsGeneratedImpureEvidence()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Net.WebUtility", limit: 40);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.Net.WebUtility.HtmlEncode(string)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Net.WebUtility.HtmlDecode(string)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Net.WebUtility.UrlEncode(string)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Net.WebUtility.UrlDecode(string)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Net.WebUtility.UrlEncodeToBytes(byte[], int, int)", "impure", "impure_callee");
            AssertPurityClassification(summary, "System.Net.WebUtility.UrlDecodeToBytes(byte[], int, int)", "pure");
            AssertEffectVisibilityClassification(summary, "System.Net.WebUtility.UrlDecodeToBytes(byte[], int, int)", "internal_only");
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeListSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.Collections.Generic.List", limit: 120);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.Collections.Generic.List`1.Contains(!0)", "pure");
            AssertPurityClassification(summary, "System.Collections.Generic.List`1.get_Count()", "pure");
            AssertPurityClassification(summary, "System.Collections.Generic.List`1.get_Item(int)", "pure");
            AssertPurityClassification(summary, "System.Collections.Generic.List`1.Exists(System.Predicate`1<!0>)", "pure");
            AssertPurityClassification(summary, "System.Collections.Generic.List`1.Find(System.Predicate`1<!0>)", "impure", "caller_visible_memory_write");
            AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.Find(System.Predicate`1<!0>)", "caller_visible");
            AssertPurityClassification(summary, "System.Collections.Generic.List`1.TrueForAll(System.Predicate`1<!0>)", "conservative_unknown", "dynamic_dispatch", "virtual_call");
            AssertEffectVisibilityClassification(summary, "System.Collections.Generic.List`1.TrueForAll(System.Predicate`1<!0>)", "unknown");

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var generatedSymbols = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Select(entry => entry.GetProperty("Symbol").GetString())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && symbol.StartsWith("System.Collections.Generic.List`1.", StringComparison.Ordinal))
                .ToArray();

            Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Contains(!0)"));
            Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.get_Count()"));
            Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.get_Item(int)"));
            Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Exists(System.Predicate`1<!0>)"));
            Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.Find(System.Predicate`1<!0>)"));
            Assert.That(generatedSymbols, Does.Contain("System.Collections.Generic.List`1.TrueForAll(System.Predicate`1<!0>)"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeFileNotFoundExceptionSlice_UsesGeneratedPurityCatalogEntries()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.IO.FileNotFoundException", limit: 80);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));

            AssertPurityClassification(summary, "System.IO.FileNotFoundException..ctor(string)", "pure");
            AssertFreshnessClassification(summary, "System.IO.FileNotFoundException..ctor(string)", "fresh_owned_object_write");
            AssertEffectVisibilityClassification(summary, "System.IO.FileNotFoundException..ctor(string)", "internal_only");

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var ctorEntry = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Single(entry => string.Equals(
                    entry.GetProperty("Symbol").GetString(),
                    "System.IO.FileNotFoundException..ctor(string)",
                    StringComparison.Ordinal));

            Assert.That(ctorEntry.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
            Assert.That(ctorEntry.GetProperty("PrimaryCategory").GetString(), Is.EqualTo("generated_purity_summary"));
            Assert.That(ctorEntry.GetProperty("FreshnessClassification").GetString(), Is.EqualTo("fresh_owned_object_write"));
            Assert.That(ctorEntry.GetProperty("EffectVisibilityClassification").GetString(), Is.EqualTo("internal_only"));
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

        [Test]
        public async Task EffectSummaryTool_DoesNotTreatNonVirtualCallvirtAsDynamicDispatch()
        {
            var source = """
public sealed class Counter
{
    private readonly int _value;

    public Counter(int value)
    {
        _value = value;
    }

    public int GetValue()
    {
        return _value;
    }
}

public static class CallvirtFixture
{
    public static int Read(Counter counter)
    {
        return counter.GetValue();
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryCallvirtFixture", source);
            using var summary = await RunEffectSummaryAsync(
                fixture.AssemblyPath,
                includeTransitiveRoots: true,
                classifyPurity: true,
                compareManualCatalogs: false);

            AssertPurityClassification(summary, "Counter.GetValue()", "pure");
            AssertPurityClassification(summary, "CallvirtFixture.Read(Counter)", "pure");
            AssertEffectVisibilityClassification(summary, "CallvirtFixture.Read(Counter)", "none");
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
            var outputPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "effect-summary-" + Guid.NewGuid().ToString("N") + ".json");
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

        private static Task<JsonDocument> RunRuntimeEffectSummaryAsync(string symbolPrefix, int limit)
        {
            return RunRuntimeEffectSummaryAsync(limit, symbolPrefix);
        }

        private static async Task<JsonDocument> RunRuntimeEffectSummaryAsync(int limit, params string[] symbolPrefixes)
        {
            return await RunRuntimeEffectSummaryAsyncCore(limit, null, symbolPrefixes);
        }

        private static Task<JsonDocument> RunRuntimeEffectSummaryAsyncForAssembly(string runtimeAssemblyName, int limit, params string[] symbolPrefixes)
        {
            return RunRuntimeEffectSummaryAsyncCore(limit, runtimeAssemblyName, symbolPrefixes);
        }

        private static async Task<JsonDocument> RunRuntimeEffectSummaryAsyncCore(
            int limit,
            string? runtimeAssemblyName,
            params string[] symbolPrefixes)
        {
            if (symbolPrefixes.Length == 0)
            {
                throw new ArgumentException("At least one symbol prefix is required.", nameof(symbolPrefixes));
            }

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
            if (!string.IsNullOrWhiteSpace(runtimeAssemblyName))
            {
                startInfo.ArgumentList.Add("--runtime-assembly");
                startInfo.ArgumentList.Add(runtimeAssemblyName);
            }
            foreach (var symbolPrefix in symbolPrefixes)
            {
                startInfo.ArgumentList.Add("--symbol-prefix");
                startInfo.ArgumentList.Add(symbolPrefix);
            }
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
                    "Symbol prefixes: " + string.Join(", ", symbolPrefixes) + Environment.NewLine +
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
