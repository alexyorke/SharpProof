using System.Reflection;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
namespace SharpProof.Test;
[TestFixture]
public sealed class ProofCoreNullableContractTests {
    [TestCase("TryParseExpression", 1, 0)]
    [TestCase("TryParseConcat", 1, 0)]
    [TestCase("TryParseConcat", 2, 0)]
    [TestCase("TryConstrainSplitWithWordBoundary", 4, 3)]
    [TestCase("TryParseRepeat", 1, 0)]
    [TestCase("TryParseAtom", 1, 0)]
    [TestCase("TryParseEscapedAtom", 1, 0)]
    [TestCase("TryParseCharClass", 1, 0)]
    [TestCase("TryParseSimpleCharClass", 1, 0)]
    [TestCase("TryParseWholeCharacterClassWithDotNet", 1, 0)]
    [TestCase("TryCreateCharacterRangesRegex", 2, 1)]
    [TestCase("TryCreateCharacterRangesRegex", 3, 2)]
    public void RegexTryHelper_FailureOutputHasNullableFlowContract(
        string methodName,
        int parameterCount,
        int outputIndex) {
        var proofCoreAssembly = typeof(SmtFormula).Assembly;
        var translatorType = proofCoreAssembly.GetType(
            "SharpProof.ProofCore.Smt.Z3RegexTranslator",
            throwOnError: true)!;
        var method = translatorType
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
                candidate.Name == methodName &&
                candidate.GetParameters().Length == parameterCount);
        var parameter = method.GetParameters()[outputIndex];
        var notNullWhen = parameter.CustomAttributes.SingleOrDefault(attribute =>
            attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");
        Assert.Multiple(() => {
            Assert.That(parameter.ParameterType.IsByRef, Is.True);
            Assert.That(parameter.ParameterType.GetElementType()?.FullName, Is.EqualTo("Microsoft.Z3.ReExpr"));
            Assert.That(new NullabilityInfoContext().Create(parameter).WriteState, Is.EqualTo(NullabilityState.Nullable));
            Assert.That(notNullWhen?.ConstructorArguments.Single().Value, Is.True);
        });
    }
    [Test]
    public void BoundedCacheTryGetValue_FailureOutputHasMaybeNullContract() {
        var proofCoreAssembly = typeof(SmtFormula).Assembly;
        var cacheType = proofCoreAssembly.GetType(
            "SharpProof.ProofCore.Collections.BoundedConcurrentCache`2",
            throwOnError: true)!;
        var parameter = cacheType.GetMethod(
            "TryGetValue",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetParameters()[1];
        var maybeNullWhen = parameter.CustomAttributes.SingleOrDefault(attribute =>
            attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute");
        Assert.That(maybeNullWhen?.ConstructorArguments.Single().Value, Is.False);
    }
    [Test]
    public void RegexTranslationSuccess_DeclaresRegexNonNull() {
        var proofCoreAssembly = typeof(SmtFormula).Assembly;
        var resultType = proofCoreAssembly.GetType(
            "SharpProof.ProofCore.Smt.Z3RegexTranslationResult",
            throwOnError: true)!;
        var success = resultType.GetProperty("Success")!;
        var memberNotNullWhen = success.CustomAttributes.SingleOrDefault(attribute =>
            attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.MemberNotNullWhenAttribute");
        var members = memberNotNullWhen?.ConstructorArguments[1].Value switch {
            string member => [member],
            IEnumerable<CustomAttributeTypedArgument> targets =>
                targets.Select(static target => target.Value),
            _ => []
        };
        Assert.Multiple(() => {
            Assert.That(memberNotNullWhen?.ConstructorArguments[0].Value, Is.True);
            Assert.That(members, Does.Contain("Regex"));
        });
    }
    [TestCase("TryGetLocal", 1)]
    [TestCase("TryGetShared", 2)]
    public void SmtProofCacheTryHelper_FailureOutputHasNullableFlowContract(
        string methodName,
        int outputIndex) {
        var cacheType = typeof(SharpProofAnalysisSession).Assembly.GetType(
            "SharpProof.Symbolic.Smt.SmtProofResultCache",
            throwOnError: true)!;
        var parameter = cacheType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public)!
            .GetParameters()[outputIndex];
        var notNullWhen = parameter.CustomAttributes.SingleOrDefault(attribute =>
            attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");
        Assert.Multiple(() => {
            Assert.That(new NullabilityInfoContext().Create(parameter).WriteState, Is.EqualTo(NullabilityState.Nullable));
            Assert.That(notNullWhen?.ConstructorArguments.Single().Value, Is.True);
        });
    }
}
