using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ReducedExtensionEffectPreconditionTests
{
    [Test]
    public async Task ReceiverAndDeclaredArgumentPreconditionsUseTheirOwnActuals()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Extensions
            {
                public static void Restricted(this int receiver, int value)
                {
                    Contract.Requires(receiver > 0);
                    Contract.Requires(value > 0);
                }
            }

            public static class Subject
            {
                [DoesNotThrow]
                public static void Satisfied() => 5.Restricted(7);

                [DoesNotThrow]
                public static void ReceiverViolated() => 0.Restricted(7);

                [DoesNotThrow]
                public static void ArgumentViolated() => 5.Restricted(-1);
            }
            """,
            "effects",
            ["SP0047"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047", "SP0047"]));
        Assert.That(
            diagnostics.Select(static diagnostic =>
                diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Has.All.Contain("CallPreconditionNotProven"));
    }
}
