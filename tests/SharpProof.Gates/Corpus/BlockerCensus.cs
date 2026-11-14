using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Frontend;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Gates.Corpus;

// Lowers every method of every pinned source with the advisory pipeline and
// counts the constructs the frontend abstains on. The "sole" count of a
// construct is the number of methods where it is the only blocker the
// lowering reports. That is an upper bound on what supporting the construct
// by itself could gain: an expression that abstains on one operand does not
// lower its other operands, so their blockers stay hidden until the first is
// fixed. Developer evidence only; it publishes no verdict.
internal static class BlockerCensus
{
    private const string Exact = "(lowers exactly)";

    internal static string Run(string root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        var document = OpenSourceCorpusCatalog.Load(root);
        var sources = document.Sources.Select(static source => source.Id).ToImmutableArray();
        var any = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        var sole = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        var totals = new int[sources.Length];
        for (var index = 0; index < sources.Length; index++)
        {
            var whole = NativeEffectOracleGate.ExpandToWholeSource(document, sources[index], cancellationToken);
            foreach (var batch in whole.Methods.Chunk(250))
            {
                var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(
                    whole with { Methods = [.. batch] }, cancellationToken, purity: true);
                foreach (var declaration in compilation.SyntaxTrees
                             .SelectMany(tree => tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>())
                             .Where(static declaration => OpenSourceCorpusRunner.CorpusMethodId(declaration) != null))
                {
                    totals[index]++;
                    var blockers = Blockers(compilation, declaration, cancellationToken);
                    foreach (var blocker in blockers)
                    {
                        Count(any, blocker, sources.Length)[index]++;
                    }
                    if (blockers.Length == 1)
                    {
                        Count(sole, blockers[0], sources.Length)[index]++;
                    }
                }
            }
        }
        return Render(sources, totals, any, sole);
    }

