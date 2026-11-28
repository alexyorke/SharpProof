using System.Text.RegularExpressions;

namespace SharpProof.Analyzer;

// One HTTP method and route through which a handler is reachable.
internal sealed class HttpEntryRoute
{
    public HttpEntryRoute(string verb, string route)
    {
        Verb = RepositoryParameterGuard.NotNull(verb, nameof(verb));
        Route = RepositoryParameterGuard.NotNull(route, nameof(route));
    }

    public string Verb { get; }

    public string Route { get; }
}

// Finds the ASP.NET Core handlers a compilation exposes: method groups passed to
// Map{Get,Post,Put,Delete,Patch} with a literal route, and controller actions
// with an Http{Get,Post,Put,Delete,Patch} attribute. Lambda handlers, MapGroup
// prefixes and routes built at run time are not resolved, so their handlers are
// left out and get no HTTP-entry checks.
internal static class HttpEntryCatalog
{
    private static readonly Regex RouteParameter = new(
        @"\{([^{}:?]+)[^{}]*\}",
        RegexOptions.CultureInvariant);

    internal static ImmutableDictionary<IMethodSymbol, ImmutableArray<HttpEntryRoute>> Collect(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var routes = ImmutableDictionary.CreateBuilder<IMethodSymbol, ImmutableArray<HttpEntryRoute>>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AnalyzerGeneratedCodePolicy.IsGenerated(tree, compilation, cancellationToken))
            {
                continue;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree);
            var root = tree.GetRoot(cancellationToken);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (MinimalApiRegistration(model, invocation, cancellationToken) is { } registration)
                {
                    Add(routes, registration.Handler, registration.Route);
                }
            }

            foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is not { } method)
                {
                    continue;
                }

                foreach (var route in ControllerRoutes(method))
                {
                    Add(routes, method, route);
                }
            }
        }

        return routes.ToImmutable();
    }

    // The route template as OpenAPI names a path: a leading slash, no trailing
    // slash, and no constraint or optional marker inside a parameter.
    internal static string NormalizeRoute(string route)
    {
        route = RepositoryParameterGuard.NotNull(route, nameof(route));
        var trimmed = route.Trim('/');
        var normalized = RouteParameter.Replace(trimmed, "{$1}");
        return normalized.Length == 0 ? "/" : "/" + normalized;
    }

    // Parameters the framework supplies from services or the request object are
    // not validated against a required value, so they are not treated as required.
    internal static bool IsRequestBound(IParameterSymbol parameter)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        if (!parameter.Type.IsReferenceType ||
            parameter.NullableAnnotation != NullableAnnotation.NotAnnotated ||
            parameter.Type.ContainingNamespace is { } typeNamespace &&
                NamespaceNames.Of(typeNamespace).StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
        {
            return false;
        }

        return !parameter.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.Name is "FromServicesAttribute" or "FromKeyedServicesAttribute" or "AsParametersAttribute");
    }

    // The validation a handler parameter declares to the framework: DataAnnotations
    // Range and Required. Whether the host enforces them depends on its
    // configuration, so these are declared behavior, not verified behavior.
    internal static DeclaredValidation DeclaredFrameworkValidation(IParameterSymbol parameter)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        var facts = DeclaredValidation.None;
        foreach (var attribute in parameter.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null ||
                NamespaceNames.Of(attributeClass.ContainingNamespace) != "System.ComponentModel.DataAnnotations")
            {
                continue;
            }

            if (attributeClass.Name == "RangeAttribute")
            {
                var arguments = attribute.ConstructorArguments;
                facts = arguments.Length == 2 &&
                    TryIntegral(arguments[0].Value, out var minimum) &&
                    TryIntegral(arguments[1].Value, out var maximum)
                        ? facts.And(new(false, minimum, maximum, false))
                        : facts.And(DeclaredValidation.Opaque);
            }
            else if (attributeClass.Name == "RequiredAttribute")
            {
                facts = facts.And(new(true, null, null, false));
            }
        }

        return facts;
    }

    private static bool TryIntegral(object? value, out long result)
    {
        result = 0;
        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long:
                result = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            default:
                return false;
        }
    }

    private static void Add(
        ImmutableDictionary<IMethodSymbol, ImmutableArray<HttpEntryRoute>>.Builder routes,
        IMethodSymbol method,
        HttpEntryRoute route)
    {
        routes[method] = routes.TryGetValue(method, out var existing)
            ? existing.Add(route)
            : ImmutableArray.Create(route);
    }

    private static (IMethodSymbol Handler, HttpEntryRoute Route)? MinimalApiRegistration(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol map ||
            !NamespaceNames.Of(map.ContainingNamespace).StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            MinimalApiVerb(map.Name) is not { } verb)
        {
            return null;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 2 ||
            model.GetConstantValue(arguments[0].Expression, cancellationToken).Value is not string pattern ||
            model.GetSymbolInfo(arguments[1].Expression, cancellationToken).Symbol is not IMethodSymbol { MethodKind: MethodKind.Ordinary } handler)
        {
            return null;
        }

        return (handler, new HttpEntryRoute(verb, NormalizeRoute(pattern)));
    }

    private static string? MinimalApiVerb(string name)
    {
        return name switch
        {
            "MapGet" => "GET",
            "MapPost" => "POST",
            "MapPut" => "PUT",
            "MapDelete" => "DELETE",
            "MapPatch" => "PATCH",
            _ => null
        };
    }

    private static ImmutableArray<HttpEntryRoute> ControllerRoutes(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type is null)
        {
            return ImmutableArray<HttpEntryRoute>.Empty;
        }

        var controllerRoute = AttributeTemplate(type, "RouteAttribute") ?? string.Empty;
        var controllerName = type.Name.EndsWith("Controller", StringComparison.Ordinal)
            ? type.Name.Substring(0, type.Name.Length - "Controller".Length)
            : type.Name;
        var routes = ImmutableArray.CreateBuilder<HttpEntryRoute>();
        foreach (var attribute in method.GetAttributes())
        {
            var verb = ControllerVerb(attribute.AttributeClass);
            if (verb is null)
            {
                continue;
            }

            var template = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string text
                ? text
                : string.Empty;
            var combined = template.StartsWith("/", StringComparison.Ordinal) || template.StartsWith("~/", StringComparison.Ordinal)
                ? template.TrimStart('~')
                : string.Join("/", new[]
                {
                    controllerRoute.Replace("[controller]", controllerName).Trim('/'),
                    template.Trim('/')
                }.Where(static part => part.Length > 0));
            routes.Add(new HttpEntryRoute(verb, NormalizeRoute(combined)));
        }

        return routes.ToImmutable();
    }

    private static string? AttributeTemplate(ISymbol symbol, string attributeName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == attributeName &&
                attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string template)
            {
                return template;
            }
        }

        return null;
    }

    private static string? ControllerVerb(INamedTypeSymbol? attributeClass)
    {
        if (attributeClass is null ||
            !NamespaceNames.Of(attributeClass.ContainingNamespace).StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal))
        {
            return null;
        }

        return attributeClass.Name switch
        {
            "HttpGetAttribute" => "GET",
            "HttpPostAttribute" => "POST",
            "HttpPutAttribute" => "PUT",
            "HttpDeleteAttribute" => "DELETE",
            "HttpPatchAttribute" => "PATCH",
            _ => null
        };
    }
}
