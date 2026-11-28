using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// The runtime and HTTP-entry checks: a documented validation is compared with the
// guards the body runs, with the DataAnnotations and required-ness an ASP.NET Core
// handler declares, and with an OpenAPI 3 document. ASP.NET Core types are declared
// in each test source, so the analyzer sees the same full names the framework uses.
[TestFixture]
public sealed class DocumentedValidationRuntimeTests
{
    private const string MismatchRule = "SP0061";
    private const string OpenApiRule = "SP0062";
    private const string UndocumentedRule = "SP0063";

    private const string AspNetStubs = """
        using Microsoft.AspNetCore.Builder;
        namespace Microsoft.AspNetCore.Builder
        {
            public static class EndpointRouteBuilderExtensions
            {
                public static void MapGet(this object app, string pattern, System.Func<int, object> handler) { }
                public static void MapGet(this object app, string pattern, System.Func<string, object> handler) { }
            }
        }
        namespace Microsoft.AspNetCore.Http
        {
            public static class Results
            {
                public static object BadRequest() => new object();
            }
        }
        namespace System.ComponentModel.DataAnnotations
        {
            [System.AttributeUsage(System.AttributeTargets.Parameter | System.AttributeTargets.Property)]
            public sealed class RangeAttribute : System.Attribute
            {
                public RangeAttribute(int minimum, int maximum) { }
            }
        }
        """;

