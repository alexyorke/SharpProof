using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class CodeFixChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        using var exports = new System.Composition.Hosting.ContainerConfiguration().WithAssembly(typeof(HmSignatureCodeFixProvider).Assembly).CreateContainer();
        if (exports.GetExports<CodeFixProvider>().Single().GetType() != typeof(HmSignatureCodeFixProvider))
        { throw new InvalidOperationException("MEF code-fix discovery failed."); }
        Program.Log.Info("PASS IDEMefCodeFixDiscovery");
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "FixChecks", "FixChecks", LanguageNames.CSharp, compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), parseOptions: CSharpParseOptions.Default, metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Input.cs", """
            // Keep the public contract comment.
            class C {
                // Identity body must survive.
                public static int Id(int value) => value;
                static int Other(int value) { var copy=value; return copy; }
                static int Concrete(int value) => value+1;
            }
            """);
        var diagnostics = await (Diagnostics(document, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var originalRoot = (await (document.GetSyntaxRootAsync(cancellationToken: cancellationToken)).ConfigureAwait(false))!;
        var idDiagnostic = diagnostics.Single(d =>
        {
            global::System.ArgumentNullException.ThrowIfNull(d);
            return d.Location.SourceSpan == originalRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m =>
            {
                global::System.ArgumentNullException.ThrowIfNull(m);
                return m.Identifier.ValueText == "Id";
            }).Identifier.Span;
        });
        var provider = new HmSignatureCodeFixProvider();
        var actions = new List<CodeAction>();
        await (provider.RegisterCodeFixesAsync(new CodeFixContext(document, idDiagnostic, (action, _) =>
        {
            global::System.ArgumentNullException.ThrowIfNull(action);
            actions.Add(action);
        }, cancellationToken))).ConfigureAwait(false);
        if (actions.Count != 1)
        {
            throw new InvalidOperationException("Expected one HM code fix.");
        }

        var solution = await (ChangedSolution(actions.Single(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        var changed = solution.GetDocument(document.Id)!;
        await (CheckCompilation(changed, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var text = (await (changed.GetTextAsync(cancellationToken: cancellationToken)).ConfigureAwait(false)).ToString();
        if (!text.Contains("Id<T1>", StringComparison.Ordinal) || !text.Contains("Identity body must survive.", StringComparison.Ordinal) || !text.Contains("=> value;", StringComparison.Ordinal) || !text.Contains("Other(int value)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Code fix lost body/trivia or changed an unrelated method.");
        }

        Program.Log.Info("PASS IDECodeFixApplication");
        var concrete = document.WithText(Microsoft.CodeAnalysis.Text.SourceText.From((await (document.GetTextAsync(cancellationToken: cancellationToken)).ConfigureAwait(false)).ToString().Replace("=> value;", "=> value+1;", StringComparison.Ordinal)));
        actions.Clear();
        await (provider.RegisterCodeFixesAsync(new CodeFixContext(concrete, idDiagnostic, (action, _) =>
        {
            global::System.ArgumentNullException.ThrowIfNull(action);
            actions.Add(action);
        }, cancellationToken))).ConfigureAwait(false);
        if (actions.Count != 0)
        {
            throw new InvalidOperationException("Stale diagnostic produced an unsafe fix.");
        }

        Program.Log.Info("PASS IDEStaleDiagnostic");
        var context = new FixAllContext(document, provider, FixAllScope.Document, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new DiagnosticSource(), cancellationToken);
        var allAction = await (provider.GetFixAllProvider().GetFixAsync(context)).ConfigureAwait(false);
        changed = (await (ChangedSolution(allAction!, cancellationToken: cancellationToken)).ConfigureAwait(false)).GetDocument(document.Id)!;
        await (CheckCompilation(changed, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var methods = (await (changed.GetSyntaxRootAsync(cancellationToken: cancellationToken)).ConfigureAwait(false))!.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        if (methods.Count(m => m.TypeParameterList is not null) != 2 || methods.Single(m =>
        {
            global::System.ArgumentNullException.ThrowIfNull(m);
            return m.Identifier.ValueText == "Concrete";
        }).TypeParameterList is not null)
        {
            throw new InvalidOperationException("Fix All failed to generalize just the eligible methods.");
        }

        Program.Log.Info("PASS IDESequentialFixAll");
        var solutionProject = document.Project;
        var another = solutionProject.AddDocument("Second.cs", "class D { static int Second(int x) => x; }");
        var projectContext = new FixAllContext(another.Project, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new DiagnosticSource(), cancellationToken);
        var projectFix = await (provider.GetFixAllProvider().GetFixAsync(projectContext)).ConfigureAwait(false);
        var fixedProject = (await (ChangedSolution(projectFix!, cancellationToken: cancellationToken)).ConfigureAwait(false)).GetProject(project.Id)!;
        foreach (var fixedDocument in fixedProject.Documents)
        {
            await (CheckCompilation(fixedDocument, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        if ((await (Diagnostics(fixedProject.Documents.Single(d =>
        {
            global::System.ArgumentNullException.ThrowIfNull(d);
            return d.Name == "Second.cs";
        }), cancellationToken: cancellationToken)).ConfigureAwait(false)).Length != 0)
        {
            throw new InvalidOperationException("Project Fix All missed the second document.");
        }

        Program.Log.Info("PASS IDEProjectFixAll");
        var overloads = document.WithText(Microsoft.CodeAnalysis.Text.SourceText.From("class C { static int Same(int x)=>x; static string Same(string x)=>x; }"));
        var overloadContext = new FixAllContext(overloads, provider, FixAllScope.Document, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new DiagnosticSource(), cancellationToken);
        var overloadAction = await (provider.GetFixAllProvider().GetFixAsync(overloadContext)).ConfigureAwait(false);
        changed = (await (ChangedSolution(overloadAction!, cancellationToken: cancellationToken)).ConfigureAwait(false)).GetDocument(overloads.Id)!;
        await (CheckCompilation(changed, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if ((await (changed.GetSyntaxRootAsync(cancellationToken: cancellationToken)).ConfigureAwait(false))!.DescendantNodes().OfType<MethodDeclarationSyntax>().Count(m => m.TypeParameterList is not null) != 1)
        {
            throw new InvalidOperationException("Fix All should reject the second conflicting generic overload.");
        }

        Program.Log.Info("PASS IDEFixAllOverloadConflict");
        var deleted = document.WithText(Microsoft.CodeAnalysis.Text.SourceText.From("class C {}"));
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(deleted, idDiagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 0)
        { throw new InvalidOperationException("Deleted method retained a code fix."); }
        Program.Log.Info("PASS IDEDeletedMethodDiagnostic");

        var invalid = document.WithText(Microsoft.CodeAnalysis.Text.SourceText.From("class C { static int Id(int value) => missing; }"));
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(invalid, idDiagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 0)
        { throw new InvalidOperationException("Invalid compilation retained a code fix."); }
        Program.Log.Info("PASS IDEInvalidCompilationDiagnostic");

        var declaration = document.WithText(Microsoft.CodeAnalysis.Text.SourceText.From("""
            class G<T1> {
                [System.Obsolete("retained")]
                static T M<T /* generic */>(T value, /* separator */ [System.Runtime.InteropServices.In] ref int ignored, bool optional=true) where T:class => value;
            }
            """));
        var declarationDiagnostic = (await Diagnostics(declaration, cancellationToken).ConfigureAwait(false)).Single();
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(declaration, declarationDiagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        changed = (await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false)).GetDocument(document.Id)!;
        await CheckCompilation(changed, cancellationToken).ConfigureAwait(false);
        text = (await changed.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        foreach (var retained in new[] { "/* generic */", "/* separator */", "ref T2 ignored", "where T : class", "optional = true", "Obsolete", "InteropServices.In" })
        {
            if (!text.Contains(retained, StringComparison.Ordinal))
            { throw new InvalidOperationException("Code fix lost declaration element: " + retained); }
        }
        Program.Log.Info("PASS IDEGenericDeclarationTrivia");

        var secondProjectId = ProjectId.CreateNewId();
        var secondDocumentId = DocumentId.CreateNewId(secondProjectId);
        var multiSolution = another.Project.Solution.AddProject(secondProjectId, "OtherProject", "OtherProject", LanguageNames.CSharp)
            .WithProjectCompilationOptions(secondProjectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(secondProjectId, Program.Compile([]).References)
            .AddDocument(secondDocumentId, "Other.cs", "class E { static int Third(int x) => x; }");
        var solutionContext = new FixAllContext(multiSolution.GetProject(project.Id)!, provider, FixAllScope.Solution, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new DiagnosticSource(), cancellationToken);
        var solutionAction = await provider.GetFixAllProvider().GetFixAsync(solutionContext).ConfigureAwait(false);
        var fixedSolution = await ChangedSolution(solutionAction!, cancellationToken).ConfigureAwait(false);
        foreach (var fixedDocument in fixedSolution.Projects.SelectMany(p => { ArgumentNullException.ThrowIfNull(p); return p.Documents; }))
        {
            await CheckCompilation(fixedDocument, cancellationToken).ConfigureAwait(false);
            if ((await Diagnostics(fixedDocument, cancellationToken).ConfigureAwait(false)).Length != 0)
            { throw new InvalidOperationException("Solution Fix All missed an eligible method."); }
        }
        Program.Log.Info("PASS IDESolutionFixAll");

        const string linkedSource = "public static partial class Linked { public static int Identity(int value) => value; }";
        var linkedFirstId = ProjectId.CreateNewId();
        var linkedSecondId = ProjectId.CreateNewId();
        var linkedFirstDocumentId = DocumentId.CreateNewId(linkedFirstId);
        var linkedSecondDocumentId = DocumentId.CreateNewId(linkedSecondId);
        var linkedSolution = workspace.CurrentSolution
            .AddProject(linkedFirstId, "LinkedFirst", "LinkedFirst", LanguageNames.CSharp)
            .WithProjectCompilationOptions(linkedFirstId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(linkedFirstId, Program.Compile([]).References)
            .AddProject(linkedSecondId, "LinkedSecond", "LinkedSecond", LanguageNames.CSharp)
            .WithProjectCompilationOptions(linkedSecondId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(linkedSecondId, Program.Compile([]).References)
            .AddDocument(linkedFirstDocumentId, "Linked.cs", linkedSource, filePath: "/shared/Linked.cs")
            .AddDocument(linkedSecondDocumentId, "Linked.cs", linkedSource, filePath: "/shared/Linked.cs");
        var linkedFirst = linkedSolution.GetDocument(linkedFirstDocumentId)!;
        var linkedDiagnostic = (await Diagnostics(linkedFirst, cancellationToken).ConfigureAwait(false)).Single();
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(linkedFirst, linkedDiagnostic, (action, _) =>
        { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        var linkedChanged = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        var linkedTexts = new List<string>();
        foreach (var linkedId in new[] { linkedFirstDocumentId, linkedSecondDocumentId })
        {
            var linkedDocument = linkedChanged.GetDocument(linkedId)!;
            await CheckCompilation(linkedDocument, cancellationToken).ConfigureAwait(false);
            linkedTexts.Add((await linkedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString());
        }
        if (linkedTexts[0] != linkedTexts[1] || !linkedTexts[0].Contains("Identity<T1>", StringComparison.Ordinal))
        { throw new InvalidOperationException("Code fix did not update both linked documents."); }
        Program.Log.Info("PASS IDELinkedDocumentCodeFix");

        var dependentId = ProjectId.CreateNewId();
        var dependentDocumentId = DocumentId.CreateNewId(dependentId);
        var transitiveId = ProjectId.CreateNewId();
        var transitiveDocumentId = DocumentId.CreateNewId(transitiveId);
        var dependentSolution = linkedSolution
            .AddProject(dependentId, "LinkedDependent", "LinkedDependent", LanguageNames.CSharp)
            .WithProjectCompilationOptions(dependentId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(dependentId, Program.Compile([]).References)
            .AddProjectReference(dependentId, new ProjectReference(linkedSecondId))
            .AddDocument(dependentDocumentId, "Dependent.cs",
                "public static class Dependent { public static int Call() => Linked.Identity(default); }")
            .AddProject(transitiveId, "LinkedTransitive", "LinkedTransitive", LanguageNames.CSharp)
            .WithProjectCompilationOptions(transitiveId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(transitiveId, Program.Compile([]).References)
            .AddProjectReference(transitiveId, new ProjectReference(dependentId))
            .AddDocument(transitiveDocumentId, "Transitive.cs",
                "public static class Transitive { public static int Call() => Dependent.Call(); }");
        await CheckCompilation(dependentSolution.GetDocument(dependentDocumentId)!, cancellationToken).ConfigureAwait(false);
        await CheckCompilation(dependentSolution.GetDocument(transitiveDocumentId)!, cancellationToken).ConfigureAwait(false);
        var invalidDependentSolution = dependentSolution
            .WithDocumentText(linkedFirstDocumentId, Microsoft.CodeAnalysis.Text.SourceText.From(linkedTexts[0]))
            .WithDocumentText(linkedSecondDocumentId, Microsoft.CodeAnalysis.Text.SourceText.From(linkedTexts[0]));
        var invalidDependentCompilation = (await invalidDependentSolution.GetProject(dependentId)!.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (!invalidDependentCompilation.GetDiagnostics(cancellationToken).Any(d =>
        { ArgumentNullException.ThrowIfNull(d); return d.Id == "CS0411"; }))
        { throw new InvalidOperationException("Dependent fixture did not prove native CS0411."); }
        var dependentFirst = dependentSolution.GetDocument(linkedFirstDocumentId)!;
        var dependentDiagnostic = (await Diagnostics(dependentFirst, cancellationToken).ConfigureAwait(false)).Single();
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(dependentFirst, dependentDiagnostic, (action, _) =>
        { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        var dependentChanged = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        foreach (var linkedId in new[] { linkedFirstDocumentId, linkedSecondDocumentId })
        {
            var dependentLinkedText = (await dependentChanged.GetDocument(linkedId)!.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            if (dependentLinkedText != linkedSource)
            { throw new InvalidOperationException("Dependent compiler error produced a partial linked code fix."); }
        }
        await CheckCompilation(dependentChanged.GetDocument(dependentDocumentId)!, cancellationToken).ConfigureAwait(false);
        await CheckCompilation(dependentChanged.GetDocument(transitiveDocumentId)!, cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS IDELinkedDependentRollback");

        var validDependentSolution = dependentSolution.WithDocumentText(dependentDocumentId,
            Microsoft.CodeAnalysis.Text.SourceText.From("public static class Dependent { public static int Call() => Linked.Identity(42); }"));
        var validFirst = validDependentSolution.GetDocument(linkedFirstDocumentId)!;
        var validDiagnostic = (await Diagnostics(validFirst, cancellationToken).ConfigureAwait(false)).Single();
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(validFirst, validDiagnostic, (action, _) =>
        { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        var validChanged = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        foreach (var linkedId in new[] { linkedFirstDocumentId, linkedSecondDocumentId })
        {
            var validLinkedText = (await validChanged.GetDocument(linkedId)!.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            if (validLinkedText != linkedTexts[0])
            { throw new InvalidOperationException("Valid dependent blocked linked code fix."); }
        }
        await CheckCompilation(validChanged.GetDocument(dependentDocumentId)!, cancellationToken).ConfigureAwait(false);
        await CheckCompilation(validChanged.GetDocument(transitiveDocumentId)!, cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS IDELinkedValidDependent");

        var existingErrorId = DocumentId.CreateNewId(dependentId);
        var existingErrorSolution = validDependentSolution.AddDocument(existingErrorId, "ExistingError.cs",
            "class ExistingError { void M() { Missing(); } }");
        var existingBefore = (await existingErrorSolution.GetProject(dependentId)!.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!
            .GetDiagnostics(cancellationToken).Where(Inference.IsCompilationError).Select(d =>
            { ArgumentNullException.ThrowIfNull(d); return d.Id; }).Order().ToArray();
        if (!existingBefore.SequenceEqual(["CS0103"]))
        { throw new InvalidOperationException("Preexisting-error fixture has unexpected diagnostics."); }
        var existingFirst = existingErrorSolution.GetDocument(linkedFirstDocumentId)!;
        var existingDiagnostic = (await Diagnostics(existingFirst, cancellationToken).ConfigureAwait(false)).Single();
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(existingFirst, existingDiagnostic, (action, _) =>
        { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        var existingChanged = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        var existingAfter = (await existingChanged.GetProject(dependentId)!.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!
            .GetDiagnostics(cancellationToken).Where(Inference.IsCompilationError).Select(d =>
            { ArgumentNullException.ThrowIfNull(d); return d.Id; }).Order().ToArray();
        var existingLinkedText = (await existingChanged.GetDocument(linkedSecondDocumentId)!.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        if (existingLinkedText != linkedTexts[0] || !existingAfter.SequenceEqual(existingBefore))
        { throw new InvalidOperationException("Unrelated dependent error blocked or changed linked code fix."); }
        Program.Log.Info("PASS IDELinkedPreexistingDependentError");

        var collisionId = DocumentId.CreateNewId(linkedSecondId);
        var collisionSolution = linkedSolution.AddDocument(collisionId, "Collision.cs",
            "public static partial class Linked { public static T Identity<T>(T value) => value; }");
        await CheckCompilation(collisionSolution.GetDocument(collisionId)!, cancellationToken).ConfigureAwait(false);
        var invalidCollision = collisionSolution.WithDocumentText(linkedSecondDocumentId,
            Microsoft.CodeAnalysis.Text.SourceText.From(linkedTexts[0]));
        var invalidCompilation = (await invalidCollision.GetProject(linkedSecondId)!.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (!invalidCompilation.GetDiagnostics(cancellationToken).Any(d =>
        { ArgumentNullException.ThrowIfNull(d); return d.Id == "CS0111"; }))
        { throw new InvalidOperationException("Linked overload fixture did not prove a native compiler conflict."); }
        var collisionFirst = collisionSolution.GetDocument(linkedFirstDocumentId)!;
        var collisionDiagnostic = (await Diagnostics(collisionFirst, cancellationToken).ConfigureAwait(false)).Single();
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(collisionFirst, collisionDiagnostic, (action, _) =>
        { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        var collisionChanged = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        foreach (var linkedId in new[] { linkedFirstDocumentId, linkedSecondDocumentId })
        {
            var collisionText = (await collisionChanged.GetDocument(linkedId)!.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            if (collisionText != linkedSource)
            { throw new InvalidOperationException("Linked project conflict produced a partial code fix."); }
        }
        Program.Log.Info("PASS IDELinkedDocumentCollisionRollback");

        const string divergentSource = """
            public static class ConditionalLinked
            {
            #if ALT
                public static int Identity(int value) => value + 1;
            #else
                public static int Identity(int value) => value;
            #endif
            }
            """;
        var divergentSolution = linkedSolution
            .WithDocumentText(linkedFirstDocumentId, Microsoft.CodeAnalysis.Text.SourceText.From(divergentSource))
            .WithDocumentText(linkedSecondDocumentId, Microsoft.CodeAnalysis.Text.SourceText.From(divergentSource))
            .WithProjectParseOptions(linkedSecondId, CSharpParseOptions.Default.WithPreprocessorSymbols("ALT"));
        await CheckCompilation(divergentSolution.GetDocument(linkedFirstDocumentId)!, cancellationToken).ConfigureAwait(false);
        await CheckCompilation(divergentSolution.GetDocument(linkedSecondDocumentId)!, cancellationToken).ConfigureAwait(false);
        var divergentFirst = divergentSolution.GetDocument(linkedFirstDocumentId)!;
        var divergentDiagnostic = (await Diagnostics(divergentFirst, cancellationToken).ConfigureAwait(false)).Single();
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(divergentFirst, divergentDiagnostic, (action, _) =>
        { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        var divergentChanged = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        foreach (var linkedId in new[] { linkedFirstDocumentId, linkedSecondDocumentId })
        {
            var divergentText = (await divergentChanged.GetDocument(linkedId)!.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            if (divergentText != divergentSource)
            { throw new InvalidOperationException("Divergent linked parse context produced a partial code fix."); }
        }
        Program.Log.Info("PASS IDELinkedDocumentDivergentContextRollback");

        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cancelled.CancelAsync().ConfigureAwait(false);
        try
        {
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, idDiagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancelled.Token)).ConfigureAwait(false);
            throw new InvalidOperationException("Code fix ignored cancellation.");
        }
        catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        Program.Log.Info("PASS IDECodeFixCancellation");
        return 17;
    }

    private static async Task<Solution> ChangedSolution(CodeAction action, CancellationToken cancellationToken)
    {
        global::System.ArgumentNullException.ThrowIfNull(action);
        return (await (action.GetOperationsAsync(cancellationToken)).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
    }

    private static async Task CheckCompilation(Document document, CancellationToken cancellationToken)
    {
        var compilation = (await (document.Project.GetCompilationAsync(cancellationToken: cancellationToken)).ConfigureAwait(false))!;
        var errors = compilation.GetDiagnostics(cancellationToken: cancellationToken).Where(d =>
        {
            global::System.ArgumentNullException.ThrowIfNull(d);
            return d.Severity == DiagnosticSeverity.Error;
        }).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException(string.Join("\n", errors.Select(d =>
            {
                global::System.ArgumentNullException.ThrowIfNull(d);
                return d.ToString();
            })));
        }
    }

    private static async Task<Diagnostic[]> Diagnostics(Document document, CancellationToken cancellationToken)
    {
        global::System.ArgumentNullException.ThrowIfNull(document);
        var compilation = (await (document.Project.GetCompilationAsync(cancellationToken: cancellationToken)).ConfigureAwait(false))!;
        var tree = await (document.GetSyntaxTreeAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);
        return (await (compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false)).GetAnalyzerDiagnosticsAsync(cancellationToken: cancellationToken)).ConfigureAwait(false)).Where(d =>
        {
            global::System.ArgumentNullException.ThrowIfNull(d);
            return d.Location.SourceTree == tree;
        }).ToArray();
    }

    internal sealed class DiagnosticSource : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
        {
            global::System.ArgumentNullException.ThrowIfNull(document);
            return await (Diagnostics(document, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            global::System.ArgumentNullException.ThrowIfNull(project);
            return Task.FromResult(Enumerable.Empty<Diagnostic>());
        }

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            global::System.ArgumentNullException.ThrowIfNull(project);
            var compilation = (await (project.GetCompilationAsync(cancellationToken)).ConfigureAwait(false))!;
            return await (compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false)).GetAnalyzerDiagnosticsAsync(cancellationToken)).ConfigureAwait(false);
        }
    }
}
