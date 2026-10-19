using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

public sealed partial class ResourceBoundsTests
{
    [TestCase("for (int i=0; i<5; i+=2) { var a = new int[2]; }", "3", "3", "6")]
    [TestCase("for (int i=2; i<9; i+=3) { var a = new int[2]; }", "3", "3", "6")]
    [TestCase("checked { for (int i=0; i<n; i+=2) {} }", "ceil(max(0, n) / 2)", "0", "0")]
    [TestCase("for (long i=0; i<n; i+=2) {}", "ceil(max(0, n) / 2)", "0", "0")]
    [TestCase("for (int i=n; i>0; i--) {}", "max(0, n)", "0", "0")]
    [TestCase("for (int i=7; i>1; i-=2) { var a = new int[2]; }", "3", "3", "6")]
    [TestCase("for (int i=n; i>0; i/=2) {}", "digits(max(0, n), 2)", "0", "0")]
    [TestCase("for (int i=9; i>0; i/=3) {}", "3", "0", "0")]
    [TestCase("for (int i=1; i<17; i*=2) {}", "5", "0", "0")]
    [TestCase("for (long i=1; i<n; i*=2) {}", "ceil_log(max(0, n), 2)", "0", "0")]
    [TestCase("checked { for (int i=1; i<n; i*=2) {} }", "ceil_log(max(0, n), 2)", "0", "0")]
    [TestCase("checked { for (int i=0; i<2147483647; i+=2) {} }", "1073741824", "0", "0")]
    [TestCase("for (int i=0; i<n; i++) { var a = new int[i]; }", "max(0, n)", "max(0, n)", "(max(0, n) * max(0, n))")]
    [TestCase("for (int i=0; i<n; i++) { for (int j=0; j<i; j++) {} }", "(max(0, n) * (1 + max(0, n)))", "0", "0")]
    [TestCase("int remaining=n; while (remaining>0) { var a = new int[2]; remaining--; }", "max(0, n)", "max(0, n)", "(max(0, n) * 2)")]
    [TestCase("int remaining=n; while (remaining>0) { remaining/=2; }", "digits(max(0, n), 2)", "0", "0")]
    [TestCase("int remaining=7; while (remaining>0) { remaining-=3; }", "3", "0", "0")]
    public void GeneralizedLoopProofsCompose(string body, string loops, string arrays, string elements)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(loops);
        ArgumentNullException.ThrowIfNull(arrays);
        ArgumentNullException.ThrowIfNull(elements);
        var result = Analyze(body);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Bounds!.LoopIterations.Text, Is.EqualTo(loops));
            Assert.That(result.Bounds.Arrays.Text, Is.EqualTo(arrays));
            Assert.That(result.Bounds.Elements.Text, Is.EqualTo(elements));
        }
    }

    [TestCase("for (int i=0; i<n; i+=2) {}", "LoopCounterMayOverflow")]
    [TestCase("for (int i=1; i<n; i*=2) {}", "LoopCounterMayOverflow")]
    [TestCase("for (int i=0; i<n; i*=2) {}", "LoopCounterMayOverflow")]
    [TestCase("for (int i=1; i<10; i*=1) {}", "UnprovedLoopProgress")]
    [TestCase("for (int i=n; i>0; i/=1) {}", "UnprovedLoopProgress")]
    [TestCase("for (int i=n; i>0; i++) {}", "UnprovedLoopProgress")]
    [TestCase("for (long i=0; i<9223372036854775807L; i+=2) {}", "LoopCounterMayOverflow")]
    [TestCase("for (int i=n; i> -1; i--) {}", "UnsupportedLoopStart")]
    [TestCase("for (int i=-1; i<n; i++) {}", "UnsupportedLoopStart")]
    [TestCase("int remaining=n; while (remaining>0) { if(n>0) continue; remaining--; }", "UnsupportedOperation:Branch")]
    [TestCase("int remaining=n; while (remaining>0) { remaining=10; remaining--; }", "UnstableInputOrCounter")]
    [TestCase("int remaining=n; remaining++; while (remaining>0) { remaining--; }", "UnprovedLoopProgress")]
    [TestCase("int remaining=n; while (remaining>0) { remaining/=1; }", "UnprovedLoopProgress")]
    [TestCase("int remaining=n; do { remaining--; } while (remaining>0);", "UnsupportedLoop")]
    public void UnprovedGeneralizedLoopShapesAbstain(string body, string reason)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(reason);
        var result = Analyze(body);
        Assert.That(result.Proven, Is.False);
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.Bounds, Is.Null);
    }

    [Test]
    public void GeneralizedBoundsDominateIndependentSmallExecutions()
    {
        for (int end = -2; end <= 20; end++)
        {
            for (int stride = 1; stride <= 6; stride++)
            {
                int up = 0, down = 0, grow = 0, shrink = 0;
                for (int i = 2; i < end; i += stride)
                { up++; }
                for (int i = end; i > 1; i -= stride)
                { down++; }
                var upResult = Analyze($"for (int i=2; i<{end}; i+={stride}) {{}}");
                var downResult = Analyze($"for (int i={end}; i>1; i-={stride}) {{}}");
                Assert.That(upResult.Proven, Is.True, upResult.UnknownReason);
                Assert.That(downResult.Proven, Is.True, downResult.UnknownReason);
                Assert.That(upResult.Bounds!.LoopIterations.Constant, Is.EqualTo(new System.Numerics.BigInteger(up)));
                Assert.That(downResult.Bounds!.LoopIterations.Constant, Is.EqualTo(new System.Numerics.BigInteger(down)));
                int remaining = end, drained = 0;
                while (remaining > 0)
                { drained++; remaining -= stride; }
                var whileResult = Analyze($"int remaining={end}; while(remaining>0) {{ remaining-={stride}; }}");
                Assert.That(whileResult.Proven, Is.True, whileResult.UnknownReason);
                Assert.That(whileResult.Bounds!.LoopIterations.Constant, Is.EqualTo(new System.Numerics.BigInteger(drained)));
                if (stride == 1)
                { continue; }
                for (int i = 1; i < end; i *= stride)
                { grow++; }
                for (int i = end; i > 0; i /= stride)
                { shrink++; }
                var growResult = Analyze($"for (int i=1; i<{end}; i*={stride}) {{}}");
                var shrinkResult = Analyze($"for (int i={end}; i>0; i/={stride}) {{}}");
                Assert.That(growResult.Proven, Is.True, growResult.UnknownReason);
                Assert.That(shrinkResult.Proven, Is.True, shrinkResult.UnknownReason);
                Assert.That(growResult.Bounds!.LoopIterations.Constant, Is.EqualTo(new System.Numerics.BigInteger(grow)));
                Assert.That(shrinkResult.Bounds!.LoopIterations.Constant, Is.EqualTo(new System.Numerics.BigInteger(shrink)));
            }
        }
    }
}
