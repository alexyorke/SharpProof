using System;
using System.IO;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CompilerEffectClaimArtifactCodecTests
{
    [Test]
    public void SealRejectsNullArtifact()
    {
        Assert.That(
            (Action)(() => CompilerEffectClaimArtifactCodec.Seal(null!)),
            Throws.ArgumentNullException);
    }

    [Test]
    public void SealRejectsConstraintWithoutAllowedExceptionTypes()
    {
        var artifact = new CompilerEffectClaimArtifact
        {
            Constraint = new CompilerEffectConstraintArtifact { AllowedExceptionTypes = default },
        };

        Assert.That(
            (Action)(() => CompilerEffectClaimArtifactCodec.Seal(artifact)),
            Throws.TypeOf<InvalidDataException>());
    }
}
