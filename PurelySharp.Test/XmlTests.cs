using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class XmlTests
    {
        [Test]
        public async Task XmlDocumentLoadXml_Diagnostic()
        {
            var test = @"
using System.Xml;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public XmlDocument {|PS0002:TestMethod|}(XmlDocument document)
    {
        document.LoadXml(""<root />"");
        return document;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XmlDocumentSelectSingleNode_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Xml;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public XmlNode? {|PS0002:TestMethod|}(XmlDocument document)
    {
        return document.SelectSingleNode(""/root"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XmlSchemaSetCompile_Diagnostic()
        {
            var test = @"
using System.Xml.Schema;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public XmlSchemaSet {|PS0002:TestMethod|}(XmlSchemaSet schemas)
    {
        schemas.Compile();
        return schemas;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XDocumentParse_Diagnostic()
        {
            var test = @"
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public XDocument {|PS0002:TestMethod|}()
    {
        return XDocument.Parse(""<root />"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XElementLoad_Diagnostic()
        {
            var test = @"
using System.IO;
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public XElement {|PS0002:TestMethod|}(Stream stream)
    {
        return XElement.Load(stream);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XElementSave_Diagnostic()
        {
            var test = @"
using System.IO;
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(XElement element, Stream stream)
    {
        element.Save(stream);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XElementAdd_Diagnostic()
        {
            var test = @"
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(XElement element)
    {
        element.Add(new XAttribute(""id"", ""1""));
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XNodeRemove_Diagnostic()
        {
            var test = @"
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(XNode node)
    {
        node.Remove();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XElementValue_Diagnostic()
        {
            var test = @"
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(XElement element)
    {
        return element.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XAttributeValue_Diagnostic()
        {
            var test = @"
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(XAttribute attribute)
    {
        return attribute.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XElementAttribute_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public XAttribute? {|PS0002:TestMethod|}(XElement element)
    {
        return element.Attribute(""id"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XElementElements_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<XElement> {|PS0002:TestMethod|}(XElement element)
    {
        return element.Elements();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task XElementDescendants_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<XElement> {|PS0002:TestMethod|}(XElement element)
    {
        return element.Descendants();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
