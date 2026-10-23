using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class PartialMethodChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Definition, string Implementation)[] cases =
        [
            ("SuppliedCallsAndGroups", "public static partial int Echo(int value);public static int Call()=>Echo(3);public static System.Func<int,int> Capture()=>Echo;", "public static partial int Echo(int value)=>value;"),
            ("Identity", "public static partial int Echo(int value);", "public static partial int Echo(int value)=>value;"),
            ("Nullable", "public static partial string? Echo(string? value);", "public static partial string? Echo(string? value)=>value;"),
            ("Await", "public static partial System.Threading.Tasks.Task<int> Echo(System.Threading.Tasks.Task<int> value);", "public static async partial System.Threading.Tasks.Task<int> Echo(System.Threading.Tasks.Task<int> value)=>await value;"),
            ("Recursive", "public static partial int Echo(int value,int count);", "public static partial int Echo(int value,int count)=>count==0?value:Echo(value,count-1);"),
            ("Instance", "public partial int Echo(int value);", "public partial int Echo(int value)=>value;"),
            ("Reference", "public static partial ref int Echo(ref int value);", "public static partial ref int Echo(ref int value)=>ref value;"),
            ("Iterator", "public static partial System.Collections.Generic.IEnumerable<int> Echo(int value);", "public static partial System.Collections.Generic.IEnumerable<int> Echo(int value){yield return value;}"),
            ("ExistingGenericNames", "public static partial (int,U) Echo<U>(int value,U tag);", "public static partial (int,T) Echo<T>(int value,T tag)=>(value,tag);"),
            ("ExistingConstraint", "public static partial (object[],U) Echo<U>(int value,U tag) where U:class;", "public static partial (object[],T) Echo<T>(int value,T tag) where T:class=>(new object[]{value},tag);"),
            ("Optional", "public static partial int Echo(bool tag,int value=1);", "public static partial int Echo(bool tag,int value=1)=>value;"),
            ("Callback", "public static partial void Echo(int value,System.Action<int> callback);", "public static partial void Echo(int value,System.Action<int> callback){callback(value);}"),
            ("AsyncCallback", "public static partial void Echo(int value,System.Action<int> callback);", "public static async partial void Echo(int value,System.Action<int> callback){await System.Threading.Tasks.Task.Yield();callback(value);}"),
            ("ReservedPartnerName", "public static partial int Echo(int T1);", "public static partial int Echo(int value)=>value;")
        ];
        var failures = new List<string>();
        foreach (var test in cases)
        {
            foreach (var sameFile in new[] { false, true })
            {
                var prefix = "#nullable enable\npublic partial class PartialProbe{";
                var definitions = CSharpSyntaxTree.ParseText(prefix + test.Definition + "}", path: "Definitions.cs", cancellationToken: cancellationToken);
                var implementation = CSharpSyntaxTree.ParseText(prefix + (sameFile ? test.Definition : "") + test.Implementation + "}", path: "Implementation.cs", cancellationToken: cancellationToken);
                var compilation = Program.Compile(sameFile ? [implementation] : [definitions, implementation]);
                var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return Inference.IsCompilationError(diagnostic); }).ToArray();
                if (errors.Length != 0)
                { throw new InvalidOperationException("Invalid partial fixture " + test.Name + ": " + errors[0]); }
                var results = Program.Results(compilation, cancellationToken);
                var suggestions = results.Where(result =>
                { ArgumentNullException.ThrowIfNull(result); return result.Status == "Suggestion"; }).ToArray();
                if (suggestions.Length != 1 || results.Any(result =>
                { ArgumentNullException.ThrowIfNull(result); return result.Status is "Skipped" or "Rejected"; }))
                { failures.Add(test.Name + ": " + string.Join<MethodResult>("; ", results)); continue; }
                Program.Log.Info("PASS Partial" + test.Name + (sameFile ? "SameFile" : "CrossFile"));
            }
        }
        (string Name, string Body, bool WarningsAsErrors)[] builderCases =
        [
            ("Block", "{await new LayoutSignal();return value;}", false),
            ("Expression", "=>await PartialLayoutFactory.From(value);", false),
            ("ExpressionFixed", "=>await new LayoutTask<int>(value);", false),
            ("Throw", "=>throw new System.InvalidOperationException();", false),
            ("Hidden", "\n#line hidden\n{await new LayoutSignal();return value;}\n#line default\n", false),
            ("MarkerCollision", "{var __hm_partial_layout_marker=value;await new LayoutSignal();return __hm_partial_layout_marker;}", false),
            ("WarningsAsErrors", "{await new LayoutSignal();return value;}", true)
        ];
        foreach (var test in builderCases)
        {
            var builderDefinition = CSharpSyntaxTree.ParseText("public partial class PartialBuilderProbe{public static partial LayoutTask<int> Echo(int value,string ignored);}", cancellationToken: cancellationToken);
            var builderImplementation = CSharpSyntaxTree.ParseText("public partial class PartialBuilderProbe{public static async partial LayoutTask<int> Echo(int value,string ignored)" + test.Body + "}", cancellationToken: cancellationToken);
            var builderSupport = CSharpSyntaxTree.ParseText(BuilderLayoutChecks.Support + "public static class PartialLayoutFactory{public static LayoutTask<T> From<T>(T value)=>new(value);}", cancellationToken: cancellationToken);
            var builderCompilation = Program.Compile([builderDefinition, builderImplementation, builderSupport]);
            builderCompilation = builderCompilation.WithOptions(builderCompilation.Options.WithOptimizationLevel(OptimizationLevel.Release)
                .WithGeneralDiagnosticOption(test.WarningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default));
            using var originalImage = new MemoryStream();
            var originalEmission = builderCompilation.Emit(originalImage, cancellationToken: cancellationToken);
            if (!originalEmission.Success)
            { throw new InvalidOperationException("Invalid partial builder fixture " + test.Name + ": " + string.Join("\n", originalEmission.Diagnostics)); }
            var builderResult = Inference.Analyze((await builderImplementation.GetRootAsync(cancellationToken).ConfigureAwait(false)).DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single(),
                builderCompilation.GetSemanticModel(builderImplementation), cancellationToken);
            var expected = test.Name == "ExpressionFixed" ? "global::LayoutTask<int> Echo<T1>(int value, T1 ignored)"
                : test.Name == "Throw" ? "global::LayoutTask<int> Echo<T1, T2>(T1 value, T2 ignored)"
                : "global::LayoutTask<T1> Echo<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged";
            if (builderResult.Signature != expected)
            { failures.Add("Partial unmanaged builder " + test.Name + ": " + builderResult); }
            else
            { Program.Log.Info("PASS PartialCustomBuilderUnmanagedStateMachine" + test.Name); }
        }

        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return cases.Length * 2 + builderCases.Length + await PartialMethodRuntimeChecks.Run(cancellationToken).ConfigureAwait(false)
            + await GeneratedPartnerChecks(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GeneratedPartnerChecks(CancellationToken cancellationToken)
    {
        (string Name, string Definition, string Implementation, string InvalidImplementation, int Arity)[] cases =
        [
            ("Concrete", "public partial int Echo(int value);", "public partial int Echo(int value)=>value;",
                "public partial T Echo<T>(T value)=>value;", 0),
            ("ExistingGeneric", "public partial (T,int) Echo<T>(T value,int tag);", "public partial (T,int) Echo<T>(T value,int tag)=>(value,tag);",
                "public partial (T,T1) Echo<T,T1>(T value,T1 tag)=>(value,tag);", 1)
        ];
        var descriptor = new DiagnosticDescriptor("HM0001", "Generalize", "Generalize", "Design", DiagnosticSeverity.Info, true);
        var provider = new HmSignatureCodeFixProvider();
        foreach (var test in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var workspace = new AdhocWorkspace();
            var references = Program.Compile([]).References;
            var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
                "GeneratedPartial" + test.Name, "GeneratedPartial" + test.Name, LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: references,
                analyzerReferences: [new PartialGeneratorReference(test.Definition)]));
            var source = project.AddDocument("Implementation.cs", "public partial class Probe { " + test.Implementation + " }");
            var compilation = (await source.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var generatedTree = compilation.SyntaxTrees.Single(tree =>
            {
                ArgumentNullException.ThrowIfNull(tree);
                return tree.FilePath.EndsWith("GeneratedPartial.g.cs", StringComparison.Ordinal);
            });
            var generatedDocument = source.Project.GetDocument(generatedTree);
            if (generatedDocument is null || source.Project.DocumentIds.Contains(generatedDocument.Id)
                || compilation.GetDiagnostics(cancellationToken).Any(Inference.IsCompilationError))
            { throw new InvalidOperationException("Invalid source-generated partial fixture " + test.Name); }
            using var originalImage = new MemoryStream();
            if (!compilation.Emit(originalImage, cancellationToken: cancellationToken).Success)
            { throw new InvalidOperationException("Source-generated partial did not emit: " + test.Name); }

            var method = ((CompilationUnitSyntax)(await source.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!)
                .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var result = Inference.Analyze(method, compilation.GetSemanticModel(method.SyntaxTree), cancellationToken);
            if (result.Status != "Suggestion")
            { throw new InvalidOperationException("Source-generated partial candidate was not reproduced: " + test.Name + ": " + result); }
            var invalidTree = CSharpSyntaxTree.ParseText("public partial class Probe { " + test.InvalidImplementation + " }",
                path: "Implementation.cs", cancellationToken: cancellationToken);
            var invalid = compilation.ReplaceSyntaxTree(method.SyntaxTree, invalidTree);
            var invalidIds = invalid.GetDiagnostics(cancellationToken).Where(Inference.IsCompilationError).Select(diagnostic =>
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
                return diagnostic.Id;
            }).ToHashSet(StringComparer.Ordinal);
            if (!invalidIds.Contains("CS8795") || !invalidIds.Contains("CS0759"))
            { throw new InvalidOperationException("Native partial pairing accepted an implementation-only arity change: " + test.Name); }

            var diagnosis = Diagnostic.Create(descriptor, method.Identifier.GetLocation());
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(source, diagnosis, (action, _) =>
            {
                ArgumentNullException.ThrowIfNull(action);
                actions.Add(action);
            }, cancellationToken)).ConfigureAwait(false);
            if (actions.Count != 0)
            { throw new InvalidOperationException("Code fix offered an uneditable source-generated partner change: " + test.Name); }

            var editableProject = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
                "EditablePartial" + test.Name, "EditablePartial" + test.Name, LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: references));
            var editableDefinition = editableProject.AddDocument("EditablePartial.g.cs", "public partial class Probe { " + test.Definition + " }");
            var editableSource = editableDefinition.Project.AddDocument("Implementation.cs", "public partial class Probe { " + test.Implementation + " }");
            var editableMethod = ((CompilationUnitSyntax)(await editableSource.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!)
                .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var editableActions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(editableSource,
                Diagnostic.Create(descriptor, editableMethod.Identifier.GetLocation()), (action, _) =>
                {
                    ArgumentNullException.ThrowIfNull(action);
                    editableActions.Add(action);
                }, cancellationToken)).ConfigureAwait(false);
            if (editableActions.Count != 1)
            { throw new InvalidOperationException("Editable .g.cs partial partner lost its code fix: " + test.Name); }
            var solution = (await editableActions[0].GetOperationsAsync(cancellationToken).ConfigureAwait(false))
                .OfType<ApplyChangesOperation>().Single().ChangedSolution;
            var changed = (await solution.GetDocument(editableSource.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var changedMethod = changed.GetTypeByMetadataName("Probe")!.GetMembers("Echo").OfType<IMethodSymbol>().Single();
            if (changedMethod.Arity != test.Arity + 1 || changedMethod.PartialImplementationPart?.Arity != test.Arity + 1)
            { throw new InvalidOperationException("Editable .g.cs partial pair was not generalized: " + test.Name); }
            using var changedImage = new MemoryStream();
            if (!changed.Emit(changedImage, cancellationToken: cancellationToken).Success)
            { throw new InvalidOperationException("Editable .g.cs partial fix did not emit: " + test.Name); }
            Program.Log.Info("PASS GeneratedPartialPartner" + test.Name + "ImmutableAndEditableControls");
        }
        return cases.Length;
    }

    private sealed class PartialGeneratorReference : AnalyzerReference
    {
        private readonly string definition;

        internal PartialGeneratorReference(string definition)
        { this.definition = definition ?? throw new ArgumentNullException(nameof(definition)); }

        public override string Display => "GeneratedPartialPartner";
        public override string FullPath => string.Empty;
        public override object Id => this;
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language)
        {
            ArgumentNullException.ThrowIfNull(language);
            return [];
        }
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages()
        { return []; }
        public override ImmutableArray<ISourceGenerator> GetGenerators(string language)
        {
            ArgumentNullException.ThrowIfNull(language);
            return [new PartialGenerator(definition)];
        }
    }

#pragma warning disable RS1042 // In-memory generator for an integration fixture, not a packaged generator.
    private sealed class PartialGenerator : ISourceGenerator
    {
        private readonly string definition;

        internal PartialGenerator(string definition)
        { this.definition = definition ?? throw new ArgumentNullException(nameof(definition)); }

        public void Initialize(GeneratorInitializationContext context) { }

        public void Execute(GeneratorExecutionContext /* SPINT001: all values valid - Roslyn supplies initialized contexts for this fixture generator. */ context)
        { context.AddSource("GeneratedPartial.g.cs", "public partial class Probe { " + definition + " }"); }
    }
#pragma warning restore RS1042
}
