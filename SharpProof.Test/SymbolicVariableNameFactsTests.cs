using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicVariableNameFactsTests {
    [TestCase("value", "value", true)]
    [TestCase("value.Field", "value", true)]
    [TestCase("value[0]", "value", true)]
    [TestCase("value2", "value", false)]
    [TestCase("other.value", "value", false)]
    public void MatchesVariableOrMemberName_RequiresAPathBoundary(string candidate, string variableName, bool expected)
        => Assert.That(SymbolicFactFactory.MatchesVariableOrMemberName(candidate, variableName), Is.EqualTo(expected));
}
