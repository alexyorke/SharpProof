namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ExceptionHandlerReachabilityTests
{
    [Test]
    public void ClosedVirtualDispatchUsesTheExactExceptionSet()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public class Base {
                public virtual void Call() { }
            }

            public class MethodSealed : Base {
                public sealed override void Call() { }
            }

            public sealed class TypeSealed : Base {
                public override void Call() { }
            }

            public static class Sample {
                public static void SealedMethod(MethodSealed value) {
                    try {
                        value.Call();
                    }
                    catch (ApplicationException) {
                    }
                }

                public static void SealedType(TypeSealed value) {
                    try {
                        value.Call();
                    }
                    catch (ApplicationException) {
                    }
                }

                public static void OpenDispatch(Base value) {
                    try {
                        value.Call();
                    }
                    catch (ApplicationException) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(IsCatchReachable("SealedMethod"), Is.False);
            Assert.That(IsCatchReachable("SealedType"), Is.False);
            Assert.That(IsCatchReachable("OpenDispatch"), Is.True);
        }

        bool IsCatchReachable(string methodName)
        {
            var method = EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                methodName);
            var declaration = (MethodDeclarationSyntax)method
                .DeclaringSyntaxReferences.Single().GetSyntax();
            var catchClause = declaration.DescendantNodes()
                .OfType<CatchClauseSyntax>()
                .Single();
            var reachability = new ExceptionHandlerReachability(
                compilation: compilation,
                caller: method,
                abstractFlow: null,
                canCompleteNormally: static _ => true,
                canMethodCompleteNormally: static _ => true,
                canCompoundValueComplete: static _ => true,
                canIncrementValueComplete: static _ => true,
                canWithCloneComplete: static _ => true,
                conversionEffects: new ConversionEffectClassifier(
                    session,
                    abstractFlow: null),
                getReachableListPatternMembers: static _ => [],
                apiSpecs: session.ApiSpecs,
                knownSymbols: session.KnownSymbols,
                isKnownNonThrowing: static _ => false);
            return reachability.IsReachable(catchClause, inFilter: false);
        }
    }

    [Test]
    public void OnlyAuthenticatedRuntimeRefLikeAccessorsAreNonthrowing()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using System;

            namespace External;

            public readonly ref struct ThrowingView {
                public int Value =>
                    throw new InvalidOperationException();
            }
            """,
            "ExternalRefLikeAccessors");
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using External;

            public static class Sample {
                public static void ReadExternal(ThrowingView value) {
                    try {
                        _ = value.Value;
                    }
                    catch (InvalidOperationException) {
                    }
                }

                public static void ReadRuntime(Span<int> value) {
                    try {
                        _ = value.Length;
                    }
                    catch (Exception) {
                    }
                }
            }
            """,
            externalReference);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(IsCatchReachable("ReadExternal"), Is.True);
            Assert.That(IsCatchReachable("ReadRuntime"), Is.False);
        }

        bool IsCatchReachable(string methodName)
        {
            var method = EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                methodName);
            var declaration = (MethodDeclarationSyntax)method
                .DeclaringSyntaxReferences.Single().GetSyntax();
            var catchClause = declaration.DescendantNodes()
                .OfType<CatchClauseSyntax>()
                .Single();
            var reachability = new ExceptionHandlerReachability(
                compilation: compilation,
                caller: method,
                abstractFlow: null,
                canCompleteNormally: static _ => true,
                canMethodCompleteNormally: static _ => true,
                canCompoundValueComplete: static _ => true,
                canIncrementValueComplete: static _ => true,
                canWithCloneComplete: static _ => true,
                conversionEffects: new ConversionEffectClassifier(
                    session,
                    abstractFlow: null),
                getReachableListPatternMembers: static _ => [],
                apiSpecs: session.ApiSpecs,
                knownSymbols: session.KnownSymbols,
                isKnownNonThrowing: static _ => false);
            return reachability.IsReachable(catchClause, inFilter: false);
        }
    }
}
