using System.Threading;
using NUnit.Framework;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class SymbolicQueryTargetTests
    {
        [Test]
        public void QueryOptions_TrimsImpliedConditions()
        {
            var options = new SymbolicQueryOptions(impliedConditions: new[] { " value > 0 ", "\t", "\nother < 10\r\n" });

            Assert.That(options.ImpliedConditions, Is.EqualTo(new[] { "value > 0", "other < 10" }));
        }

        [Test]
        public void QueryOptions_NullReferenceEntry_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => new SymbolicQueryOptions(references: new MetadataReference[] { null! }));

            Assert.That(exception!.ParamName, Is.EqualTo("references"));
        }

        [Test]
        public void SourceCompilation_NullReferenceEntry_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => SymbolicSourceCompilation.Create(
                    "public sealed class Sample { }",
                    "Sample.cs",
                    "Sample.cs",
                    "Sample",
                    new MetadataReference[] { null! },
                    CancellationToken.None));

            Assert.That(exception!.ParamName, Is.EqualTo("references"));
        }

        [Test]
        public void LineSpan_EndLineBeforeStartLine_Throws()
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => SymbolicQueryTarget.LineSpan(3, 1, 2, 1));

            Assert.That(exception!.ParamName, Is.EqualTo("endLine"));
        }

        [Test]
        public void LineSpan_EndColumnBeforeStartColumnOnSameLine_Throws()
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => SymbolicQueryTarget.LineSpan(3, 5, 3, 4));

            Assert.That(exception!.ParamName, Is.EqualTo("endColumn"));
        }

        [Test]
        public void LineSpan_SameLocation_IsAllowed()
        {
            var target = SymbolicQueryTarget.LineSpan(3, 5, 3, 5);

            Assert.That(target.Kind, Is.EqualTo(SymbolicQueryTargetKind.LineSpan));
            Assert.That(target.StartLine, Is.EqualTo(3));
            Assert.That(target.StartColumn, Is.EqualTo(5));
            Assert.That(target.EndLine, Is.EqualTo(3));
            Assert.That(target.EndColumn, Is.EqualTo(5));
        }
    }
}
