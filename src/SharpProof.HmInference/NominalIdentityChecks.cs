using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class NominalIdentityChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string librarySource = """
            #nullable enable
            namespace Collision {
                public interface IContract {}
                public class Item:IContract {}
                public class Box<T> {public T Value=default!;public class Inner<U>{}}
                public class Constrained<T> where T:IContract {}
            }
            """;
        var images = new List<byte[]>();
        MetadataReference Library(string name, string alias)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(alias);
            using var image = new MemoryStream();
            var library = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(librarySource, cancellationToken: cancellationToken)], Program.Compile([]).References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var emitted = library.Emit(image, cancellationToken: cancellationToken);
            if (!emitted.Success)
            { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
            var bytes = image.ToArray();
            images.Add(bytes);
            return MetadataReference.CreateFromImage(bytes).WithAliases([alias]);
        }
        var references = Program.Compile([]).References.Concat([Library("HmLeftIdentity", "Left"), Library("HmRightIdentity", "global")]).ToArray();
        const string sharedAliasSource = "extern alias Left;global using SharedClosed=Left::Collision.Box<int>;";
        var sharedAlias = CSharpSyntaxTree.ParseText(sharedAliasSource, cancellationToken: cancellationToken);
        var aliasUse = CSharpSyntaxTree.ParseText("#nullable enable\npublic static class ImportedAliasProbe{public static SharedClosed Echo(SharedClosed value)=>value;}", cancellationToken: cancellationToken);
        var aliasCompilation = CSharpCompilation.Create("SharedClosedAlias", [sharedAlias, aliasUse], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var aliasErrors = aliasCompilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
        if (aliasErrors.Length != 0)
        { throw new InvalidOperationException("Invalid shared alias fixture: " + aliasErrors[0]); }
        var aliasResult = Program.Results(aliasCompilation, cancellationToken).Single(result =>
        { ArgumentNullException.ThrowIfNull(result); return result.Method == "Echo"; });
        if (aliasResult.Signature != "Left::Collision.Box<T1> Echo<T1>(Left::Collision.Box<T1> value)")
        { throw new InvalidOperationException("Shared closed alias inference: " + (aliasResult.Signature ?? aliasResult.Message)); }
        Program.Log.Info("PASS CrossFileGlobalClosedAliasInference");
        foreach (var alias in new[] { "class", "T1" })
        {
            var renamed = references.Select(reference =>
            { ArgumentNullException.ThrowIfNull(reference); return reference.Properties.Aliases.Contains("Left") ? reference.WithAliases([alias]) : reference; });
            var escaped = alias == "class" ? "@class" : alias;
            var globals = CSharpSyntaxTree.ParseText("extern alias " + escaped + ";global using SharedClosed=" + escaped + "::Collision.Box<int>;", cancellationToken: cancellationToken);
            var named = CSharpCompilation.Create("AliasName" + alias, [globals, aliasUse], renamed, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var result = Program.Results(named, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "Echo"; });
            if (result.Status != "Suggestion")
            { throw new InvalidOperationException("Importable alias name " + alias + ": " + result.Message); }
            Program.Log.Info("PASS GlobalClosedAliasName" + alias);
        }
        const string parameterAliasUse = "public static class ScopedAlias{public static SharedClosed Echo(SharedClosed value,int Left){var copy=Left;return value;}}";
        var parameterAliasTree = CSharpSyntaxTree.ParseText(parameterAliasUse, cancellationToken: cancellationToken);
        var parameterAliasCompilation = CSharpCompilation.Create("ScopedAlias", [sharedAlias, parameterAliasTree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var nativeAliasTree = CSharpSyntaxTree.ParseText("extern alias Left;" + parameterAliasUse.Replace("SharedClosed", "Left::Collision.Box<int>", StringComparison.Ordinal), cancellationToken: cancellationToken);
        var nativeAliasCompilation = CSharpCompilation.Create("NativeScopedAlias", [nativeAliasTree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        if (nativeAliasCompilation.GetDiagnostics(cancellationToken).Any(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }))
        { throw new InvalidOperationException("A native extern alias cannot coexist with a parameter of the same name."); }
        var parameterAliasResult = Program.Results(parameterAliasCompilation, cancellationToken).Single(result =>
        { ArgumentNullException.ThrowIfNull(result); return result.Method == "Echo"; });
        if (parameterAliasResult.Signature != "Left::Collision.Box<T1> Echo<T1, T2>(Left::Collision.Box<T1> value, T2 Left)")
        { throw new InvalidOperationException("Scoped alias name blocked a valid generic signature: " + parameterAliasResult); }
        using var scopedWorkspace = new AdhocWorkspace();
        var scopedProject = scopedWorkspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ScopedAliasFix", "ScopedAliasFix", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: references));
        var scopedGlobals = scopedProject.AddDocument("Globals.cs", sharedAliasSource);
        var scopedDocument = scopedGlobals.Project.AddDocument("ScopedAlias.cs", parameterAliasUse);
        var scopedBefore = (await scopedDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var scopedDiagnostics = await scopedBefore.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false))
            .GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var scopedDiagnostic = scopedDiagnostics.Single(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "HM0001"; });
        var scopedProvider = new HmSignatureCodeFixProvider();
        var scopedActions = new List<CodeAction>();
        await scopedProvider.RegisterCodeFixesAsync(new CodeFixContext(scopedDocument, scopedDiagnostic,
            (action, _) => { ArgumentNullException.ThrowIfNull(action); scopedActions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        var scopedOperations = await scopedActions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        var scopedSolution = scopedOperations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        scopedDocument = scopedSolution.GetDocument(scopedDocument.Id)!;
        var scopedRoot = (CompilationUnitSyntax)(await scopedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var scopedMethod = scopedRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var scopedAfter = (await scopedDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (scopedRoot.Externs.Count != 1 || scopedRoot.Externs[0].Identifier.ValueText != "Left"
            || scopedMethod.ParameterList.Parameters[1].Identifier.ValueText != "Left"
            || scopedMethod.Body!.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single().Initializer?.Value is not IdentifierNameSyntax { Identifier.ValueText: "Left" }
            || (await scopedSolution.GetDocument(scopedGlobals.Id)!.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString() != sharedAliasSource
            || scopedAfter.GetDiagnostics(cancellationToken).Any(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }))
        { throw new InvalidOperationException("Scoped alias code fix did not preserve the parameter and compile with a local extern import."); }
        Program.Log.Info("PASS ScopedExternAliasParameterName");
        var occupiedAliasTree = CSharpSyntaxTree.ParseText("using Left=System.String;public static class OccupiedAlias{public static SharedClosed Echo(SharedClosed value)=>value;}", cancellationToken: cancellationToken);
        var occupiedAliasCompilation = CSharpCompilation.Create("OccupiedAlias", [sharedAlias, occupiedAliasTree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var occupiedAliasResult = Program.Results(occupiedAliasCompilation, cancellationToken).Single(result =>
        { ArgumentNullException.ThrowIfNull(result); return result.Method == "Echo"; });
        if (occupiedAliasResult.Status == "Suggestion")
        { throw new InvalidOperationException("A conflicting using alias was treated as a safe extern import."); }
        Program.Log.Info("PASS ConflictingUsingAliasGuard");
        foreach (var guarded in new[]
        {
            "using System.Runtime.CompilerServices;public static class GuardedAlias{static int Save([CallerLineNumber]int line=0)=>line;public static SharedClosed Echo(SharedClosed value){Save();return value;}}"
        })
        {
            var guardedTree = CSharpSyntaxTree.ParseText(guarded, cancellationToken: cancellationToken);
            var guardedCompilation = CSharpCompilation.Create("GuardedAlias", [sharedAlias, guardedTree], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var result = Program.Results(guardedCompilation, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "Echo"; });
            if (result.Status == "Suggestion")
            { throw new InvalidOperationException("Alias import changed an occupied name or caller-info constant: " + result.Signature); }
        }
        Program.Log.Info("PASS AliasImportNameAndCallerInfoProtection");


        const string source = """
            #nullable enable
            extern alias Left;
            using System.Threading.Tasks;
            using Closed=Left::Collision.Box<int>;
            public static class NominalProbe {
                public static Left::Collision.Box<int> Identity(Left::Collision.Box<int> value)=>value;
                public static Closed ClosedAlias(Closed value)=>value;
                public static object Narrow(Left::Collision.Box<int> value)=>value;
                public static async Task<object?> AwaitNarrow(Task<Left::Collision.Box<int>?> value)=>await value;
                public static (Left::Collision.Box<int>,Collision.Box<string>) Pair(Left::Collision.Box<int> left,Collision.Box<string> right)=>(left,right);
                public static Left::Collision.Box<int>? Nullable(Left::Collision.Box<int>? value)=>value;
                public static async Task<Left::Collision.Box<int>?> Await(Task<Left::Collision.Box<int>?> value)=>await value;
                public static Left::Collision.Box<int>.Inner<string> Nested(Left::Collision.Box<int>.Inner<string> value)=>value;
                public static Left::Collision.Constrained<Left::Collision.Item> Constraint(Left::Collision.Constrained<Left::Collision.Item> value)=>value;
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "NominalIdentity", "NominalIdentity", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: references));
        var document = project.AddDocument("NominalIdentity.cs", source);
        var originalCompilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var originalProbe = originalCompilation.GetTypeByMetadataName("NominalProbe")!;
        var expectedFixes = originalProbe.GetMembers().OfType<IMethodSymbol>().Count(method =>
            { ArgumentNullException.ThrowIfNull(method); return method.MethodKind == MethodKind.Ordinary; });
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < expectedFixes; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var results = Program.Results((CSharpCompilation)compilation, cancellationToken).ToArray();
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Nominal analyzer crashed."); }
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001"; });
            if (diagnostic is null)
            { throw new InvalidOperationException("Missing nominal fix at round " + round + ": " + string.Join("; ", results.Select(r => { ArgumentNullException.ThrowIfNull(r); return r.ToString(); }))); }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        var changed = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var errors = changed.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (errors.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", errors.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        var probe = changed.GetTypeByMetadataName("NominalProbe")!;
        foreach (var method in probe.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.Name is "Narrow" or "AwaitNarrow")
            {
                var retained = method.Name == "Narrow" ? method.ReturnType : ((INamedTypeSymbol)method.ReturnType).TypeArguments[0];
                var originalMember = originalProbe.GetMembers(method.Name).OfType<IMethodSymbol>().Single();
                var beforeInput = originalMember.Parameters[0].Type;
                var afterInput = method.Parameters[0].Type;
                var beforeResult = originalMember.ReturnType;
                if (method.Name == "AwaitNarrow")
                {
                    beforeInput = ((INamedTypeSymbol)beforeInput).TypeArguments[0];
                    afterInput = ((INamedTypeSymbol)afterInput).TypeArguments[0];
                    beforeResult = ((INamedTypeSymbol)beforeResult).TypeArguments[0];
                }
                if (method.TypeParameters.Length != 1 || retained.SpecialType != SpecialType.System_Object
                    || retained.NullableAnnotation != beforeResult.NullableAnnotation || afterInput.NullableAnnotation != beforeInput.NullableAnnotation
                    || beforeInput is not INamedTypeSymbol beforeNamed || afterInput is not INamedTypeSymbol afterNamed
                    || !beforeNamed.ContainingAssembly.Identity.Equals(afterNamed.ContainingAssembly.Identity)
                    || beforeNamed.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != afterNamed.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                { throw new InvalidOperationException("Return widening was lost: " + method.Name); }
                Program.Log.Info("PASS NominalPreservedConversion" + method.Name);
                continue;
            }
            if (method.TypeParameters.Length == 0)
            { throw new InvalidOperationException("Missing inferred parameters for " + method.Name); }
            var returnType = method.ReturnType;
            if (method.Name == "Await")
            { returnType = ((INamedTypeSymbol)returnType).TypeArguments[0]; }
            if (method.Name == "Pair")
            {
                var pair = (INamedTypeSymbol)returnType;
                if (pair.TupleElements[0].Type.ContainingAssembly.Name != "HmLeftIdentity" || pair.TupleElements[1].Type.ContainingAssembly.Name != "HmRightIdentity")
                { throw new InvalidOperationException("Aliased pair constructor identity changed."); }
            }
            else if (returnType.ContainingAssembly.Name != "HmLeftIdentity")
            { throw new InvalidOperationException("Aliased return constructor identity changed: " + method.Name); }
            if (method.Name == "Constraint" && method.TypeParameters[0].ConstraintTypes.Single().ContainingAssembly.Name != "HmLeftIdentity")
            { throw new InvalidOperationException("Aliased constraint identity changed."); }
            Program.Log.Info("PASS NominalAliasFix" + method.Name);
        }
        // New domains must compile against the applied fixes, retaining both assemblies.
        document = document.Project.AddDocument("NominalRunner.cs", """
            #nullable enable
            extern alias Left;
            using System;
            using System.Threading.Tasks;
            public static class NominalRunner {
                public static async Task<bool> Run(){
                    var sentinel=new Uri("https://example.invalid/nominal");
                    var box=new Left::Collision.Box<Uri>{Value=sentinel};
                    var other=new Collision.Box<Guid>{Value=Guid.NewGuid()};
                    if(!ReferenceEquals(NominalProbe.Identity(box),box)||!ReferenceEquals(NominalProbe.ClosedAlias(box),box)||NominalProbe.Nullable<Uri>(null)!=null)return false;
                    var pair=NominalProbe.Pair(box,other);if(!ReferenceEquals(pair.Item1,box)||!ReferenceEquals(pair.Item2,other))return false;
                    if(!ReferenceEquals(await NominalProbe.Await(Task.FromResult<Left::Collision.Box<Uri>?>(box)),box))return false;
                    var completion=new TaskCompletionSource<Left::Collision.Box<Uri>?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pending=NominalProbe.Await(completion.Task);if(pending.IsCompleted)return false;
                    completion.SetResult(box);if(!ReferenceEquals(await pending,box))return false;
                    var wideBox=new Left::Collision.Box<int>{Value=42};if(!ReferenceEquals(NominalProbe.Narrow(wideBox),wideBox))return false;
                    var wideCompletion=new TaskCompletionSource<Left::Collision.Box<int>?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var widePending=NominalProbe.AwaitNarrow(wideCompletion.Task);if(widePending.IsCompleted)return false;
                    wideCompletion.SetResult(wideBox);if(!ReferenceEquals(await widePending,wideBox))return false;
                    return await NominalProbe.Await(Task.FromResult<Left::Collision.Box<Uri>?>(null))==null;
                }
            }
            """);
        var runtime = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        using var runtimeImage = new MemoryStream();
        var runtimeEmit = runtime.Emit(runtimeImage, cancellationToken: cancellationToken);
        if (!runtimeEmit.Success)
        { throw new InvalidOperationException(string.Join("\n", runtimeEmit.Diagnostics)); }
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("HmNominalRuntime", isCollectible: true);
        try
        {
            foreach (var bytes in images)
            {
                using var dependency = new MemoryStream(bytes);
                loadContext.LoadFromStream(dependency);
            }
            runtimeImage.Position = 0;
            var loaded = loadContext.LoadFromStream(runtimeImage);
            var runtimeCheck = (Task<bool>)loaded.GetType("NominalRunner")!.GetMethod("Run")!.Invoke(null, null)!;
            if (!await runtimeCheck.ConfigureAwait(false))
            { throw new InvalidOperationException("Aliased nullable/async runtime behavior changed."); }
        }
        finally { loadContext.Unload(); }
        Program.Log.Info("PASS NominalAliasNullableAsyncRuntime");
        const string shadow = "class Outer<T>{public class Inner<T>{public static Inner<T> M(Inner<T> value,bool ignored)=>value;}}";
        var shadowCompilation = Program.Compile([CSharpSyntaxTree.ParseText(shadow, cancellationToken: cancellationToken)]);
        var shadowResult = Program.Results(shadowCompilation, cancellationToken).Single();
        if (shadowResult.Signature != "Inner<T> M<T1>(Inner<T> value, T1 ignored)")
        { throw new InvalidOperationException("Shadowed owner identity: " + shadowResult); }
        Program.Log.Info("PASS ShadowedEnclosingGenericNames");
        return 17 + await SharedAliasRuntime(references, images, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> SharedAliasRuntime(IEnumerable<MetadataReference> references, IEnumerable<byte[]> images, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(images);
        const string imports = """
            extern alias Left;
            global using SharedClosed=Left::Collision.Box<int>;
            global using SharedNested=Left::Collision.Box<int>.Inner<string>;
            global using SharedConstraint=Left::Collision.Constrained<Left::Collision.Item>;
            """;
        const string source = """
            // retained file header
            #nullable enable
            using System.Threading.Tasks;
            using System.Runtime.CompilerServices;
            /// <summary>Retained alias owner.</summary>
            public static class ImportedAliasProbe {
                // retained method comment
                public static string Format(SharedClosed value,int tag)=>Capture(value.Value+1);
                private static string Capture(int value,[CallerArgumentExpression("value")]string expression="")=>expression;
                public static SharedClosed Echo(SharedClosed value)=>value;
                public static SharedClosed Again(SharedClosed value)=>value;
                public static SharedClosed? Nullable(SharedClosed? value)=>value;
                public static async Task<SharedClosed> Await(Task<SharedClosed> value)=>await value;
                public static SharedNested Nested(SharedNested value)=>value;
                public static SharedConstraint Constraint(SharedConstraint value)=>value;
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "SharedAliasRuntime", "SharedAliasRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: references));
        var importsDocument = project.AddDocument("Globals.cs", imports);
        var document = importsDocument.Project.AddDocument("AliasUse.cs", source);
        var before = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var oldDocumentation = before.GetTypeByMetadataName("ImportedAliasProbe")!.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document, "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var operations = await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        document = solution.GetDocument(document.Id)!;
        var text = (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        if (root.Externs.Count != 1 || root.Externs[0].Identifier.ValueText != "Left"
            || root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count(method =>
            { ArgumentNullException.ThrowIfNull(method); return method.TypeParameterList is not null; }) != 8
            || !text.StartsWith("// retained file header", StringComparison.Ordinal)
            || !text.Contains("// retained method comment", StringComparison.Ordinal)
            || (await solution.GetDocument(importsDocument.Id)!.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString() != imports)
        { throw new InvalidOperationException("Alias import Fix All lost a fix/trivia, duplicated an import or changed the alias declaration."); }
        var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (compilation.GetTypeByMetadataName("ImportedAliasProbe")!.GetDocumentationCommentXml(cancellationToken: cancellationToken) != oldDocumentation)
        { throw new InvalidOperationException("Adding an extern alias changed class documentation."); }
        var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException(string.Join("\n", errors.Select(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.ToString(); })));
        }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("SharedAliasRuntime", isCollectible: true);
        try
        {
            foreach (var bytes in images)
            {
                using var dependency = new MemoryStream(bytes);
                loadContext.LoadFromStream(dependency);
            }
            image.Position = 0;
            var assembly = loadContext.LoadFromStream(image);
            var probe = assembly.GetType("ImportedAliasProbe")!;
            var left = loadContext.Assemblies.Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.GetName().Name == "HmLeftIdentity"; });
            var boxType = left.GetType("Collision.Box" + (char)96 + "1")!.MakeGenericType(typeof(Uri));
            var box = Activator.CreateInstance(boxType)!;
            var marker = new Uri("https://example.invalid/shared-alias");
            boxType.GetField("Value")!.SetValue(box, marker);
            if (!ReferenceEquals(probe.GetMethod("Echo")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [box]), box)
                || !ReferenceEquals(probe.GetMethod("Again")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [box]), box)
                || probe.GetMethod("Nullable")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [null]) is not null)
            { throw new InvalidOperationException("Shared alias fixes changed assembly identity or reference values."); }
            var nestedType = left.GetType("Collision.Box" + (char)96 + "1+Inner" + (char)96 + "1")!.MakeGenericType(typeof(Uri), typeof(decimal));
            var nested = Activator.CreateInstance(nestedType)!;
            if (!ReferenceEquals(probe.GetMethod("Nested")!.MakeGenericMethod(typeof(Uri), typeof(decimal)).Invoke(null, [nested]), nested))
            { throw new InvalidOperationException("Shared nested alias fix lost its generic owner."); }
            var intBoxType = left.GetType("Collision.Box" + (char)96 + "1")!.MakeGenericType(typeof(int));
            var intBox = Activator.CreateInstance(intBoxType)!;
            intBoxType.GetField("Value")!.SetValue(intBox, 42);
            var argumentText = probe.GetMethod("Format")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [intBox, marker]);
            if (!Equals(argumentText, "value.Value+1"))
            { throw new InvalidOperationException("IDE formatting changed a caller-argument-expression value: " + argumentText); }
            var completionType = typeof(TaskCompletionSource<>).MakeGenericType(boxType);
            var completion = Activator.CreateInstance(completionType, [TaskCreationOptions.RunContinuationsAsynchronously])!;
            var task = completionType.GetProperty("Task")!.GetValue(completion)!;
            var pending = (Task)probe.GetMethod("Await")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [task])!;
            if (pending.IsCompleted)
            { throw new InvalidOperationException("Shared alias await completed before its operand."); }
            completionType.GetMethod("SetResult")!.Invoke(completion, [box]);
            await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!ReferenceEquals(pending.GetType().GetProperty("Result")!.GetValue(pending), box)
                || probe.GetMethod("Constraint")!.GetGenericArguments()[0].GetGenericParameterConstraints().Single().Assembly != left)
            { throw new InvalidOperationException("Shared alias fix changed an await result or foreign constraint identity."); }
        }
        finally { loadContext.Unload(); }
        Program.Log.Info("PASS CrossFileGlobalAliasImportFixAllTriviaAndIdentity");
        Program.Log.Info("PASS CrossFileGlobalAliasNullableAwaitAndNestedRuntime");
        return 2;
    }

}
