using System;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerCachePathTests
{
    [Test]
    public void ResolveRejectsNullProjectDirectory()
    {
        Assert.That(
            (Action)(() => WorkerCachePath.Resolve(null, null!)),
            Throws.ArgumentNullException);
    }
}
