using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class RecursiveGroupCodeFixChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(),
            VersionStamp.Create(), "RecursiveGroupFix", "RecursiveGroupFix", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: CSharpParseOptions.Default,
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("RecursiveGroup.cs", """
            public static class RecursivePair
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
                public static int Second(int value, bool again) => again ? First(value, false) : value;
                public static int ExistingCaller() => First(7, true);
            }
            """);
        var diagnostics = await Diagnostics(document, cancellationToken).ConfigureAwait(false);
        if (diagnostics.Length != 2)
        { throw new InvalidOperationException("Expected two recursive-group analyzer diagnostics."); }
        var first = diagnostics.Single(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ =>
            (awaitableMethodName(d, document, cancellationToken)) == "First");
        var provider = new HmSignatureCodeFixProvider();
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, first,
            (action /* SPINT001: all values valid - Code fix registration supplies a nonnull action. */, _) => actions.Add(action), cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 1)
        { throw new InvalidOperationException("Expected one recursive-group code action."); }

        var fixedSolution = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        var fixedDocument = fixedSolution.GetDocument(document.Id)!;
        await AssertApplied(fixedDocument, cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS RecursiveGroupIDEAtomicApply");

        var context = new FixAllContext(document, provider, FixAllScope.Document,
            "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var fixAll = await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false);
        if (fixAll is null)
        { throw new InvalidOperationException("Missing recursive-group Fix All action."); }
        var allSolution = await ChangedSolution(fixAll, cancellationToken).ConfigureAwait(false);
        await AssertApplied(allSolution.GetDocument(document.Id)!, cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS RecursiveGroupIDEFixAll");
        var tripleDocument = project.AddDocument("TripleRecursive.cs", """
            public static class TripleRecursive
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
                public static int Second(int value, bool again) => again ? Third(value, false) : value;
                public static int Third(int value, bool again) => again ? First(value, false) : value;
                public static int ExistingCaller() => Third(9, true);
            }
            """);
        var tripleDiagnostics = await Diagnostics(tripleDocument, cancellationToken).ConfigureAwait(false);
        if (tripleDiagnostics.Length != 3)
        { throw new InvalidOperationException("Expected three SCC diagnostics in the IDE."); }
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(tripleDocument, tripleDiagnostics[0],
            (action /* SPINT001: all values valid - Code fix registration supplies a nonnull action. */, _) => actions.Add(action), cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 1)
        { throw new InvalidOperationException("Expected a three-member SCC code action."); }
        var tripleFixed = (await ChangedSolution(actions.Single(), cancellationToken)
            .ConfigureAwait(false)).GetDocument(tripleDocument.Id)!;
        await AssertTripleApplied(tripleFixed, cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS RecursiveTripleIDEAtomicApply");
        var tripleContext = new FixAllContext(tripleDocument, provider, FixAllScope.Document,
            "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var tripleFixAll = await provider.GetFixAllProvider().GetFixAsync(tripleContext)
            .ConfigureAwait(false);
        if (tripleFixAll is null)
        { throw new InvalidOperationException("Missing three-member SCC Fix All action."); }
        var tripleAllSolution = await ChangedSolution(tripleFixAll, cancellationToken).ConfigureAwait(false);
        await AssertTripleApplied(tripleAllSolution.GetDocument(tripleDocument.Id)!, cancellationToken)
            .ConfigureAwait(false);
        Program.Log.Info("PASS RecursiveTripleIDEFixAll");
        var qualifiedDocument = project.AddDocument("QualifiedRecursive.cs", """
            using PairAlias = global::QualifiedCycle;
            public static class QualifiedCycle
            {
                public static int First(int value, bool again) => again ? QualifiedCycle.Second(value, false) : value;
                public static int Second(int value, bool again) => again ? global::QualifiedCycle.Third(value, false) : value;
                public static int Third(int value, bool again) => again ? PairAlias.First(value, false) : value;
                public static int ExistingCaller() => QualifiedCycle.First(11, true);
            }
            """);
        var qualifiedDiagnostics = await Diagnostics(qualifiedDocument, cancellationToken).ConfigureAwait(false);
        if (qualifiedDiagnostics.Length != 3)
        { throw new InvalidOperationException("Expected three qualified SCC diagnostics in the IDE."); }
        var qualifiedNotices = Program.RecursiveGroupNotices(qualifiedDiagnostics);
        if (qualifiedDiagnostics.Any(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ =>
                !d.Properties.ContainsKey(RecursiveGroupInfo.DiagnosticAnchorProperty)
                || !d.Properties.ContainsKey(RecursiveGroupInfo.DiagnosticCountProperty))
            || qualifiedNotices.Length != 1
            || qualifiedNotices[0] != "Recursive group (3 methods): apply all member signatures together: T1 First<T1>(T1 value, bool again); T1 Second<T1>(T1 value, bool again); T1 Third<T1>(T1 value, bool again)")
        { throw new InvalidOperationException("HM0001 group metadata must describe one atomic three-member notice."); }
        Program.Log.Info("PASS RecursiveQualifiedDiagnosticMetadata");
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(qualifiedDocument, qualifiedDiagnostics[0],
            (action /* SPINT001: all values valid - Code fix registration supplies a nonnull action. */, _) =>
                actions.Add(action), cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 1)
        { throw new InvalidOperationException("Expected a qualified SCC code action."); }
        var qualifiedFixed = (await ChangedSolution(actions.Single(), cancellationToken)
            .ConfigureAwait(false)).GetDocument(qualifiedDocument.Id)!;
        await AssertQualifiedApplied(qualifiedFixed, cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS RecursiveQualifiedIDEAtomicApply");

        var qualifiedContext = new FixAllContext(qualifiedDocument, provider, FixAllScope.Document,
            "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var qualifiedFixAll = await provider.GetFixAllProvider().GetFixAsync(qualifiedContext)
            .ConfigureAwait(false);
        if (qualifiedFixAll is null)
        { throw new InvalidOperationException("Missing qualified SCC Fix All action."); }
        var qualifiedAll = await ChangedSolution(qualifiedFixAll, cancellationToken).ConfigureAwait(false);
        await AssertQualifiedApplied(qualifiedAll.GetDocument(qualifiedDocument.Id)!, cancellationToken)
            .ConfigureAwait(false);
        Program.Log.Info("PASS RecursiveQualifiedIDEFixAll");
        const string linkedSource = """
            public static class LinkedRecursivePair
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
                public static int Second(int value, bool again) => again ? First(value, false) : value;
                public static int ExistingCaller() => First(7, true);
            }
            """;
        var linkedFirstId = ProjectId.CreateNewId();
        var linkedSecondId = ProjectId.CreateNewId();
        var linkedFirstDocumentId = DocumentId.CreateNewId(linkedFirstId);
        var linkedSecondDocumentId = DocumentId.CreateNewId(linkedSecondId);
        var linkedSolution = workspace.CurrentSolution
            .AddProject(linkedFirstId, "GroupLinkedFirst", "GroupLinkedFirst", LanguageNames.CSharp)
            .WithProjectCompilationOptions(linkedFirstId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(linkedFirstId, Program.Compile([]).References)
            .AddProject(linkedSecondId, "GroupLinkedSecond", "GroupLinkedSecond", LanguageNames.CSharp)
            .WithProjectCompilationOptions(linkedSecondId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(linkedSecondId, Program.Compile([]).References)
            .AddDocument(linkedFirstDocumentId, "RecursiveLinked.cs", linkedSource, filePath: "/shared/RecursiveLinked.cs")
            .AddDocument(linkedSecondDocumentId, "RecursiveLinked.cs", linkedSource, filePath: "/shared/RecursiveLinked.cs");
        var linkedFirst = linkedSolution.GetDocument(linkedFirstDocumentId)!;
        var linkedDiagnostic = (await Diagnostics(linkedFirst, cancellationToken).ConfigureAwait(false))
            .OrderBy(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ => d.Location.SourceSpan.Start).First();
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(linkedFirst, linkedDiagnostic,
            (action /* SPINT001: all values valid - Code fix registration supplies a nonnull action. */, _) => actions.Add(action), cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 1)
        { throw new InvalidOperationException("Expected linked recursive-group code action."); }
        var linkedChanged = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        var linkedTexts = new List<string>();
        foreach (var linkedId in new[] { linkedFirstDocumentId, linkedSecondDocumentId })
        {
            var linkedDocument = linkedChanged.GetDocument(linkedId)!;
            var linkedCompilation = (await linkedDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            if (linkedCompilation.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ => d.Severity == DiagnosticSeverity.Error))
            { throw new InvalidOperationException("Linked recursive-group fix failed compilation."); }
            linkedTexts.Add((await linkedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString());
        }
        if (linkedTexts[0] != linkedTexts[1] || !linkedTexts[0].Contains("First<T1>", StringComparison.Ordinal)
            || !linkedTexts[0].Contains("Second<T1>", StringComparison.Ordinal))
        { throw new InvalidOperationException("Linked recursive-group fix missed a member."); }
        Program.Log.Info("PASS RecursiveGroupIDELinkedApply");
        var dependentId = ProjectId.CreateNewId();
        var dependentDocumentId = DocumentId.CreateNewId(dependentId);
        var dependentSolution = linkedSolution
            .AddProject(dependentId, "GroupDependent", "GroupDependent", LanguageNames.CSharp)
            .WithProjectCompilationOptions(dependentId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(dependentId, Program.Compile([]).References)
            .AddProjectReference(dependentId, new ProjectReference(linkedSecondId))
            .AddDocument(dependentDocumentId, "Dependent.cs",
                "public static class Dependent { public static int Use() => LinkedRecursivePair.First(default, false); }");
        var dependentBefore = (await dependentSolution.GetProject(dependentId)!
            .GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (dependentBefore.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ => d.Severity == DiagnosticSeverity.Error))
        { throw new InvalidOperationException("Dependent group control must compile before the fix."); }
        linkedFirst = dependentSolution.GetDocument(linkedFirstDocumentId)!;
        linkedDiagnostic = (await Diagnostics(linkedFirst, cancellationToken).ConfigureAwait(false))
            .OrderBy(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ => d.Location.SourceSpan.Start).First();
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(linkedFirst, linkedDiagnostic,
            (action /* SPINT001: all values valid - Code fix registration supplies a nonnull action. */, _) => actions.Add(action), cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 1)
        { throw new InvalidOperationException("Expected dependent group code action."); }
        linkedChanged = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        foreach (var linkedId in new[] { linkedFirstDocumentId, linkedSecondDocumentId })
        {
            var text = (await linkedChanged.GetDocument(linkedId)!.GetTextAsync(cancellationToken)
                .ConfigureAwait(false)).ToString();
            if (text != linkedSource)
            { throw new InvalidOperationException("Dependent C# break caused a partial group edit."); }
        }
        Program.Log.Info("PASS RecursiveGroupIDEDependentRollback");

        const string divergentSource = """
            public static class LinkedRecursivePair
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
            #if ALT
                public static int Second(int value, bool again) => again ? First(value, false) : value + 1;
            #else
                public static int Second(int value, bool again) => again ? First(value, false) : value;
            #endif
                public static int ExistingCaller() => First(7, true);
            }
            """;
        var divergent = linkedSolution
            .WithDocumentText(linkedFirstDocumentId, Microsoft.CodeAnalysis.Text.SourceText.From(divergentSource))
            .WithDocumentText(linkedSecondDocumentId, Microsoft.CodeAnalysis.Text.SourceText.From(divergentSource))
            .WithProjectParseOptions(linkedSecondId, CSharpParseOptions.Default.WithPreprocessorSymbols("ALT"));
        linkedFirst = divergent.GetDocument(linkedFirstDocumentId)!;
        linkedDiagnostic = (await Diagnostics(linkedFirst, cancellationToken).ConfigureAwait(false))
            .OrderBy(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ => d.Location.SourceSpan.Start).First();
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(linkedFirst, linkedDiagnostic,
            (action /* SPINT001: all values valid - Code fix registration supplies a nonnull action. */, _) => actions.Add(action), cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 1)
        { throw new InvalidOperationException("Expected divergent linked recursive-group action."); }
        linkedChanged = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        foreach (var linkedId in new[] { linkedFirstDocumentId, linkedSecondDocumentId })
        {
            var text = (await linkedChanged.GetDocument(linkedId)!.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            if (text != divergentSource)
            { throw new InvalidOperationException("Divergent linked group caused a partial edit."); }
        }
        Program.Log.Info("PASS RecursiveGroupIDELinkedRollback");
        const string tripleDivergentSource = """
            public static class LinkedRecursiveTriple
            {
                public static int First(int value, bool again) => again ? Second(value, false) : value;
                public static int Second(int value, bool again) => again ? Third(value, false) : value;
            #if ALT
                public static int Third(int value, bool again) => again ? First(value, false) : value + 1;
            #else
                public static int Third(int value, bool again) => again ? First(value, false) : value;
            #endif
                public static int ExistingCaller() => Third(9, true);
            }
            """;
        var tripleDivergent = linkedSolution
            .WithDocumentText(linkedFirstDocumentId,
                Microsoft.CodeAnalysis.Text.SourceText.From(tripleDivergentSource))
            .WithDocumentText(linkedSecondDocumentId,
                Microsoft.CodeAnalysis.Text.SourceText.From(tripleDivergentSource))
            .WithProjectParseOptions(linkedSecondId,
                CSharpParseOptions.Default.WithPreprocessorSymbols("ALT"));
        foreach (var projectId in new[] { linkedFirstId, linkedSecondId })
        {
            var compilation = (await tripleDivergent.GetProject(projectId)!
                .GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            if (compilation.GetDiagnostics(cancellationToken).Any(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ => d.Severity == DiagnosticSeverity.Error))
            { throw new InvalidOperationException("Three-member linked context must compile natively."); }
        }
        linkedFirst = tripleDivergent.GetDocument(linkedFirstDocumentId)!;
        var tripleLinkedDiagnostics = await Diagnostics(linkedFirst, cancellationToken).ConfigureAwait(false);
        if (tripleLinkedDiagnostics.Length != 3)
        { throw new InvalidOperationException("Expected three SCC diagnostics in the primary linked context."); }
        actions.Clear();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(linkedFirst,
            tripleLinkedDiagnostics.OrderBy(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ => d.Location.SourceSpan.Start).First(),
            (action /* SPINT001: all values valid - Code fix registration supplies a nonnull action. */, _) => actions.Add(action), cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 1)
        { throw new InvalidOperationException("Expected three-member linked SCC action."); }
        linkedChanged = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        foreach (var linkedId in new[] { linkedFirstDocumentId, linkedSecondDocumentId })
        {
            var text = (await linkedChanged.GetDocument(linkedId)!
                .GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            if (text != tripleDivergentSource)
            { throw new InvalidOperationException("Three-member divergent linked SCC caused a partial edit."); }
        }
        Program.Log.Info("PASS RecursiveTripleIDELinkedRollback");
        return 11;
    }

    private static string awaitableMethodName(
        Diagnostic diagnostic, Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentNullException.ThrowIfNull(document);
        var root = document.GetSyntaxRootAsync(cancellationToken).GetAwaiter().GetResult()!;
        return root.FindToken(diagnostic.Location.SourceSpan.Start).Parent!
            .FirstAncestorOrSelf<MethodDeclarationSyntax>()!.Identifier.ValueText;
    }

    private static async Task<Diagnostic[]> Diagnostics(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        return (await compilation.WithAnalyzers([new HmInferenceAnalyzer()],
            new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null,
                concurrentAnalysis: true, logAnalyzerExecutionTime: false))
            .GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false)).ToArray();
    }

    private static async Task<Solution> ChangedSolution(CodeAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        return (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false))
            .OfType<ApplyChangesOperation>().Single().ChangedSolution;
    }

    private static async Task AssertTripleApplied(Document document, CancellationToken cancellationToken)
    {
        var tripleRoot = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        if (tripleRoot.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Count(method /* SPINT001: all values valid - Syntax descendants are nonnull method nodes. */ => method.TypeParameterList is not null) != 3)
        { throw new InvalidOperationException("IDE action missed a three-member SCC member."); }
        var tripleOwner = tripleRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var tripleCaller = SyntaxFactory.ParseMemberDeclaration(
            "public static string NewCaller() => First(\"three\", true);")!;
        var tripleTested = document.WithSyntaxRoot(tripleRoot.ReplaceNode(tripleOwner,
            tripleOwner.AddMembers(tripleCaller)));
        var tripleCompilation = (await tripleTested.Project.GetCompilationAsync(cancellationToken)
            .ConfigureAwait(false))!;
        using var tripleImage = new MemoryStream();
        var tripleEmit = tripleCompilation.Emit(tripleImage, cancellationToken: cancellationToken);
        if (!tripleEmit.Success)
        {
            throw new InvalidOperationException("Three-member IDE fix did not emit: "
            + string.Join("\n", tripleEmit.Diagnostics.Select(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ => d.ToString())));
        }
        var tripleType = System.Reflection.Assembly.Load(tripleImage.ToArray())
            .GetType("TripleRecursive")!;
        if (!Equals(tripleType.GetMethod("ExistingCaller")!.Invoke(null, null), 9)
            || !Equals(tripleType.GetMethod("NewCaller")!.Invoke(null, null), "three"))
        { throw new InvalidOperationException("Three-member IDE fix failed runtime controls."); }
    }

    private static async Task AssertQualifiedApplied(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        if (root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Count(method /* SPINT001: all values valid - Syntax descendants are nonnull method nodes. */ =>
                method.TypeParameterList is not null) != 3)
        { throw new InvalidOperationException("Qualified IDE action missed an SCC member."); }
        var text = root.ToFullString();
        if (!text.Contains("QualifiedCycle.Second", StringComparison.Ordinal)
            || !text.Contains("global::QualifiedCycle.Third", StringComparison.Ordinal)
            || !text.Contains("PairAlias.First", StringComparison.Ordinal))
        { throw new InvalidOperationException("Qualified IDE action changed call syntax."); }
        var owner = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var stringCaller = SyntaxFactory.ParseMemberDeclaration(
            "public static string NewCaller() => PairAlias.Third(\"ide\", true);")!;
        var tested = document.WithSyntaxRoot(root.ReplaceNode(owner, owner.AddMembers(stringCaller)));
        var compilation = (await tested.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        {
            throw new InvalidOperationException("Qualified IDE fix failed emission: "
            + string.Join("\n", emitted.Diagnostics.Select(d /* SPINT001: all values valid - Compiler diagnostics are nonnull values. */ => d.ToString())));
        }
        var type = System.Reflection.Assembly.Load(image.ToArray()).GetType("QualifiedCycle")!;
        if (!Equals(type.GetMethod("ExistingCaller")!.Invoke(null, null), 11)
            || !Equals(type.GetMethod("NewCaller")!.Invoke(null, null), "ide"))
        { throw new InvalidOperationException("Qualified IDE fix failed int/string runtime controls."); }
    }

    private static async Task AssertApplied(Document document, CancellationToken cancellationToken)
    {
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        if (methods.Count(method /* SPINT001: all values valid - Syntax descendants are nonnull method nodes. */ => method.TypeParameterList is not null) != 2)
        { throw new InvalidOperationException("Group fix did not rewrite both members."); }
        var owner = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var stringCaller = SyntaxFactory.ParseMemberDeclaration(
            "public static string NewCaller() => Second(\"text\", true);")!;
        var tested = document.WithSyntaxRoot(root.ReplaceNode(owner, owner.AddMembers(stringCaller)));
        var compilation = (await tested.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        {
            throw new InvalidOperationException("IDE group fix failed emission: "
            + string.Join("\n", emitted.Diagnostics.Select(d /* SPINT001: all values valid - Analyzer diagnostics are nonnull values. */ => d.ToString())));
        }
        var pair = System.Reflection.Assembly.Load(image.ToArray()).GetType("RecursivePair")!;
        if (!Equals(pair.GetMethod("ExistingCaller")!.Invoke(null, null), 7)
            || !Equals(pair.GetMethod("NewCaller")!.Invoke(null, null), "text"))
        { throw new InvalidOperationException("IDE group fix failed int/string runtime controls."); }
    }
}
