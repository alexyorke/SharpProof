namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class TransitiveRefAliasOwnershipTests
{
    [Test]
    public void HelperRebindingCannotHideLaterPointeeWrites()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public ref struct RefAlias {
                private static int s_cell;
                public ref int Cell;

                public void BindStaticThroughHelpers() =>
                    BindStaticMiddle();

                private void BindStaticMiddle() => BindStaticCore();

                private void BindStaticCore() {
                    Cell = ref s_cell;
                }

                public void BindCallerThroughHelpers(int[] cells) =>
                    BindCallerMiddle(cells);

                private void BindCallerMiddle(int[] cells) =>
                    BindCallerCore(cells);

                private void BindCallerCore(int[] cells) {
                    Cell = ref cells[0];
                }

                public void Set() => Cell = 1;
            }

            public static class Sample {
                public static void MutateStatic() {
                    RefAlias alias = default;
                    alias.BindStaticThroughHelpers();
                    alias.Set();
                }

                public static void MutateCaller(int[] cells) {
                    RefAlias alias = default;
                    alias.BindCallerThroughHelpers(cells);
                    alias.Set();
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var staticMutation = Analyze("MutateStatic");
        var callerMutation = Analyze("MutateCaller");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                staticMutation.Writes.Contains(EffectRegionId.Static()) ||
                staticMutation.Writes.IsUnknown,
                Is.True,
                "the helper chain can rebind to static storage");
            Assert.That(
                callerMutation.Writes.Contains(EffectRegionId.Parameter(0)) ||
                callerMutation.Writes.IsUnknown,
                Is.True,
                "the helper chain can rebind to caller-owned storage");
        }

        EffectSummary Analyze(string methodName)
        {
            return session.Analyze(EffectTestHost.RequireMethod(
                    compilation,
                    "Sample",
                    methodName))
                .Summary;
        }
    }
}
