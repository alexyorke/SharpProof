using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class Pr275BulkSmtGenericInputTests
{
    [Test]
    public void DecodeCoreTreatsNullToStringAsMalformedChargesTheItemAndDisposesIt()
    {
        using var expression = new NullToStringDisposable();
        NullToStringDisposable[] core = [expression];
        var meter = new SmtQueryResourceMeter(100, CancellationToken.None);

        var result = CallableSolverSession.DecodeCore(
            core,
            new Dictionary<string, int>(),
            "g-current",
            meter);

        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Unknown));
        Assert.That(result.FailureReason, Is.EqualTo(BackendFailureReason.MalformedResult));
        Assert.That(meter.Consumed, Is.EqualTo(1));
        Assert.That(expression.IsDisposed, Is.True);
    }

    private sealed class NullToStringDisposable : IDisposable
    {
        internal bool IsDisposed { get; private set; }

        public override string? ToString()
        {
            return null;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
