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

        Action source = () =>
        {
            _ = generated.Source;
        };
        var exception = Assert.Throws<ArgumentOutOfRangeException>(source);
        Assert.That(exception!.ParamName, Is.EqualTo("type"));
    }
}
