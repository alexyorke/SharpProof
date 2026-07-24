using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Symbolic;
namespace SharpProof.Test;
[TestFixture]
public sealed class AnalyzerQueryBoundaryTests {
    [Test]
    public void NullQueryResult_IsInternalFailureRatherThanSuccessfulOutcome() {
        var outcome = AnalyzerSymbolicQueryBoundary.TryExecute<string>(() => null!);
        Assert.Multiple(() => {
            Assert.That(outcome.IsSuccess, Is.False);
            Assert.That(outcome.Value, Is.Null);
            Assert.That(outcome.Error?.Code, Is.EqualTo(SymbolicErrorCodes.InternalFailure));
            Assert.That(outcome.Error?.Category, Is.EqualTo(SharpProofErrorCategory.Internal));
        });
    }
}
