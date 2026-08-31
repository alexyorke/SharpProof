namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class RefConditionalArgumentHavocTests
{
    [Test]
    public void RefConditionalArgumentsInvalidateEveryPossibleStorage()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int Value;
            }

            public static class Subject {
                private static void Clear(ref int value) {
                    value = 0;
                }

                private static void Clear(ref Box? value) {
                    value = null;
                }

                public static int Divide(bool choose) {
                    var left = 1;
                    var right = 2;
                    Clear(ref (choose ? ref left : ref right));
                    return 1 / (choose ? left : right);
                }

                public static int Dereference(bool choose) {
                    Box? left = new Box();
                    Box? right = new Box();
                    Clear(ref (choose ? ref left : ref right));
                    return (choose ? left : right)!.Value;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var divide = session.Analyze(EffectTestHost.RequireMethod(
            compilation,
            "Subject",
            "Divide"));
        var dereference = session.Analyze(EffectTestHost.RequireMethod(
            compilation,
            "Subject",
            "Dereference"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                divide.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.DivideByZeroException"));
            Assert.That(
                dereference.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.NullReferenceException"));
            Assert.That(
                divide.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                dereference.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
