using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

public sealed partial class ResourceBoundsTests
{
    [TestCase("int left=2147483647,right=2147483647; while(left>0||right>0) { if(left>0) left--; else right--; }", "4294967294", "0", "0")]
    [TestCase("long left=9223372036854775807L,right=9223372036854775807L; while(left>0||right>0) { if(left>0) left--; else right--; }", "18446744073709551614", "0", "0")]
    [TestCase("int left=2147483647,right=2147483647; while(left>0||right>0) { if(left>0) left-=2147483647; else right/=2147483647; }", "3", "0", "0")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { if(left>0) { left--; } else { right--; } }", "5", "0", "0")]
    [TestCase("int left=5,right=4; while(left>0||right>0) { if(left>0) { var a=new int[2]; left-=2; } else { var b=new int[3]; right--; } }", "7", "7", "18")]
    [TestCase("int left=8,right=9; while(left>0||right>0) { if(left>0) { left/=2; } else { right/=3; } }", "7", "0", "0")]
    [TestCase("int left=-2,right=3; while(left>0||right>0) { if(left>0) { left--; } else { right--; } }", "3", "0", "0")]
    [TestCase("int left=3,right=-2; while(left>0||right>0) { if(left>0) { left--; } else { right--; } }", "3", "0", "0")]
    [TestCase("int left=-2,right=-3; while(left>0||right>0) { if(left>0) { left--; } else { right--; } }", "0", "0", "0")]
    [TestCase("long left=2; int right=3; while(left>0||right>0) { if(left>0) { left--; } else { right--; } }", "5", "0", "0")]
    [TestCase("int left=n,right=n; while(left>0||right>0) { if(left>0) { left--; } else { right--; } }", "(max(0, n) + max(0, n))", "0", "0")]
    [TestCase("int left=2,right=1; while(left>0||right>0) { if(left>0) { for(int j=0;j<3;j++) {} left--; } else { right--; } }", "9", "0", "0")]
    [TestCase("int left=2,right=3; while(left>0||right>0) if(left>0) left--; else right--;", "5", "0", "0")]
    [TestCase("int i=0; while(i<5) { var a=new int[2]; i+=2; }", "3", "3", "6")]
    [TestCase("int i=2; while(i<9) { i+=3; }", "3", "0", "0")]
    [TestCase("checked { int i=0; while(i<n) { i+=2; } }", "ceil(max(0, n) / 2)", "0", "0")]
    [TestCase("long i=0; while(i<n) { i+=2; }", "ceil(max(0, n) / 2)", "0", "0")]
    [TestCase("int i=1; while(i<17) { i*=2; }", "5", "0", "0")]
    [TestCase("checked { int i=1; while(i<n) { i*=2; } }", "ceil_log(max(0, n), 2)", "0", "0")]
    [TestCase("int remaining=n; while(remaining>2) { remaining-=3; }", "ceil(max(0, (max(0, n) - 2)) / 3)", "0", "0")]
    [TestCase("int remaining=7; while(remaining>1) { remaining-=2; }", "3", "0", "0")]
    [TestCase("checked { int i=0; while(i<2147483647) { i+=2; } }", "1073741824", "0", "0")]
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

    [TestCase("int left=2; while(left>0||left>0) { if(left>0) left--; else left--; }", "UnsupportedPhaseGuard")]
    [TestCase("int right=2; while(n>0||right>0) { if(n>0) n--; else right--; }", "UnsupportedPhaseGuard")]
    [TestCase("int left=2,right=3; while(left>1||right>0) { if(left>1) left--; else right--; }", "UnsupportedPhaseGuard")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { if(left>0) left--; }", "UnsupportedPhaseSelection")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { if(right>0) right--; else left--; }", "UnsupportedPhaseSelection")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { int other=0; if(left>0) left--; else right--; }", "UnsupportedPhaseSelection")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { if(left>0) left++; else right--; }", "UnprovedLoopProgress")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { if(left>0) left--; else left--; }", "UnprovedLoopProgress")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { if(left>0) { right++; left--; } else right--; }", "UnstableInputOrCounter")]
    [TestCase("int left=2,right=3; left++; while(left>0||right>0) { if(left>0) left--; else right--; }", "UnprovedLoopProgress")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { if(left>0) left/=1; else right--; }", "UnprovedLoopProgress")]
    [TestCase("int left=2,right=3; while((left>0)|(right>0)) { if(left>0) left--; else right--; }", "UnsupportedLoop")]
    [TestCase("int left=2,right=3,third=4; while(left>0||right>0||third>0) { if(left>0) left--; else right--; }", "UnsupportedPhaseGuard")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { if(left>0) { continue; left--; } else right--; }", "UnsupportedOperation:Branch")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { if(left>0) left-=unchecked((int)2147483648L); else right--; }", "UnprovedLoopProgress")]
    [TestCase("int i=-1; while(i<n) { i++; }", "UnsupportedLoopStart")]
    [TestCase("int i=n; while(i<10) { i++; }", "UnsupportedLoopStart")]
    [TestCase("int i=0; while(i<n) { i+=2; }", "LoopCounterMayOverflow")]
    [TestCase("int i=1; while(i<n) { i*=2; }", "LoopCounterMayOverflow")]
    [TestCase("int i=1; while(i<10) { i*=1; }", "UnprovedLoopProgress")]
    [TestCase("int i=0; i++; while(i<n) { i++; }", "UnprovedLoopProgress")]
    [TestCase("int i=0; while(i<n) { i=1; i++; }", "UnstableInputOrCounter")]
    [TestCase("int i=0; while(i<n) { if(n>0) continue; i++; }", "UnsupportedOperation:Branch")]
    [TestCase("int i=0; while(i<n) { i--; }", "UnprovedLoopProgress")]
    [TestCase("int remaining=n; while(remaining>2) { remaining/=2; }", "UnprovedLoopProgress")]
    [TestCase("int i=0; while(i<n) { i++; } while(i<n) { i++; }", "UnprovedLoopProgress")]
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
    [TestCase("int remaining=n; while (remaining>0) {}", "UnprovedLoopProgress")]
    [TestCase("int remaining=n; while (remaining>0) { remaining+=1; }", "UnprovedLoopProgress")]
    [TestCase("int remaining=n; int other=1; while (remaining>0) { other--; }", "UnprovedLoopProgress")]
    [TestCase("for (int i=0; i<n; --i) {}", "UnprovedLoopProgress")]
    [TestCase("for (int i=0; i<n; i+=m) {}", "UnsupportedLoop")]
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
                var whileUpResult = Analyze($"int counter=2; while(counter<{end}) {{ counter+={stride}; }}");
                var whileDownResult = Analyze($"int counter={end}; while(counter>1) {{ counter-={stride}; }}");
                Assert.That(whileUpResult.Proven, Is.True, whileUpResult.UnknownReason);
                Assert.That(whileDownResult.Proven, Is.True, whileDownResult.UnknownReason);
                Assert.That(whileUpResult.Bounds!.LoopIterations.Constant, Is.EqualTo(new System.Numerics.BigInteger(up)));
                Assert.That(whileDownResult.Bounds!.LoopIterations.Constant, Is.EqualTo(new System.Numerics.BigInteger(down)));
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
                var whileGrowResult = Analyze($"int counter=1; while(counter<{end}) {{ counter*={stride}; }}");
                Assert.That(whileGrowResult.Proven, Is.True, whileGrowResult.UnknownReason);
                Assert.That(whileGrowResult.Bounds!.LoopIterations.Constant, Is.EqualTo(new System.Numerics.BigInteger(grow)));
            }
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void PhasedBoundsMatchIndependentCounterExecutions(bool divide)
    {
        int checkedExecutions = 0;
        for (int leftStart = -2; leftStart <= 7; leftStart++)
        {
            for (int rightStart = -2; rightStart <= 7; rightStart++)
            {
                int left = leftStart, right = rightStart, entries = 0, arrays = 0, elements = 0;
                while (left > 0 || right > 0)
                {
                    Assert.That(++entries, Is.LessThan(100));
                    arrays++;
                    if (left > 0)
                    {
                        elements += 2;
                        left = divide ? left / 2 : left - 2;
                    }
                    else
                    {
                        elements += 3;
                        right = divide ? right / 3 : right - 3;
                    }
                }
                var leftUpdate = divide ? "left/=2;" : "left-=2;";
                var rightUpdate = divide ? "right/=3;" : "right-=3;";
                var result = Analyze($"int left={leftStart},right={rightStart}; while(left>0||right>0) {{ if(left>0) {{ var a=new int[2]; {leftUpdate} }} else {{ var b=new int[3]; {rightUpdate} }} }}");
                Assert.That(result.Proven, Is.True, result.UnknownReason);
                Assert.That(result.Bounds!.LoopIterations.Constant, Is.EqualTo(new System.Numerics.BigInteger(entries)));
                Assert.That(result.Bounds.Arrays.Constant, Is.EqualTo(new System.Numerics.BigInteger(arrays)));
                Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(new System.Numerics.BigInteger(elements)));
                checkedExecutions++;
            }
        }
        Assert.That(checkedExecutions, Is.EqualTo(100));
    }
}
