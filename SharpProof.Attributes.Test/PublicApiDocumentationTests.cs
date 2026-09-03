using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Attributes.Test;

[TestFixture]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates test fixtures through reflection.")]
internal sealed class PublicApiDocumentationTests
{
    [Test]
    public void IntelliSenseFileExactlyDocumentsTheSupportedPublicApi()
    {
        var documented = XDocument.Load(GetDocumentationPath())
            .Descendants("member")
            .ToDictionary(
                static element =>
                    (string?)element.Attribute("name") ??
                    throw new InvalidOperationException(
                        "A documentation member has no name."),
                StringComparer.Ordinal);
        var expected = GetPublicApiMembers();

        Assert.That(
            documented.Keys,
            Is.EquivalentTo(expected.Keys),
            "The IntelliSense XML member set must exactly match the supported " +
            "SharpProof.Attributes public surface.");

        foreach (var (id, member) in expected)
        {
            var element = documented[id];
            Assert.That(
                element.Element("summary")?.Value,
                Is.Not.Null.And.Not.Empty,
                id + " must have a nonempty summary.");
            foreach (var parameter in GetParameters(member))
            {
                AssertNamedElement(element, "param", parameter.Name!, id);
            }
            if (member is MethodInfo method)
            {
                foreach (var argument in method.GetGenericArguments())
                {
                    AssertNamedElement(element, "typeparam", argument.Name, id);
                }

                if (method.ReturnType != typeof(void))
                {
                    Assert.That(
                        element.Element("returns")?.Value,
                        Is.Not.Null.And.Not.Empty,
                        id + " must document its return value.");
                }
            }
            if (member is PropertyInfo)
            {
                Assert.That(
                    element.Element("value")?.Value,
                    Is.Not.Null.And.Not.Empty,
                    id + " must document its property value.");
            }
        }
    }

    private static SortedDictionary<string, MemberInfo> GetPublicApiMembers()
    {
        var result = new SortedDictionary<string, MemberInfo>(
            StringComparer.Ordinal);
        foreach (var type in typeof(Contract).Assembly.GetExportedTypes())
        {
            result.Add("T:" + GetTypeName(type), type);
            foreach (var constructor in type.GetConstructors(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.DeclaredOnly))
            {
                result.Add(GetMethodId(constructor), constructor);
            }
            foreach (var method in type.GetMethods(
                         BindingFlags.Public |
                         BindingFlags.Static |
                         BindingFlags.Instance |
                         BindingFlags.DeclaredOnly))
            {
                if (!method.IsSpecialName)
                {
                    result.Add(GetMethodId(method), method);
                }
            }
            foreach (var property in type.GetProperties(
                         BindingFlags.Public |
                         BindingFlags.Static |
                         BindingFlags.Instance |
                         BindingFlags.DeclaredOnly))
            {
                result.Add(
                    "P:" + GetTypeName(type) + "." + property.Name,
                    property);
            }
            foreach (var field in type.GetFields(
                         BindingFlags.Public |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                result.Add(
                    "F:" + GetTypeName(type) + "." + field.Name,
                    field);
            }
        }
        return result;
    }

    private static string GetMethodId(MethodBase method)
    {
        var declaringType = method.DeclaringType ??
            throw new InvalidOperationException(
                "A public method has no declaring type.");
        var name = method.IsConstructor ? "#ctor" : method.Name;
        if (method.IsGenericMethodDefinition)
        {
            name += "``" + method.GetGenericArguments().Length;
        }

        var parameters = method.GetParameters();
        var parameterList = parameters.Length == 0
            ? string.Empty
            : "(" + string.Join(
                ",",
                parameters.Select(static parameter =>
                    GetParameterTypeName(parameter.ParameterType))) + ")";
        return "M:" + GetTypeName(declaringType) + "." + name + parameterList;
    }

    private static string GetTypeName(Type type)
    {
        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static string GetParameterTypeName(Type type)
    {
        return type.IsArray
            ? GetParameterTypeName(type.GetElementType()!) + "[]"
            : type.IsGenericParameter
                ? (type.DeclaringMethod == null ? "`" : "``") +
                    type.GenericParameterPosition
                : GetTypeName(type);
    }

    private static ParameterInfo[] GetParameters(MemberInfo member)
    {
        return member is MethodBase method ? method.GetParameters() : [];
    }

    private static void AssertNamedElement(
        XElement member,
        string elementName,
        string name,
        string id)
    {
        Assert.That(
            member.Elements(elementName).Any(element =>
                string.Equals(
                    (string?)element.Attribute("name"),
                    name,
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(element.Value)),
            Is.True,
            id + " must document " + elementName + " '" + name + "'.");
    }

    private static string GetDocumentationPath()
    {
        var candidate = Path.Combine(
            AppContext.BaseDirectory,
            "SharpProof.Attributes.xml");
        if (File.Exists(candidate))
        {
            return candidate;
        }
        throw new InvalidOperationException(
            "Could not find generated SharpProof.Attributes.xml.");
    }
}
