using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

// Held source proposal; requires the production FillExactly extraction.
[TestFixture]
public sealed class CompilerIlStreamFillTests
{
    private static readonly ImmutableArray<(int Offset, int Count)> ExpectedPartialReads = [(0, 81920), (50000, 31921)];
    private static readonly ImmutableArray<byte> ExpectedUntouchedDestination = [0xcc, 0xcc];
    private static readonly ImmutableArray<byte> TwoByteSource = [10, 20];
    private static readonly ImmutableArray<byte> OneByteSource = [10];

    [Test]
    public void GenuineCapturedScalarDependencyStillUsesTheProviderAndBodyMemoization()
    {
        using var subject = new MetadataTestSubject(
            "public static class Library { public static int Target(int value) => value; }",
            "public static class Subject { public static int Target(int value) => Library.Target(value); }");
        var method = (IMethodSymbol)subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").Single();
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
        var provider = new CompilerTotalIlBodyProvider(subject.Compilation, references);

        var body = provider.Resolve(method, TestContext.CurrentContext.CancellationToken);

        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Method, Is.SameAs(method));
        Assert.That(body.Instructions, Is.Not.Empty);
        Assert.That(provider.Resolve(method, TestContext.CurrentContext.CancellationToken), Is.SameAs(body));
        foreach (var value in ImmutableArray.Create(-2, 0, 7))
        {
            Assert.That(subject.Method.Invoke(null, [value]), Is.EqualTo(value));
            Assert.That(subject.RootMethod.Invoke(null, [value]), Is.EqualTo(value));
        }
    }

    [TestCase(1)]
    [TestCase(81920)]
    [TestCase(81921)]
    [TestCase(163841)]
    public void ExactFillCopiesFromCurrentPositionWithoutReadingBeyondDestination(int length)
    {
        var source = Enumerable.Range(0, length + 3).Select(index => ((index < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(index)) : ((byte)(index % 251)))).ToArray();
        using var stream = new MemoryStream(source, writable: false);
        stream.Position = 1;
        var destination = new byte[length];

        CompilerTotalIlBodyProvider.FillExactly(stream, destination, TestContext.CurrentContext.CancellationToken);

        Assert.That(destination, Is.EqualTo(source.Skip(1).Take(length).ToImmutableArray()));
        Assert.That(stream.Position, Is.EqualTo(length + 1));
        Assert.That(stream.ReadByte(), Is.EqualTo((int)source[length + 1]));
    }

    [TestCase(0)]
    [TestCase(81920)]
    public void PrematureEofThrowsAfterKeepingTheAvailablePrefix(int available)
    {
        var source = Enumerable.Range(0, available).Select(index => ((index < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(index)) : ((byte)(index % 251)))).ToArray();
        using var stream = new MemoryStream(source, writable: false);
        var destination = Enumerable.Repeat((byte)0xcc, available + 1).ToArray();

        Assert.Throws<EndOfStreamException>((Action)(() =>
            CompilerTotalIlBodyProvider.FillExactly(stream, destination, TestContext.CurrentContext.CancellationToken)));

        Assert.That(destination.Take(available).ToImmutableArray(), Is.EqualTo(source));
        Assert.That(destination[available], Is.EqualTo((byte)0xcc));
        Assert.That(stream.Position, Is.EqualTo(available));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void PositivePartialReadsContinueUsingTheOriginalChunkBound()
    {
        var source = Enumerable.Range(0, 81921).Select(index => ((index < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(index)) : ((byte)(index % 251)))).ToArray();
        using var inner = new MemoryStream(source, writable: false);
        using var stream = new PartialReadStream(inner, 50000);
        var destination = new byte[source.Length];

        CompilerTotalIlBodyProvider.FillExactly(stream, destination, TestContext.CurrentContext.CancellationToken);

        Assert.That(destination, Is.EqualTo(source));
        Assert.That(stream.Reads, Is.EqualTo(ExpectedPartialReads));
        Assert.That(inner.Position, Is.EqualTo(source.Length));
        Assert.That(inner.CanRead, Is.True);
    }

    [Test]
    public void CancellationPrecedesReadingAndLeavesTheDestinationUntouched()
    {
        using var stream = new MemoryStream(TwoByteSource.ToArray(), writable: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var destination = new byte[] { 0xcc, 0xcc };

        var error = Assert.Throws<OperationCanceledException>((Action)(() =>
            CompilerTotalIlBodyProvider.FillExactly(stream, destination, cancellation.Token)));

        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(stream.Position, Is.Zero);
        Assert.That(destination, Is.EqualTo(ExpectedUntouchedDestination));
        Assert.That(stream.ReadByte(), Is.EqualTo(10));
    }

    [Test]
    public void CancellationBetweenPartialReadsKeepsTheUnreadDestinationUntouched()
    {
        var source = Enumerable.Range(0, 81921).Select(index => ((index < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(index)) : ((byte)(index % 251)))).ToArray();
        using var inner = new MemoryStream(source, writable: false);
        using var cancellation = new CancellationTokenSource();
        using var stream = new PartialReadStream(inner, 50000, cancellation);
        var destination = Enumerable.Repeat((byte)0xcc, source.Length).ToArray();

        var error = Assert.Throws<OperationCanceledException>((Action)(() =>
            CompilerTotalIlBodyProvider.FillExactly(stream, destination, cancellation.Token)));

        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(cancellation.IsCancellationRequested, Is.True);
        Assert.That(stream.Reads, Has.Count.EqualTo(1));
        Assert.That(stream.Reads[0], Is.EqualTo((Offset: 0, Count: 81920)));
        Assert.That(inner.Position, Is.EqualTo(50000));
        Assert.That(destination.Take(50000).ToImmutableArray(), Is.EqualTo(source.Take(50000).ToImmutableArray()));
        Assert.That(destination.Skip(50000).All(value => value == 0xcc), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyDestinationDoesNotReadOrObserveCancellation(bool canceled)
    {
        using var stream = new MemoryStream(OneByteSource.ToArray(), writable: false);
        using var cancellation = new CancellationTokenSource();
        if (canceled)
        { cancellation.Cancel(); }

        CompilerTotalIlBodyProvider.FillExactly(stream, [], cancellation.Token);

        Assert.That(stream.Position, Is.Zero);
        Assert.That(stream.ReadByte(), Is.EqualTo(10));
    }

    // A normal read-only, non-seekable Stream adapter that permits positive
    // short reads; no compiler object, PE image, authority or provider hook.
    private sealed class PartialReadStream(Stream inner, int maximumRead, CancellationTokenSource? cancellation = null) : Stream
    {
        internal List<(int Offset, int Count)> Reads { get; } = (inner is null ? throw new System.ArgumentNullException(nameof(inner)) : []);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            System.ArgumentNullException.ThrowIfNull(buffer, nameof(buffer));
            Reads.Add((offset, count));
            var read = inner.Read(buffer, offset, Math.Min(count, maximumRead));
            if (read > 0 && Reads.Count == 1)
            { cancellation?.Cancel(); }
            return read;
        }

        public override void Flush() { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { System.ArgumentNullException.ThrowIfNull(buffer, nameof(buffer)); throw new NotSupportedException(); }
    }
}
