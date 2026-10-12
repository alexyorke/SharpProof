using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzUnsupportedRootControls
{
    [TestCase(true)]
    [TestCase(false)]
    public void GeneratedSourceRejectsUnsupportedRootResultTypes(bool sequence)
    {
        var expression = sequence
            ? GeneratedCSharpExpression.Values()
            : GeneratedCSharpExpression.Reference();
        var generated = new GeneratedCSharpCase(expression, 0, 0, false);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = generated.Source;
        });
        Assert.That(exception!.ParamName, Is.EqualTo("type"));
    }
}