    private static int[] Count(SortedDictionary<string, int[]> counts, string key, int width)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(key);
        if (!counts.TryGetValue(key, out var row))
        {
            row = new int[width];
            counts.Add(key, row);
        }
        return row;
    }

    // The distinct constructs one method abstains on, or the single marker
    // for a body that lowers exactly.
    internal static ImmutableArray<string> Blockers(
        CSharpCompilation compilation, MethodDeclarationSyntax declaration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(declaration);
        var model = CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
        if (model.GetDeclaredSymbol(declaration, cancellationToken) is not { } method ||
            !TotalSourceCallSession.IsBodyDeclaration(declaration))
        {
            return ["UnsupportedCallable"];
        }
        var paths = compilation.SyntaxTrees.Select(static (tree, ordinal) => (tree, ordinal))
            .ToImmutableDictionary(static item => item.tree, static item => item.ordinal.ToString(CultureInfo.InvariantCulture));
        var context = new TotalLoweringContext(new IrFactory(), method, tree => paths[tree]);
        var binding = new TotalContractBinder(compilation, context.Factory).BindTotal(context, cancellationToken: cancellationToken);
        if (!binding.IsSuccess)
        {
            return ["ContractBinding: " + binding.Failure];
        }
        var graph = TotalSourceCallSession.CreateGraph(declaration, method, model, cancellationToken);
        var lowering = graph == null
            ? null
            : TotalBodyLowering.Lower(compilation, graph, context, cancellationToken, opaqueCalls: true, stopAtFirstAbstention: false);
        if (lowering == null)
        {
            return ["UnsupportedCallable"];
        }
        if (lowering.IsExact)
        {
            return [Exact];
        }
        var trees = paths.ToImmutableDictionary(static item => item.Value, static item => item.Key, StringComparer.Ordinal);
        var blockers = lowering.Abstentions
            .Select(abstention => Describe(abstention, context.Factory, trees, compilation, cancellationToken))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        return blockers.IsEmpty ? ["Lowering: " + lowering.Classification.Abstention] : blockers;
    }

    private static string Describe(
        FrontendProgramAbstention abstention, IrFactory factory, ImmutableDictionary<string, SyntaxTree> trees,
        CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(compilation);
        var reason = abstention.Reason.ToString();
        if (factory.GetOperationInfo(abstention.Operation).SourceSpan is not { } span ||
            !trees.TryGetValue(span.Document, out var tree))
        {
            return reason;
        }
        var node = tree.GetRoot(cancellationToken).FindNode(new TextSpan(span.Start, span.Length), getInnermostNodeForTie: true);
        var operation = CompilationModelProvider.GetSemanticModel(compilation, tree).GetOperation(node, cancellationToken);
        return reason + ": " + (operation == null ? node.Kind().ToString() : Describe(operation, compilation));
    }

    private static string Describe(IOperation operation, CSharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(compilation);
        return operation switch
        {
            IInvocationOperation invocation => "call " + Member(invocation.TargetMethod, compilation) +
                (SameType(invocation, invocation.TargetMethod.ContainingType) ? " [same-type]" : ""),
            IObjectCreationOperation creation => "new " + (creation.Constructor is { } constructor
                ? Owner(constructor.ContainingType, compilation)
                : "unknown"),
            IPropertyReferenceOperation property => "property " + Owner(property.Property.ContainingType, compilation) +
                (property.Property.IsStatic ? " static" : ""),
            IFieldReferenceOperation field => "field " + Owner(field.Field.ContainingType, compilation) +
                (field.Field.IsStatic ? " static" : ""),
            IConversionOperation conversion => "conversion to " + Kind(conversion.Type),
            IBinaryOperation binary => "binary " + binary.OperatorKind + " on " + Kind(binary.LeftOperand.Type),
            _ => operation.Kind + (operation.Type == null ? "" : " of " + Kind(operation.Type))
        };
    }

    private static string Member(IMethodSymbol method, CSharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(compilation);
        var dispatch = method.ContainingType.TypeKind == TypeKind.Interface ? " interface"
            : method.IsVirtual || method.IsOverride || method.IsAbstract ? " virtual"
            : method.MethodKind == MethodKind.DelegateInvoke ? " delegate"
            : method.IsStatic ? " static" : " instance";
        var label = Owner(method.ContainingType, compilation) + dispatch +
            (method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ? " ref/out" : "") +
            (method.IsGenericMethod ? " generic" : "");
        if (!SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly))
        {
            return label;
        }
        // Why a source callee is not inlined, as far as its declaration shows.
        var declaration = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        return label +
            (method.ReducedFrom != null ? " [extension-form]" : "") +
            (method.Parameters.Any(static parameter => !CSharpOperationSemantics.IsValueDomain(parameter.Type)) ||
                !method.ReturnsVoid && !CSharpOperationSemantics.IsValueDomain(method.ReturnType) ? " [non-value-domain]" : "") +
            (HasTypeInitializer(method.ContainingType) ? " [type-initializer]" : "") +
            (declaration != null && declaration.DescendantNodes().Any(static node => node is YieldStatementSyntax) ? " [iterator]" : "") +
            (declaration != null && declaration.DescendantNodes().Any(static node => node is ThrowStatementSyntax or ThrowExpressionSyntax) ? " [throws]" : "");
    }

    // The callee is declared in the caller's type or one that encloses it.
    private static bool SameType(IOperation operation, INamedTypeSymbol callee)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(callee);
        for (var current = operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart)?.ContainingType;
             current != null; current = current.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, callee.OriginalDefinition))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasTypeInitializer(INamedTypeSymbol type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.StaticConstructors.Any(static constructor => !constructor.IsImplicitlyDeclared) ||
            type.GetMembers().Any(static member => member is IFieldSymbol { IsStatic: true, IsConst: false } field &&
                field.DeclaringSyntaxReferences.Any(static reference =>
                    reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null })) ||
            type.GetMembers().Any(static member => member is IPropertySymbol { IsStatic: true } property &&
                property.DeclaringSyntaxReferences.Any(static reference =>
                    reference.GetSyntax() is PropertyDeclarationSyntax { Initializer: not null }));
    }

    // Source types by shape; framework types by name, so the table shows
    // which APIs a catalog entry would unblock.
    private static string Owner(INamedTypeSymbol? type, CSharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (type == null)
        {
            return "unknown";
        }
        return SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)
            ? "source " + Kind(type)
            : type.ContainingNamespace + "." + type.Name;
    }

    private static string Kind(ITypeSymbol? type)
    {
        return type switch
        {
            null => "none",
            ITypeParameterSymbol => "type parameter",
            IArrayTypeSymbol => "array",
            { SpecialType: not SpecialType.None } => type.SpecialType.ToString(),
            { TypeKind: TypeKind.Enum } => "enum",
            { TypeKind: TypeKind.Struct } => "struct",
            { TypeKind: TypeKind.Interface } => "interface",
            { TypeKind: TypeKind.Delegate } => "delegate",
            _ => "class"
        };
    }

    private static string Render(
        ImmutableArray<string> sources, int[] totals, SortedDictionary<string, int[]> any, SortedDictionary<string, int[]> sole)
    {
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(any);
        ArgumentNullException.ThrowIfNull(sole);
        var text = new StringBuilder();
        text.Append("# Lowering blockers across whole pinned sources\n\n");
        text.Append("Methods: ").Append(totals.Sum().ToString(CultureInfo.InvariantCulture)).Append(" (")
            .AppendJoin(", ", sources.Select((id, index) => $"{id} {totals[index]}")).Append(").\n\n");
        text.Append("Any counts methods that report the construct. Sole counts methods that report nothing else; it is an upper bound ");
        text.Append("on the gain from supporting the construct, because an abstaining operand hides blockers in sibling operands.\n\n");
        text.Append("| Construct | Sole | Any | ").AppendJoin(" | ", sources.Select(static id => $"`{id}` sole")).Append(" |\n");
        text.Append("| --- | ---: | ---: |").Insert(text.Length, " ---: |", sources.Length).Append('\n');
        foreach (var row in any
                     .OrderByDescending(row => sole.TryGetValue(row.Key, out var value) ? value.Sum() : 0)
                     .ThenByDescending(static row => row.Value.Sum())
                     .ThenBy(static row => row.Key, StringComparer.Ordinal))
        {
            var soleRow = sole.TryGetValue(row.Key, out var value) ? value : new int[sources.Length];
            text.Append("| ").Append(row.Key.Replace("|", "\\|", StringComparison.Ordinal)).Append(" | ")
                .Append(soleRow.Sum().ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(row.Value.Sum().ToString(CultureInfo.InvariantCulture));
            foreach (var count in soleRow)
            {
                text.Append(" | ").Append(count.ToString(CultureInfo.InvariantCulture));
            }
            text.Append(" |\n");
        }
        return text.ToString();
    }
}
