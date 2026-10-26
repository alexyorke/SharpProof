using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

internal static class RecursiveConversionChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        Check("ReturnUserConversion", "ReturnCycle", "7:1:True", "9:1:True", """
            public sealed class Input
            {
                public static int Conversions;
                public readonly int Id;
                public Input(int id) { Id = id; }
                public static implicit operator Output(Input value)
                {
                    Conversions++;
                    return new Output(value);
                }
            }
            public sealed class Output
            {
                public readonly Input Source;
                public Output(Input source) { Source = source; }
            }
            public static class ReturnCycle
            {
                public static Output First(Input value, int marker, bool again)
                    => again ? Second(value, marker, false) : value;
                public static Output Second(Input value, int marker, bool again)
                    => again ? First(value, marker, false) : value;
                public static string Run()
                {
                    Input.Conversions = 0;
                    var input = new Input(7);
                    var result = First(input, 1, true);
                    return result.Source.Id + ":" + Input.Conversions + ":" + ReferenceEquals(result.Source, input);
                }
            }
            """, """
            public static string NewRun()
            {
                Input.Conversions = 0;
                var input = new Input(9);
                var result = First(input, "marker", true);
                return result.Source.Id + ":" + Input.Conversions + ":" + ReferenceEquals(result.Source, input);
            }
            """, requireGenericOwner: false, cancellationToken);

        Check("ArgumentUserConversion", "ArgumentCycle", "7:1:True", "9:1:True", """
            public sealed class ArgSource
            {
                public static int Conversions;
                public readonly int Id;
                public ArgSource(int id) { Id = id; }
                public static implicit operator ArgTarget(ArgSource value)
                {
                    Conversions++;
                    return new ArgTarget(value);
                }
            }
            public sealed class ArgTarget
            {
                public readonly ArgSource Source;
                public ArgTarget(ArgSource source) { Source = source; }
            }
            public static class ArgumentCycle
            {
                public static ArgTarget First(ArgSource source, ArgTarget value, int marker, bool again)
                    => again ? Second(source, source, marker, false) : value;
                public static ArgTarget Second(ArgSource source, ArgTarget value, int marker, bool again)
                    => again ? First(source, source, marker, false) : value;
                public static string Run()
                {
                    ArgSource.Conversions = 0;
                    var source = new ArgSource(7);
                    var result = First(source, new ArgTarget(source), 1, true);
                    return result.Source.Id + ":" + ArgSource.Conversions + ":" + ReferenceEquals(result.Source, source);
                }
            }
            """, """
            public static string NewRun()
            {
                ArgSource.Conversions = 0;
                var source = new ArgSource(9);
                var result = First(source, new ArgTarget(source), "marker", true);
                return result.Source.Id + ":" + ArgSource.Conversions + ":" + ReferenceEquals(result.Source, source);
            }
            """, requireGenericOwner: false, cancellationToken);

        Check("Boxing", "BoxingCycle", "True", "True", """
            public static class BoxingCycle
            {
                public static object First(int value, int marker, bool again)
                    => again ? Second(value, marker, false) : value;
                public static object Second(int value, int marker, bool again)
                    => again ? First(value, marker, false) : value;
                public static string Run()
                {
                    var boxed = First(7, 1, true);
                    return (boxed.GetType() == typeof(int) && Equals(boxed, 7)).ToString();
                }
            }
            """, """
            public static string NewRun()
            {
                var boxed = First(9, "marker", true);
                return (boxed.GetType() == typeof(int) && Equals(boxed, 9)).ToString();
            }
            """, requireGenericOwner: false, cancellationToken);

        Check("GenericOwnerConversion", "GenericOwnerCycle", "7:1:True", "True:1:True", """
            public sealed class Input<T>
            {
                public static int Conversions;
                public readonly T Value;
                public Input(T value) { Value = value; }
                public static implicit operator Output<T>(Input<T> value)
                {
                    Conversions++;
                    return new Output<T>(value);
                }
            }
            public sealed class Output<T>
            {
                public readonly Input<T> Source;
                public Output(Input<T> source) { Source = source; }
            }
            public sealed class Payload { }
            public static class GenericOwnerCycle
            {
                public static Output<int> First(Input<int> value, int marker, bool again)
                    => again ? Second(value, marker, false) : value;
                public static Output<int> Second(Input<int> value, int marker, bool again)
                    => again ? First(value, marker, false) : value;
                public static string Run()
                {
                    Input<int>.Conversions = 0;
                    var input = new Input<int>(7);
                    var result = First(input, 1, true);
                    return result.Source.Value + ":" + Input<int>.Conversions + ":" + ReferenceEquals(result.Source, input);
                }
            }
            """, """
            public static string NewRun()
            {
                var payload = new Payload();
                Input<Payload>.Conversions = 0;
                var input = new Input<Payload>(payload);
                var result = First(input, "marker", true);
                return ReferenceEquals(result.Source.Value, payload) + ":" + Input<Payload>.Conversions
                    + ":" + ReferenceEquals(result.Source, input);
            }
            """, requireGenericOwner: true, cancellationToken);

        return 4;
    }

    private static void Check(string label, string ownerName, string expectedOriginal,
        string expectedNew, string source, string newMethod, bool requireGenericOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(ownerName);
        ArgumentNullException.ThrowIfNull(expectedOriginal);
        ArgumentNullException.ThrowIfNull(expectedNew);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(newMethod);
        cancellationToken.ThrowIfCancellationRequested();
        var tree = CSharpSyntaxTree.ParseText(source, path: label + ".cs",
            cancellationToken: cancellationToken);
        var original = Program.Compile([tree]).WithAssemblyName("RecursiveOriginal" + label);
        AssertClean(original, label + " native", cancellationToken);
        AssertRuntime(original, ownerName, "Run", expectedOriginal, label + " native", cancellationToken);

        MethodDeclarationSyntax? first = null;
        foreach (var method in tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Identifier.ValueText == "First")
            { first = method; break; }
        }
        if (first is null)
        { throw new InvalidOperationException(label + " fixture lacks First."); }
        var proposal = Inference.AnalyzeRecursiveGroup(first,
            original.GetSemanticModel(tree), cancellationToken)
            ?? throw new InvalidOperationException(label + " SCC did not produce a joint proposal.");
        if (proposal.Results.Count != 2)
        { throw new InvalidOperationException(label + " proposal did not contain both members."); }
        foreach (var result in proposal.Results.Values)
        {
            if (result.Status != "Suggestion" || result.Signature is null)
            { throw new InvalidOperationException(label + " contains an unvalidated member."); }
        }

        var rewrittenRoot = proposal.RewrittenRoots[tree];
        var rewritten = tree.WithRootAndOptions(rewrittenRoot, tree.Options);
        var changed = original.ReplaceSyntaxTree(tree, rewritten)
            .WithAssemblyName("RecursiveChanged" + label);
        AssertClean(changed, label + " joint rewrite", cancellationToken);
        AssertGeneralized(changed, rewritten, requireGenericOwner, label, cancellationToken);
        AssertRuntime(changed, ownerName, "Run", expectedOriginal,
            label + " rewritten old domain", cancellationToken);

        ClassDeclarationSyntax? owner = null;
        foreach (var type in rewrittenRoot.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (type.Identifier.ValueText == ownerName)
            { owner = type; break; }
        }
        if (owner is null)
        { throw new InvalidOperationException(label + " fixture lacks its owner."); }
        var parsedMethod = SyntaxFactory.ParseMemberDeclaration(newMethod)
            ?? throw new InvalidOperationException(label + " new domain method failed parsing.");
        var extendedRoot = rewrittenRoot.ReplaceNode(owner, owner.AddMembers(parsedMethod));
        var extended = tree.WithRootAndOptions(extendedRoot, tree.Options);
        var newDomain = changed.ReplaceSyntaxTree(rewritten, extended)
            .WithAssemblyName("RecursiveNewDomain" + label);
        AssertClean(newDomain, label + " new domain", cancellationToken);
        AssertRuntime(newDomain, ownerName, "NewRun", expectedNew,
            label + " rewritten new domain", cancellationToken);
        Program.Log.Info("PASS Recursive" + label + "NativeAtomicRuntime");
    }

    private static void AssertGeneralized(CSharpCompilation changed, SyntaxTree rewritten,
        bool requireGenericOwner, string label, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(rewritten);
        ArgumentNullException.ThrowIfNull(label);
        var model = changed.GetSemanticModel(rewritten);
        var found = 0;
        foreach (var method in rewritten.GetRoot(cancellationToken)
            .DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Identifier.ValueText is not ("First" or "Second"))
            { continue; }
            found++;
            var symbol = model.GetDeclaredSymbol(method, cancellationToken)
                ?? throw new InvalidOperationException(label + " rewritten member did not bind.");
            IParameterSymbol? marker = null;
            foreach (var parameter in symbol.Parameters)
            {
                if (parameter.Name == "marker")
                { marker = parameter; break; }
            }
            if (marker?.Type is not ITypeParameterSymbol)
            { throw new InvalidOperationException(label + " marker was not generalized."); }
            if (!requireGenericOwner)
            { continue; }
            if (symbol.Parameters[0].Type is not INamedTypeSymbol input
                || input.TypeArguments.Length != 1
                || input.TypeArguments[0] is not ITypeParameterSymbol ownerType
                || symbol.ReturnType is not INamedTypeSymbol output
                || output.TypeArguments.Length != 1
                || !SymbolEqualityComparer.Default.Equals(ownerType, output.TypeArguments[0]))
            { throw new InvalidOperationException(label + " input/output owners lost their shared type argument."); }
        }
        if (found != 2)
        { throw new InvalidOperationException(label + " rewritten member count changed."); }
    }

    private static void AssertClean(CSharpCompilation compilation, string label,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(label);
        var problems = new List<string>();
        foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
        {
            if (diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            { problems.Add(diagnostic.ToString()); }
        }
        if (problems.Count != 0)
        { throw new InvalidOperationException(label + ": " + string.Join("; ", problems)); }
    }

    private static void AssertRuntime(CSharpCompilation compilation, string ownerName,
        string methodName, string expected, string label, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(ownerName);
        ArgumentNullException.ThrowIfNull(methodName);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(label);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        {
            var diagnostics = new List<string>();
            foreach (var diagnostic in emitted.Diagnostics)
            { diagnostics.Add(diagnostic.ToString()); }
            throw new InvalidOperationException(label + " failed emission: " + string.Join("; ", diagnostics));
        }
        var assembly = Assembly.Load(image.ToArray());
        var type = assembly.GetType(ownerName)
            ?? throw new InvalidOperationException(label + " owner was absent at runtime.");
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(label + " method was absent at runtime.");
        var actual = method.Invoke(null, null) as string;
        if (actual != expected)
        { throw new InvalidOperationException(label + " expected " + expected + ", got " + actual); }
    }
}
