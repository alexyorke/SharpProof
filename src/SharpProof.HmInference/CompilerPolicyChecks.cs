using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class CompilerPolicyChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            specificDiagnosticOptions: ImmutableDictionary<string, ReportDiagnostic>.Empty.Add("CS8019", ReportDiagnostic.Error));
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "CompilerPolicy", "CompilerPolicy", LanguageNames.CSharp,
            compilationOptions: options, metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Input.cs", "using System.Text; public static class PolicyProbe {public static int M(int value)=>value;}");
        var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (!compilation.GetDiagnostics(cancellationToken).Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "CS8019" && d.Severity == DiagnosticSeverity.Error && d.DefaultSeverity == DiagnosticSeverity.Hidden; }))
        { throw new InvalidOperationException("Compiler policy fixture must contain an existing editor-only style error."); }
        var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var diagnostic = diagnostics.Single(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001"; });
        var provider = new HmSignatureCodeFixProvider();
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        var after = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (after.GetDiagnostics(cancellationToken).Any(Inference.IsCompilationError))
        { throw new InvalidOperationException("Style-tolerant code fix introduced a compilation error."); }
        using var image = new MemoryStream();
        var emitted = after.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Style-tolerant code fix failed emission: " + string.Join("\n", emitted.Diagnostics)); }
        var marker = Guid.NewGuid();
        var method = System.Reflection.Assembly.Load(image.ToArray()).GetType("PolicyProbe")!.GetMethod("M")!.MakeGenericMethod(typeof(Guid));
        if (!Equals(method.Invoke(null, new object[] { marker }), marker))
        { throw new InvalidOperationException("Style-tolerant code fix changed the inferred identity."); }
        Program.Log.Info("PASS ExistingEditorStyleErrorAnalyzerCodeFixAndRuntime");
        var introduced = Program.Compile([CSharpSyntaxTree.ParseText("using System.Collections.Generic;class Cases {static List<int> M(List<int> value)=>value;}", cancellationToken: cancellationToken)]).WithOptions(options);
        var result = Program.Results(introduced, cancellationToken).Single();
        if (result.Status != "Rejected" || !result.Message.Contains("style", StringComparison.Ordinal))
        { throw new InvalidOperationException("New editor style errors must still reject generalization: " + result); }
        Program.Log.Info("PASS NewEditorStyleErrorsRejectGeneralization");
        var configured = introduced.WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithSyntaxTreeOptionsProvider(new PerTreeStyleOptions(introduced.SyntaxTrees.Single())));
        result = Program.Results(configured, cancellationToken).Single();
        if (result.Status != "Rejected" || !result.Message.Contains("style", StringComparison.Ordinal))
        { throw new InvalidOperationException("Per-tree editor severity was lost during candidate validation: " + result); }
        Program.Log.Info("PASS PerTreeEditorSeveritySurvivesCandidateReplacement");
        return 3;
    }
    private sealed class PerTreeStyleOptions : SyntaxTreeOptionsProvider
    {
        private readonly SyntaxTree original;
        internal PerTreeStyleOptions(SyntaxTree original)
        {
            ArgumentNullException.ThrowIfNull(original);
            this.original = original;
        }
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(tree);
            return GeneratedKind.Unknown;
        }
        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            ArgumentNullException.ThrowIfNull(tree);
            ArgumentNullException.ThrowIfNull(diagnosticId);
            severity = ReportDiagnostic.Error;
            return ReferenceEquals(tree, original) && diagnosticId == "CS8019";
        }
        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            ArgumentNullException.ThrowIfNull(diagnosticId);
            severity = ReportDiagnostic.Default;
            return false;
        }
    }
}
