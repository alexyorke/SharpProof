using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerSpecificationPackProviderTests
{
    private static readonly string[] KindProperty = ["kind"];
    private static readonly MethodInfo s_parseTerm =
        typeof(CompilerSpecificationPackProvider)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(static method => method.Name == "ParseTerm");
    private static readonly MethodInfo s_instantiate =
        typeof(CompilerSpecificationPackProvider)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(static method => method.Name == "Instantiate");
    private static readonly MethodInfo s_parseMethod =
        typeof(CompilerSpecificationPackProvider)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(static method => method.Name == "ParseMethod");
    private static readonly MethodInfo s_parsePack =
        typeof(CompilerSpecificationPackProvider)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(static method => method.Name == "ParsePack");
    private static readonly MethodInfo s_typeId =
        typeof(CompilerSpecificationPackProvider)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(static method => method.Name == "TypeId");
    private static readonly MethodInfo s_requireObject =
        typeof(CompilerSpecificationPackProvider)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(static method => method.Name == "RequireObject");

    [Test]
    public void ScalarTermKindsAndOperatorsParseAndInstantiateExactly()
    {
        var boolean =
            """{"kind":"boolean","type":"Boolean","value":true}""";
        var integer =
            """{"kind":"integer","type":"Integer","value":7}""";
        var parameter =
            """{"kind":"parameter","type":"Integer","ordinal":0}""";
        var terms = new List<(string Json, string RuntimeType)>
        {
            (boolean, "BooleanTerm"),
            (integer, "IntegerTerm"),
            (parameter, "ParameterTerm"),
            ($$"""
              {"kind":"unary","type":"Boolean","operator":"Not","operand":{{boolean}}}
              """, "UnaryTerm"),
            ($$"""
              {"kind":"unary","type":"Integer","operator":"Negate","operand":{{integer}}}
              """, "UnaryTerm"),
            ($$"""
              {"kind":"conditional","type":"Integer","condition":{{boolean}},"whenTrue":{{integer}},"whenFalse":{{parameter}}}
              """, "ConditionalTerm")
        };
        var arithmetic = new[]
        {
            "Add",
            "Subtract",
            "Multiply",
            "Divide",
            "Remainder"
        };
        var logical = new[] { "AndAlso", "OrElse" };
        var comparisons = new[]
        {
            "Equal",
            "NotEqual",
            "LessThan",
            "LessThanOrEqual",
            "GreaterThan",
            "GreaterThanOrEqual"
        };
        terms.AddRange(arithmetic.Select(operation => (
            Binary(operation, "Integer", integer, integer),
            "BinaryTerm")));
        terms.AddRange(logical.Select(operation => (
            Binary(operation, "Boolean", boolean, boolean),
            "BinaryTerm")));
        terms.AddRange(comparisons.Select(operation => (
            Binary(operation, "Boolean", integer, integer),
            "BinaryTerm")));

        using (Assert.EnterMultipleScope())
        {
            foreach (var (json, runtimeType) in terms)
            {
                var parsed = ParseTerm(json);
                var instantiated = Instantiate(parsed);
                Assert.That(parsed.GetType().Name, Is.EqualTo(runtimeType), json);
                Assert.That(instantiated, Is.Not.Null, json);
            }
        }
    }

    [Test]
    public void MalformedSpecificationPackTermsFailClosed()
    {
        var invalid = new (string Json, int Depth, string Message)[]
        {
            (
                """{"kind":"boolean","type":"Boolean","value":true}""",
                65,
                "too deep"),
            (
                """{"kind":"boolean","type":"Integer","value":true}""",
                0,
                "Boolean literal is invalid"),
            (
                """{"kind":"integer","type":"Boolean","value":1}""",
                0,
                "integer literal is invalid"),
            (
                """{"kind":"unary","type":"Boolean","operator":"Unknown","operand":{"kind":"boolean","type":"Boolean","value":true}}""",
                0,
                "unary operator is unsupported"),
            (
                """{"kind":"binary","type":"Integer","operator":"Unknown","left":{"kind":"integer","type":"Integer","value":1},"right":{"kind":"integer","type":"Integer","value":2}}""",
                0,
                "binary operator is unsupported"),
            (
                """{"kind":"unknown","type":"Integer"}""",
                0,
                "term kind is unsupported"),
            (
                """{"kind":"integer","type":1,"value":1}""",
                0,
                "must be a scalar type name"),
            (
                """{"kind":1,"type":"Integer","value":1}""",
                0,
                "kind must be a string with content"),
            (
                """{"kind":"integer","type":"String","value":1}""",
                0,
                "unsupported scalar type"),
            (
                """{"kind":"integer","type":"Integer","value":1,"extra":true}""",
                0,
                "invalid property set"),
            (
                """{"type":"Integer","value":1}""",
                0,
                "missing 'kind'")
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var (json, depth, message) in invalid)
            {
                var error = Assert.Throws<InvalidDataException>((Action)(() =>
                    ParseTerm(json, depth)));
                Assert.That(error!.Message, Does.Contain(message), json);
            }
        }
    }

    [Test]
    public void MalformedSpecificationPackMethodEvidenceFailsClosed()
    {
        var validMethod = MethodJson(
            "M:X.M",
            """[{"name":"A","publicKeyToken":""}]""",
            "[]",
            "Integer",
            """{"kind":"integer","type":"Integer","value":1}""");
        var invalid = new (string Json, string Message)[]
        {
            (
                MethodJson(
                    "X.M",
                    """[{"name":"A","publicKeyToken":""}]""",
                    "[]",
                    "Integer",
                    """{"kind":"integer","type":"Integer","value":1}"""),
                "method identity is invalid"),
            (
                MethodJson(
                    "M:X.M",
                    """[{"name":"A","publicKeyToken":"invalid"}]""",
                    "[]",
                    "Integer",
                    """{"kind":"integer","type":"Integer","value":1}"""),
                "public-key token is invalid"),
            (
                MethodJson(
                    "M:X.M",
                    """[{"name":"A","publicKeyToken":""},{"name":"A","publicKeyToken":""}]""",
                    "[]",
                    "Integer",
                    """{"kind":"integer","type":"Integer","value":1}"""),
                "must be unique and sorted"),
            (
                MethodJson(
                    "M:X.M",
                    "[]",
                    "[]",
                    "Integer",
                    """{"kind":"integer","type":"Integer","value":1}"""),
                "requires an assembly identity"),
            (
                MethodJson(
                    "M:X.M",
                    """[{"name":"A","publicKeyToken":""}]""",
                    "[]",
                    "Boolean",
                    """{"kind":"integer","type":"Integer","value":1}"""),
                "result expression has the wrong type"),
            (
                MethodJson(
                    "M:X.M",
                    "{}",
                    "[]",
                    "Integer",
                    """{"kind":"integer","type":"Integer","value":1}"""),
                "assemblies must be an array"),
            (
                MethodJson(
                    "M:X.M",
                    """[{"name":"A","publicKeyToken":""}]""",
                    "[]",
                    "Integer",
                    """{"kind":"parameter","type":"Integer","ordinal":"zero"}"""),
                "ordinal must be an Int32")
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var (json, message) in invalid)
            {
                var error = Assert.Throws<InvalidDataException>((Action)(() =>
                    ParseMethod(json)));
                Assert.That(error!.Message, Does.Contain(message), json);
            }

            foreach (var (json, message) in new[]
                     {
                         (
                             PackJson("Bad", "[]"),
                             "not a canonical identifier"),
                         (
                             PackJson("valid", "[]"),
                             "cannot be empty"),
                         (
                             PackJson("valid", "[" + validMethod + "," +
                                 validMethod + "]"),
                             "methods must be unique and sorted")
                     })
            {
                var error = Assert.Throws<InvalidDataException>((Action)(() =>
                    ParsePack(json)));
                Assert.That(error!.Message, Does.Contain(message), json);
            }

            var unknownPack = Assert.Throws<InvalidOperationException>(
                (Action)(() => _ = new CompilerSpecificationPackProvider(
                    new IrFactory(),
                    ["unknown.pack"])));
            Assert.That(unknownPack!.Message, Does.Contain("Unknown"));
            var duplicatePack = Assert.Throws<InvalidOperationException>(
                (Action)(() => _ = new CompilerSpecificationPackProvider(
                    new IrFactory(),
                    [null!, " ", "dotnet.scalar", "dotnet.scalar"])));
            Assert.That(duplicatePack!.Message, Does.Contain("unique"));

            var provider = new CompilerSpecificationPackProvider(
                new IrFactory(),
                []);
            Assert.That(
                (Action)(() => Invoke(
                    s_typeId,
                    provider,
                    [(IrTypeKind)int.MaxValue])),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                (Action)(() => Instantiate(ParseTerm(
                    """{"kind":"parameter","type":"Integer","ordinal":-1}"""))),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                (Action)(() => Instantiate(ParseTerm(
                    """{"kind":"parameter","type":"Boolean","ordinal":0}"""))),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                (Action)(() => Instantiate(
                    ParseTerm(
                        """{"kind":"boolean","type":"Boolean","value":true}"""),
                    65)),
                Throws.TypeOf<ArgumentException>());
            using var array = JsonDocument.Parse("[]");
            Assert.That(
                (Action)(() => Invoke(
                    s_requireObject,
                    instance: null,
                    [array.RootElement, "term", KindProperty])),
                Throws.TypeOf<InvalidDataException>());
        }
    }

    [Test]
    public void SelectionAuthorityIsExplicitCanonicalAndCatalogBound()
    {
        var unset = CompilerSpecificationPackProvider.ResolveAuthority(null);
        var selected = CompilerSpecificationPackProvider.ResolveAuthority(
            [" dotnet.scalar "]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unset.SpecificationPackIds, Is.Empty);
            Assert.That(selected.SpecificationPackIds,
                Is.EqualTo(["dotnet.scalar"]));
            Assert.That(unset.SpecificationPackCatalogVersion,
                Is.EqualTo(CompilerSpecificationPackCatalogVersions.Current));
            Assert.That(unset.SpecificationPackCatalogSha256,
                Is.EqualTo(CompilerSpecificationPackCatalogVersions.Sha256));
        }

        Assert.Throws<InvalidOperationException>((Action)(() =>
            CompilerSpecificationPackProvider.ResolveAuthority(
                ["dotnet.scalar", "dotnet.scalar"])));
        Assert.Throws<InvalidOperationException>((Action)(() =>
            CompilerSpecificationPackProvider.ResolveAuthority(
                ["dotnet.scalar", "missing.pack"])));
    }

    private static string Binary(
        string operation,
        string type,
        string left,
        string right)
    {
        return $$"""
            {"kind":"binary","type":"{{type}}","operator":"{{operation}}","left":{{left}},"right":{{right}}}
            """;
    }

    private static string MethodJson(
        string identity,
        string assemblies,
        string parameterTypes,
        string resultType,
        string result)
    {
        return $$"""
            {
              "documentationCommentId":"{{identity}}",
              "assemblies":{{assemblies}},
              "parameterTypes":{{parameterTypes}},
              "resultType":"{{resultType}}",
              "result":{{result}}
            }
            """;
    }

    private static string PackJson(string id, string methods)
    {
        return $$"""
            {
              "id":"{{id}}",
              "version":"1",
              "evidence":"test-evidence",
              "methods":{{methods}}
            }
            """;
    }

    private static object ParseTerm(string json, int depth = 0)
    {
        using var document = JsonDocument.Parse(json);
        return Invoke(
            s_parseTerm,
            instance: null,
            [document.RootElement, depth]);
    }

    private static object ParseMethod(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Invoke(
            s_parseMethod,
            instance: null,
            [document.RootElement]);
    }

    private static object ParsePack(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Invoke(
            s_parsePack,
            instance: null,
            [document.RootElement]);
    }

    private static IrTerm Instantiate(object term, int depth = 0)
    {
        var factory = new IrFactory();
        var provider = new CompilerSpecificationPackProvider(factory, []);
        var parameter = factory.CreateVariable(
            "spec-pack-test:parameter",
            factory.IntegerType);
        return (IrTerm)Invoke(
            s_instantiate,
            provider,
            [term, ImmutableArray.Create(parameter), depth]);
    }

    private static object Invoke(
        MethodInfo method,
        object? instance,
        object?[] arguments)
    {
        try
        {
            return method.Invoke(instance, arguments) ??
                throw new InvalidOperationException(
                    method.Name + " unexpectedly returned null.");
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
