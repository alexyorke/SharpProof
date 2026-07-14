using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class XmlTests
{
    private static IEnumerable<TestCaseData> ImpureCalls()
    {
        yield return Case("XmlDocumentLoadXml", "using System.Xml;", "XmlDocument", "XmlDocument document",
            "document.LoadXml(\"<root />\");\n        return document;");
        yield return Case("XmlDocumentSelectSingleNode", "#nullable enable\nusing System.Xml;", "XmlNode?",
            "XmlDocument document", "return document.SelectSingleNode(\"/root\");");
        yield return Case("XmlSchemaSetCompile", "using System.Xml.Schema;", "XmlSchemaSet", "XmlSchemaSet schemas",
            "schemas.Compile();\n        return schemas;");
        yield return Case("XDocumentParse", "using System.Xml.Linq;", "XDocument", "",
            "return XDocument.Parse(\"<root />\");");
        yield return Case("XElementLoad", "using System.IO;\nusing System.Xml.Linq;", "XElement", "Stream stream",
            "return XElement.Load(stream);");
        yield return Case("XElementSave", "using System.IO;\nusing System.Xml.Linq;", "void",
            "XElement element, Stream stream", "element.Save(stream);");
        yield return Case("XElementAdd", "using System.Xml.Linq;", "void", "XElement element",
            "element.Add(new XAttribute(\"id\", \"1\"));");
        yield return Case("XNodeRemove", "using System.Xml.Linq;", "void", "XNode node", "node.Remove();");
        yield return Case("XElementValue", "using System.Xml.Linq;", "string", "XElement element",
            "return element.Value;");
        yield return Case("XAttributeValue", "using System.Xml.Linq;", "string", "XAttribute attribute",
            "return attribute.Value;");
        yield return Case("XElementAttribute", "#nullable enable\nusing System.Xml.Linq;", "XAttribute?",
            "XElement element", "return element.Attribute(\"id\");");
        yield return Case("XElementElements", "using System.Collections.Generic;\nusing System.Xml.Linq;",
            "IEnumerable<XElement>", "XElement element", "return element.Elements();");
        yield return Case("XElementDescendants", "using System.Collections.Generic;\nusing System.Xml.Linq;",
            "IEnumerable<XElement>", "XElement element", "return element.Descendants();");
    }

    private static TestCaseData Case(
        string name,
        string imports,
        string returnType,
        string parameters,
        string body)
    {
        return new TestCaseData(imports, returnType, parameters, body).SetName(name + "_Diagnostic");
    }

    [TestCaseSource(nameof(ImpureCalls))]
    public async Task XmlCall_Diagnostic(string imports, string returnType, string parameters, string body)
    {
        var test = $@"
{imports}
using SharpProof.Attributes;

public class TestClass
{{
    [EnforcePure]
    public {returnType} {{|SP0002:TestMethod|}}({parameters})
    {{
        {body}
    }}
}}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
