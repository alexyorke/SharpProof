using System.Reflection;
using NUnit.Framework;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class SmtSyntacticClassifierTests
    {
        [Test]
        public void SyntacticFactSetCopy_PreservesBooleanFactInferenceDepth()
        {
            var factSetType = typeof(SmtAnalysisService).Assembly
                .GetType("SharpProof.Symbolic.Smt.SmtSyntacticClassifier+SyntacticFactSet", throwOnError: true)!;
            var defaultConstructor = factSetType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null,
                Type.EmptyTypes,
                modifiers: null)!;
            var copyConstructor = factSetType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { factSetType },
                modifiers: null)!;
            var depthField = factSetType.GetField(
                "_booleanFactInferenceDepth",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            var source = defaultConstructor.Invoke(Array.Empty<object>());
            depthField.SetValue(source, 7);

            var copy = copyConstructor.Invoke(new[] { source });

            Assert.That(depthField.GetValue(copy), Is.EqualTo(7));
        }
    }
}
