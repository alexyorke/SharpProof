using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerCaptureIdentityInvariantTests
{
    [Test]
    public void ModuleIdentityFormattingPreservesTheActualCompilerIdentity()
    {
        var compiler = typeof(CSharpCompilation);
        var mvid = compiler.Module.ModuleVersionId;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mvid, Is.Not.EqualTo(Guid.Empty));
            Assert.That(CompilerCaptureIdentity.FormatMvid(mvid), Is.EqualTo(mvid.ToString("D")));
            Assert.That(CompilerCaptureIdentity.CaptureMvid(compiler), Is.EqualTo(mvid.ToString("D")));
        }
    }

    [Test]
    public void EmptyModuleIdentityIsRejectedByTheFormatter()
    {
        Assert.That((TestDelegate)(() => CompilerCaptureIdentity.FormatMvid(Guid.Empty)),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("The compiler MVID is not canonical."));
    }
}