    [Test]
    public async Task RuntimeGuardMatchingTheDocumentedRangeIsConsistent()
    {
        var diagnostics = await Analyze("""
            using System;
            public static class Fixture {
                /// <param name="page">Must be between 1 and 100.</param>
                public static int Get(int page) {
                    if (page < 1 || page > 100) throw new ArgumentOutOfRangeException(nameof(page));
                    return page;
                }
            }
            """, [MismatchRule, UndocumentedRule]);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task RuntimeGuardNarrowerThanTheDocumentationIsReported()
    {
        var diagnostics = await Analyze("""
            using System;
            public static class Fixture {
                /// <param name="page">Must be between 1 and 100.</param>
                public static int Get(int page) {
                    if (page < 1 || page > 50) throw new ArgumentOutOfRangeException(nameof(page));
                    return page;
                }
            }
            """, [MismatchRule]);
        AnalyzerTestHost.AssertIds(diagnostics, [MismatchRule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "its declarations and runtime guards admit 1 through 50");
    }

    [Test]
    public async Task ArgumentThrowIfGuardsAreReadAsRuntimeValidation()
    {
        var diagnostics = await Analyze("""
            using System;
            public static class Fixture {
                /// <param name="page">Must be between 1 and 100.</param>
                public static int Get(int page) {
                    if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(page, 100);
                    return page;
                }
            }
            """, [MismatchRule, UndocumentedRule]);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task UndocumentedRuntimeGuardIsReportedAsInfo()
    {
        var diagnostics = await Analyze("""
            using System;
            public static class Fixture {
                /// <summary>Gets a page.</summary>
                public static int Get(int page) {
                    if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
                    return page;
                }
            }
            """, [UndocumentedRule]);
        AnalyzerTestHost.AssertIds(diagnostics, [UndocumentedRule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "Parameter 'page' is checked at runtime and admits at least 1, but its XML documentation does not state it");
    }

    // The leading run of guards ends at the first statement that is not a
    // rejection, so a later guard is not read. The mention makes page Unknown,
    // which keeps the analyzer silent instead of reporting "no runtime check".
    [Test]
    public async Task GuardAfterOtherWorkIsNotReadAndStaysSilent()
    {
        var diagnostics = await Analyze("""
            using System;
            public static class Fixture {
                /// <param name="page">Must be at least 1.</param>
                public static int Get(int page) {
                    var doubled = page * 2;
                    if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
                    return doubled;
                }
            }
            """, [MismatchRule, UndocumentedRule]);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task HttpEntryWithoutRuntimeValidationIsReported()
    {
        var diagnostics = await Analyze(AspNetStubs + """

            public static class Handlers {
                /// <param name="id">Must be between 1 and 100.</param>
                public static object GetItem(int id) => id;
            }
            public static class Program {
                public static void Main() {
                    object app = new object();
                    app.MapGet("/items/{id}", Handlers.GetItem);
                }
            }
            """, [MismatchRule]);
        AnalyzerTestHost.AssertIds(diagnostics, [MismatchRule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "no runtime guard or DataAnnotation bounds it on this HTTP entry");
    }

    [Test]
    public async Task HttpEntryRejectingOutOfRangeInputIsConsistent()
    {
        var diagnostics = await Analyze(AspNetStubs + """

            public static class Handlers {
                /// <param name="id">Must be between 1 and 100.</param>
                public static object GetItem(int id) {
                    if (id < 1 || id > 100) return Microsoft.AspNetCore.Http.Results.BadRequest();
                    return id;
                }
            }
            public static class Program {
                public static void Main() {
                    object app = new object();
                    app.MapGet("/items/{id}", Handlers.GetItem);
                }
            }
            """, [MismatchRule, OpenApiRule]);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task HttpEntryDataAnnotationRangeCountsAsDeclaredValidation()
    {
        var diagnostics = await Analyze(AspNetStubs + """

            public static class Handlers {
                /// <param name="id">Must be between 1 and 100.</param>
                public static object GetItem([System.ComponentModel.DataAnnotations.Range(1, 100)] int id) => id;
            }
            public static class Program {
                public static void Main() {
                    object app = new object();
                    app.MapGet("/items/{id}", Handlers.GetItem);
                }
            }
            """, [MismatchRule, UndocumentedRule]);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task UndocumentedDataAnnotationOnAnHttpEntryIsReportedAsInfo()
    {
        var diagnostics = await Analyze(AspNetStubs + """

            public static class Handlers {
                public static object GetItem([System.ComponentModel.DataAnnotations.Range(1, 100)] int id) => id;
            }
            public static class Program {
                public static void Main() {
                    object app = new object();
                    app.MapGet("/items/{id}", Handlers.GetItem);
                }
            }
            """, [UndocumentedRule]);
        AnalyzerTestHost.AssertIds(diagnostics, [UndocumentedRule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "Parameter 'id' is checked at runtime and admits 1 through 100");
    }

    [Test]
    public async Task ControllerActionWithoutRuntimeValidationIsReported()
    {
        var diagnostics = await Analyze("""
            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method)]
                public sealed class RouteAttribute : System.Attribute { public RouteAttribute(string template) { } }
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class HttpGetAttribute : System.Attribute { public HttpGetAttribute(string template) { } }
            }
            [Microsoft.AspNetCore.Mvc.Route("items")]
            public sealed class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase {
                /// <param name="id">Must be between 1 and 100.</param>
                [Microsoft.AspNetCore.Mvc.HttpGet("{id}")]
                public object Get(int id) => id;
            }
            """, [MismatchRule]);
        AnalyzerTestHost.AssertIds(diagnostics, [MismatchRule]);
    }

    [Test]
    public async Task OpenApiBoundsDisagreeingWithTheHandlerAndDocumentationAreReported()
    {
        const string openApi = """
            {"openapi":"3.0.1","paths":{"/items/{id}":{"get":{"parameters":[
              {"name":"id","in":"path","required":true,"schema":{"type":"integer","minimum":1,"maximum":1000}}]}}}}
            """;
        var diagnostics = await AnalyzeWithOpenApi(AspNetStubs + """

            public static class Handlers {
                /// <param name="id">Must be between 1 and 100.</param>
                public static object GetItem(int id) {
                    if (id < 1 || id > 100) return Microsoft.AspNetCore.Http.Results.BadRequest();
                    return id;
                }
            }
            public static class Program {
                public static void Main() {
                    object app = new object();
                    app.MapGet("/items/{id}", Handlers.GetItem);
                }
            }
            """, openApi);
        AnalyzerTestHost.AssertIds(diagnostics, [OpenApiRule, OpenApiRule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "OpenAPI declares 1 through 1000 for 'id' of GET /items/{id}, but the HTTP entry admits 1 through 100");
    }

    [Test]
    public async Task OpenApiBoundsMatchingTheHandlerAreConsistent()
    {
        const string openApi = """
            {"openapi":"3.0.1","paths":{"/items/{id}":{"get":{"parameters":[
              {"name":"id","in":"path","required":true,"schema":{"type":"integer","minimum":1,"maximum":100}}]}}}}
            """;
        var diagnostics = await AnalyzeWithOpenApi(AspNetStubs + """

            public static class Handlers {
                /// <param name="id">Must be between 1 and 100.</param>
                public static object GetItem(int id) {
                    if (id < 1 || id > 100) return Microsoft.AspNetCore.Http.Results.BadRequest();
                    return id;
                }
            }
            public static class Program {
                public static void Main() {
                    object app = new object();
                    app.MapGet("/items/{id}", Handlers.GetItem);
                }
            }
            """, openApi);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task OpenApiNullableDisagreeingWithARejectedNullIsReported()
    {
        const string openApi = """
            {"openapi":"3.0.1","paths":{"/items":{"get":{"parameters":[
              {"name":"name","in":"query","schema":{"type":"string","nullable":true}}]}}}}
            """;
        var diagnostics = await AnalyzeWithOpenApi(AspNetStubs + """

            public static class Handlers {
                public static object Find(string name) {
                    if (name is null) return Microsoft.AspNetCore.Http.Results.BadRequest();
                    return name;
                }
            }
            public static class Program {
                public static void Main() {
                    object app = new object();
                    app.MapGet("/items", Handlers.Find);
                }
            }
            """, openApi);
        AnalyzerTestHost.AssertIds(diagnostics, [OpenApiRule, UndocumentedRule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "OpenAPI declares 'name' of GET /items nullable, but the HTTP entry rejects null");
    }

    [Test]
    public async Task AMalformedOpenApiDocumentIsNotCompared()
    {
        var diagnostics = await AnalyzeWithOpenApi(AspNetStubs + """

            public static class Handlers {
                /// <param name="id">Must be between 1 and 100.</param>
                public static object GetItem(int id) {
                    if (id < 1 || id > 100) return Microsoft.AspNetCore.Http.Results.BadRequest();
                    return id;
                }
            }
            public static class Program {
                public static void Main() {
                    object app = new object();
                    app.MapGet("/items/{id}", Handlers.GetItem);
                }
            }
            """, "{\"openapi\":\"3.0.1\",\"paths\":{");
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    private static Task<ImmutableArray<Diagnostic>> Analyze(string source, string[] enabledIds)
    {
        return AnalyzerTestHost.AnalyzeAsync(source, "contracts", enabledIds, profile: "strict");
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeWithOpenApi(string source, string openApi)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            source,
            [MismatchRule, OpenApiRule, UndocumentedRule]);
        var values = ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            [
                KeyValuePair.Create("build_property.SharpProofProfile", "strict"),
                KeyValuePair.Create("build_property.SharpProofFeatures", "contracts")
            ]);
        return AnalyzerTestHost.AnalyzeAsync(
            compilation,
            values,
            ImmutableArray.Create<AdditionalText>(new MemoryAdditionalText("openapi.json", openApi)));
    }

    private sealed class MemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken)
        {
            return SourceText.From(text);
        }
    }
}
