using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class VirtualHierarchyPreconditionRegressionTests
{
    [Test]
    public async Task ExactRuntimeTargetsRetainOverrideAndInterfacePreconditions()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public class BaseService {
                public virtual void Run(int value) { }
            }

            public sealed class DerivedService : BaseService {
                public override void Run(int value) {
                    Contract.Requires(value > 0);
                }
            }

            public interface IService {
                void Run(int value);
            }

            public sealed class Service : IService {
                public void Run(int value) {
                    Contract.Requires(value > 0);
                }
            }

            public interface IExplicitService {
                void Run(int value);
            }

            public sealed class ExplicitService : IExplicitService {
                void IExplicitService.Run(int value) {
                    Contract.Requires(value > 0);
                }
            }

            public static class Subject {
                public static void CallDirect() {
                    new DerivedService().Run(-1);
                }

                public static void CallVirtual() {
                    ((BaseService)new DerivedService()).Run(-2);
                }

                public static void CallInterface() {
                    ((IService)new Service()).Run(-3);
                }

                public static void CallExplicitInterface() {
                    ((IExplicitService)new ExplicitService()).Run(-4);
                }
            }
            """,
            "contracts",
            []);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027", 4);
    }
}
