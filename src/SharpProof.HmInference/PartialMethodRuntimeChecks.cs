using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class PartialMethodRuntimeChecks
{
    private delegate ref T Reference<T>(ref T value);

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string definitions = """
            // retained definition banner
            #nullable enable
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public partial class PartialProbe{
                /// <summary>Retained definition documentation.</summary>
                public static partial int Echo(int value);
                public static partial string? Nullable(string? value);
                public static partial Task<int> Await(Task<int> value);
                public static partial (int,U) Generic<U>(int value,U tag);
                public static partial int Recursive(int value,int count);
                public static partial ref int Ref(ref int value);
                public static partial IEnumerable<int> Iterator(int value);
                public static int Other(int value)=>value;
            }
            """;
        const string implementations = """
            // retained implementation banner
            #nullable enable
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public partial class PartialProbe{
                // retained implementation comment
                public static partial int Echo(int value)=>value;
                public static partial string? Nullable(string? value)=>value;
                public static async partial Task<int> Await(Task<int> value)=>await value;
                public static partial (int,T) Generic<T>(int value,T tag)=>(value,tag);
                public static partial int Recursive(int value,int count)=>count==0?value:Recursive(value,count-1);
                public static partial ref int Ref(ref int value)=>ref value;
                public static partial IEnumerable<int> Iterator(int value){yield return value;}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "PartialRuntime", "PartialRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var implementation = project.AddDocument("Implementation.cs", implementations);
        var definition = implementation.Project.AddDocument("Definition.cs", definitions);
        implementation = definition.Project.GetDocument(implementation.Id)!;
        var original = (await implementation.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var originalWarnings = original.GetDiagnostics(cancellationToken).Where(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }).Select(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id; }).Order().ToArray();
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(implementation, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        implementation = solution.GetDocument(implementation.Id)!;
        definition = solution.GetDocument(definition.Id)!;
        var definitionText = (await definition.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        var implementationText = (await implementation.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        var compilation = (await implementation.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var diagnostics = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error; }).ToArray();
        if (diagnostics.Any(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; })
            || !diagnostics.Select(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id; }).Order().SequenceEqual(originalWarnings))
        {
            throw new InvalidOperationException("Partial Fix All diagnostics: " + string.Join("\n", diagnostics.Select(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.ToString(); })));
        }
        var probeSymbol = compilation.GetTypeByMetadataName("PartialProbe")!;
        foreach (var method in probeSymbol.GetMembers().OfType<IMethodSymbol>().Where(method =>
            { ArgumentNullException.ThrowIfNull(method); return method.MethodKind == MethodKind.Ordinary; }))
        {
            if (!method.IsGenericMethod || method.PartialImplementationPart is { IsGenericMethod: false })
            { throw new InvalidOperationException("Partial project Fix All missed " + method.Name); }
        }
        var definitionRoot = (await definition.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var implementationRoot = (await implementation.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var genericDefinition = definitionRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
        { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "Generic"; });
        var genericImplementation = implementationRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
        { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "Generic"; });
        if (genericDefinition.TypeParameterList!.Parameters[0].Identifier.ValueText != "U"
            || genericImplementation.TypeParameterList!.Parameters[0].Identifier.ValueText != "T"
            || !definitionText.StartsWith("// retained definition banner", StringComparison.Ordinal)
            || !definitionText.Contains("Retained definition documentation.", StringComparison.Ordinal)
            || !implementationText.StartsWith("// retained implementation banner", StringComparison.Ordinal)
            || !implementationText.Contains("// retained implementation comment", StringComparison.Ordinal))
        { throw new InvalidOperationException("Partial rewrite lost names or trivia."); }
        Program.Log.Info("PASS PartialProjectFixAllBothPartsNamesTriviaAndOrdinaryMethod");

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("HmPartialRuntime", isCollectible: true);
        try
        {
            image.Position = 0;
            var probe = loadContext.LoadFromStream(image).GetType("PartialProbe")!;
            var marker = new Uri("https://example.invalid/partial");
            var other = new Uri("https://example.invalid/other");
            if (!ReferenceEquals(probe.GetMethod("Echo")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [marker]), marker)
                || !ReferenceEquals(probe.GetMethod("Other")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [marker]), marker)
                || !ReferenceEquals(probe.GetMethod("Recursive")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [marker, 3]), marker)
                || probe.GetMethod("Nullable")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [null]) is not null)
            { throw new InvalidOperationException("Partial generic identity, null or recursion changed."); }
            var generic = probe.GetMethod("Generic")!.MakeGenericMethod(typeof(decimal), typeof(Uri)).Invoke(null, [marker, 4m])!;
            if (!ReferenceEquals(generic.GetType().GetField("Item1")!.GetValue(generic), marker)
                || !Equals(generic.GetType().GetField("Item2")!.GetValue(generic), 4m))
            { throw new InvalidOperationException("Partial existing generic parameter mapping changed."); }
            var reference = probe.GetMethod("Ref")!.MakeGenericMethod(typeof(Uri)).CreateDelegate<Reference<Uri>>();
            VerifyReference(reference, marker, other);
            var iterator = (IEnumerable<Uri>)probe.GetMethod("Iterator")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [marker])!;
            if (!ReferenceEquals(iterator.Single(), marker))
            { throw new InvalidOperationException("Partial iterator lost its value."); }
            Program.Log.Info("PASS PartialIdentityRecursionGenericReferenceAndIteratorRuntime");
            var completion = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = (Task<Uri>)probe.GetMethod("Await")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [completion.Task])!;
            if (pending.IsCompleted)
            { throw new InvalidOperationException("Partial async fix failed to suspend."); }
            completion.SetResult(marker);
            if (!ReferenceEquals(await pending.WaitAsync(cancellationToken).ConfigureAwait(false), marker))
            { throw new InvalidOperationException("Partial async result lost identity."); }
            var failure = new InvalidOperationException("partial-await");
            var fault = (Task<Uri>)probe.GetMethod("Await")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [Task.FromException<Uri>(failure)])!;
            try
            { await fault.WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Missing partial fault."); }
            catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure)) { }
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(false);
#pragma warning disable SPINT003 // Deliberately canceled fixture token, independent of test cancellation.
            var cancellation = (Task<Uri>)probe.GetMethod("Await")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [Task.FromCanceled<Uri>(canceled.Token)])!;
#pragma warning restore SPINT003
            try
            { await cancellation.WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Missing partial cancellation."); }
            catch (OperationCanceledException exception) when (exception.CancellationToken == canceled.Token) { }
            Program.Log.Info("PASS PartialAwaitSuspensionFaultAndCancellationRuntime");
            return 3;
        }
        finally { loadContext.Unload(); }
    }

    private static void VerifyReference(Reference<Uri> reference, Uri marker, Uri other)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(other);
        ref var slot = ref reference(ref marker);
        slot = other;
        if (!ReferenceEquals(marker, other))
        { throw new InvalidOperationException("Partial ref return lost its storage alias."); }
    }
}
