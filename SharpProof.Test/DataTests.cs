using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class DataTests
{
    [Test]
    public async Task DataColumnConstructor_Diagnostic()
    {
        var test = @"
using System.Data;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DataColumn {|SP0002:TestMethod|}()
    {
        return new DataColumn(""Id"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DataRelationConstructor_Diagnostic()
    {
        var test = @"
using System.Data;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DataRelation {|SP0002:TestMethod|}(DataColumn parentColumn, DataColumn childColumn)
    {
        return new DataRelation(""Rel"", parentColumn, childColumn);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}