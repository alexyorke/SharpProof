using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class InlineArrayConversionChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        RunAsync(cancellationToken).GetAwaiter().GetResult();
        return 7;
    }

    private static async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var workspace = new AdhocWorkspace();
        await CheckCSharp12(workspace, cancellationToken).ConfigureAwait(false);
        await CheckNegativeControls(workspace, cancellationToken).ConfigureAwait(false);
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "InlineSpanIDE", "InlineSpanIDE", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp13),
            metadataReferences: Program.Compile([]).References));
        var fixture = project.AddDocument("Fixture.cs", FixtureSource);
        var runtime = fixture.Project.AddDocument("RuntimeCheck.cs", RuntimeSource);
        fixture = runtime.Project.GetDocument(fixture.Id)!;
        await CheckAndRun(fixture.Project, "original", cancellationToken: cancellationToken).ConfigureAwait(false);

        var diagnostics = await Diagnostics(fixture, cancellationToken).ConfigureAwait(false);
        string[] required = ["ReturnReadOnly", "ReturnWritable", "DirectCount", "DirectCountIn", "DirectCountRef",
            "ViaGeneric", "ViaGenericWrite"];
        var root = (await fixture.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        foreach (var name in required)
        {
            var method = FindMethod(root, name);
            if (!diagnostics.Any(d =>
            { ArgumentNullException.ThrowIfNull(d); return d.Location.SourceSpan == method.Identifier.Span; }))
            { throw new InvalidOperationException("Missing HM0001: " + name); }
        }
        Program.Log.Info("PASS analyzer HM0001 for " + required.Length + " inline-span forms; no AD0001");

        var provider = new HmSignatureCodeFixProvider();
        var readOnly = FindMethod(root, "ReturnReadOnly");
        var diagnostic = diagnostics.Single(d =>
        { ArgumentNullException.ThrowIfNull(d); return d.Location.SourceSpan == readOnly.Identifier.Span; });
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(fixture, diagnostic,
            (action, _) =>
            { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 1)
        { throw new InvalidOperationException("Inline-span IDE action missing."); }
        var oneSolution = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        var oneDocument = oneSolution.GetDocument(fixture.Id)!;
        await CheckAndRun(oneDocument.Project, "single IDE fix", cancellationToken: cancellationToken).ConfigureAwait(false);
        var oneText = (await oneDocument.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        if (!oneText.Contains("ReadOnlySpan<T1> ReturnReadOnly<T1, T2>(in global::Buffer<T1>", StringComparison.Ordinal))
        { throw new InvalidOperationException("ReadOnlySpan IDE fix lost Buffer<T> payload link."); }
        Program.Log.Info("PASS applied IDE readonly span fix and runtime");

        var context = new FixAllContext(fixture, provider, FixAllScope.Document, "HM.GeneralizeSignature",
            provider.FixableDiagnosticIds, new DiagnosticSource(), cancellationToken);
        var allAction = await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Inline-span Fix All action missing.");
        var allDocument = (await ChangedSolution(allAction, cancellationToken).ConfigureAwait(false)).GetDocument(fixture.Id)!;
        await CheckAndRun(allDocument.Project, "document Fix All", cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = (await allDocument.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        var changedRoot = (await allDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        foreach (var name in required)
        {
            if (FindMethod(changedRoot, name).TypeParameterList is null)
            { throw new InvalidOperationException("Fix All missed " + name); }
        }
        if (!text.Contains("ReadOnlySpan<T1> ReturnReadOnly<T1, T2>(in global::Buffer<T1>", StringComparison.Ordinal)
            || !text.Contains("Span<T1> ReturnWritable<T1, T2>(ref global::Buffer<T1>", StringComparison.Ordinal))
        { throw new InvalidOperationException("Fix All lost span-to-buffer payload relationship."); }
        foreach (var name in new[] { "ViaFixed", "ViaFixedWrite" })
        {
            if (!FindMethod(changedRoot, name).ParameterList.ToString().Contains("Buffer<int>", StringComparison.Ordinal))
            { throw new InvalidOperationException("Fixed helper caller generalized Buffer<int>: " + name); }
        }
        if (FindMethod(changedRoot, "Fixed").TypeParameterList is not null
            || FindMethod(changedRoot, "Fill").TypeParameterList is not null)
        { throw new InvalidOperationException("Int-specific helper unexpectedly generalized."); }
        var generalized = allDocument.Project.AddDocument("GeneralizedRuntimeCheck.cs", GeneralizedRuntimeSource);
        await CheckAndRun(generalized.Project, "new generic payloads", "GeneralizedRuntimeCheck", cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS document Fix All applied " + required.Length + " span/foreach signatures and runtime");
    }

    private static async Task CheckCSharp12(AdhocWorkspace workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var references = Program.Compile([]).References;
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var negativeProject = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "InlineSpanCSharp12Negative", "InlineSpanCSharp12Negative", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: parseOptions, metadataReferences: references));
        var negative = negativeProject.AddDocument("DirectForEach.cs", FixtureSource);
        var negativeCompilation = (await negative.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (!negativeCompilation.GetDiagnostics(cancellationToken).Any(d =>
        { ArgumentNullException.ThrowIfNull(d); return d.Id == "CS9202"; }))
        { throw new InvalidOperationException("Pinned Roslyn explicit C#12 foreach boundary changed."); }
        _ = await Diagnostics(negative, cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS pinned Roslyn explicit C#12 direct foreach CS9202; no AD0001");

        var returnProject = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "InlineSpanCSharp12Return", "InlineSpanCSharp12Return", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: parseOptions, metadataReferences: references));
        var source = returnProject.AddDocument("ReturnOnly.cs", ReturnOnlySource);
        var sourceCompilation = (await source.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var problems = sourceCompilation.GetDiagnostics(cancellationToken).Where(d =>
        { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (problems.Length != 0)
        {
            throw new InvalidOperationException("Explicit C#12 return fixture: " + string.Join("; ", problems.Select(d =>
            {
                ArgumentNullException.ThrowIfNull(d);
                return d.ToString();
            })));
        }
        var diagnostics = await Diagnostics(source, cancellationToken).ConfigureAwait(false);
        if (diagnostics.Length != 2)
        { throw new InvalidOperationException("Expected two explicit C#12 span return diagnostics."); }
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(source, provider, FixAllScope.Document, "HM.GeneralizeSignature",
            provider.FixableDiagnosticIds, new DiagnosticSource(), cancellationToken);
        var action = await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Explicit C#12 span return Fix All missing.");
        var fixedDocument = (await ChangedSolution(action, cancellationToken).ConfigureAwait(false)).GetDocument(source.Id)!;
        var runtime = fixedDocument.Project.AddDocument("ReturnRuntime.cs", ReturnOnlyRuntimeSource);
        await CheckAndRun(runtime.Project, "explicit C#12 span return Fix All", "ReturnOnlyRuntime", cancellationToken).ConfigureAwait(false);
    }

    private static async Task CheckNegativeControls(AdhocWorkspace workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "InlineSpanNegative", "InlineSpanNegative", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp13),
            metadataReferences: Program.Compile([]).References));
        var source = project.AddDocument("Negative.cs", NegativeSource);
        var runtime = source.Project.AddDocument("NegativeRuntime.cs", NegativeRuntimeSource);
        source = runtime.Project.GetDocument(source.Id)!;
        await CheckAndRun(runtime.Project, "user conversion and fake-name controls", "NegativeRuntime", cancellationToken).ConfigureAwait(false);
        var diagnostics = await Diagnostics(source, cancellationToken).ConfigureAwait(false);
        if (diagnostics.Length != 2
            || !diagnostics.Any(d =>
            {
                ArgumentNullException.ThrowIfNull(d);
                return d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("global::System.ReadOnlySpan<T1> UserToRealSpan<T1, T2>(global::FakeBuffer<T1>", StringComparison.Ordinal);
            })
            || !diagnostics.Any(d =>
            {
                ArgumentNullException.ThrowIfNull(d);
                return d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("global::Mimic.Span<T1> UserToFakeSpan<T1, T2>(global::FakeBuffer<T1>", StringComparison.Ordinal);
            }))
        { throw new InvalidOperationException("User-defined or unrelated-name conversion suggestions changed."); }
        Program.Log.Info("PASS user-defined conversion and unrelated InlineArrayAttribute/Span<T> names; no AD0001");
    }

    private static MethodDeclarationSyntax FindMethod(SyntaxNode root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(name);
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
        { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == name; });
    }

    private static async Task<Diagnostic[]> Diagnostics(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var all = await compilation.WithAnalyzers([new HmInferenceAnalyzer()],
            new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null,
                concurrentAnalysis: true, logAnalyzerExecutionTime: false))
            .GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var exceptions = all.Where(d =>
        { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }).ToArray();
        if (exceptions.Length != 0)
        {
            throw new InvalidOperationException("Analyzer exception: " + string.Join("; ", exceptions.Select(d =>
            {
                ArgumentNullException.ThrowIfNull(d);
                return d.ToString();
            })));
        }
        var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(d =>
        { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && d.Location.SourceTree == tree; }).ToArray();
    }

    private static async Task<Solution> ChangedSolution(CodeAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        return (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false))
            .OfType<ApplyChangesOperation>().Single().ChangedSolution;
    }

    private static async Task CheckAndRun(Project project, string stage, CancellationToken cancellationToken)
    {
        await CheckAndRun(project, stage, "RuntimeCheck", cancellationToken).ConfigureAwait(false);
    }

    private static async Task CheckAndRun(Project project, string stage, string runtimeType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(runtimeType);
        var compilation = (await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var problems = compilation.GetDiagnostics(cancellationToken).Where(d =>
        { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (problems.Length != 0)
        {
            throw new InvalidOperationException(stage + " compiler diagnostics: " + string.Join("; ", problems.Select(d =>
            {
                ArgumentNullException.ThrowIfNull(d);
                return d.ToString();
            })));
        }
        using var bytes = new MemoryStream();
        var emitted = compilation.Emit(bytes, cancellationToken: cancellationToken);
        if (!emitted.Success)
        {
            throw new InvalidOperationException(stage + " emit: " + string.Join("; ", emitted.Diagnostics.Select(d =>
            {
                ArgumentNullException.ThrowIfNull(d);
                return d.ToString();
            })));
        }
        bytes.Position = 0;
        var context = new AssemblyLoadContext("InlineArray-" + stage, isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(bytes);
            var method = assembly.GetType(runtimeType)!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            try
            {
                var outcome = (string)method.Invoke(null, null)!;
                Program.Log.Info(stage + ": " + outcome);
            }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException(stage + " runtime", error.InnerException); }
        }
        finally { context.Unload(); }
    }

    private sealed class DiagnosticSource : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);
            return await Diagnostics(document, cancellationToken).ConfigureAwait(false);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(project);
            return Task.FromResult(Enumerable.Empty<Diagnostic>());
        }

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(project);
            var results = new List<Diagnostic>();
            foreach (var document in project.Documents)
            { results.AddRange(await Diagnostics(document, cancellationToken).ConfigureAwait(false)); }
            return results;
        }
    }


    private const string FixtureSource = """
        #nullable enable
        using System;
        using System.Runtime.CompilerServices;

        [InlineArray(4)] public struct Buffer<T> { private T _element0; }
        public static class Cases
        {
            public static ReadOnlySpan<int> ReturnReadOnly(in Buffer<int> values, int ignored) => values;
            public static Span<int> ReturnWritable(ref Buffer<int> values, int ignored) => values;
            public static int DirectCount(Buffer<int> values, int ignored)
            { int count = 0; foreach (var _ in values) count++; return count; }
            public static int DirectCountIn(in Buffer<int> values, int ignored)
            { int count = 0; foreach (var _ in values) count++; return count; }
            public static int DirectCountRef(ref Buffer<int> values, int ignored)
            { int count = 0; foreach (var _ in values) count++; return count; }
            public static int ViaFixed(in Buffer<int> values, int ignored) => Fixed(values);
            private static int Fixed(ReadOnlySpan<int> values) => values[0] + 1;
            public static void ViaFixedWrite(ref Buffer<int> values, int replacement, int ignored) => Fill(values, replacement);
            private static void Fill(Span<int> values, int replacement) => values[0] = replacement + 1;
            public static int ViaGeneric(in Buffer<int> values, int sample, int ignored) => ReadGeneric(values, sample);
            private static T ReadGeneric<T>(ReadOnlySpan<T> values, T sample) => values[1];
            public static void ViaGenericWrite(ref Buffer<int> values, int replacement, int ignored) => FillGeneric(values, replacement);
            private static void FillGeneric<T>(Span<T> values, T replacement) => values[1] = replacement;
        }
        """;

    private const string RuntimeSource = """
        #nullable enable
        using System;

        public static class RuntimeCheck
        {
            private static void Check(bool value, string label)
            { if (!value) throw new InvalidOperationException(label); }

            public static string Run()
            {
                Buffer<int> values = default;
                values[0] = 3;
                values[1] = 7;
                ReadOnlySpan<int> readOnly = Cases.ReturnReadOnly(in values, 0);
                Check(readOnly.Length == 4 && readOnly[1] == 7, "readonly return");
                Span<int> writable = Cases.ReturnWritable(ref values, 0);
                writable[1] = 9;
                Check(values[1] == 9, "writable alias identity");
                Check(Cases.DirectCount(values, 0) == 4, "value foreach");
                Check(Cases.DirectCountIn(in values, 0) == 4, "in foreach");
                Check(Cases.DirectCountRef(ref values, 0) == 4, "ref foreach");
                Check(Cases.ViaFixed(in values, 0) == 4, "fixed helper read");
                Cases.ViaFixedWrite(ref values, 10, 0);
                Check(values[0] == 11, "fixed helper write");
                Check(Cases.ViaGeneric(in values, 11, 0) == 9, "generic helper argument conversion");
                Cases.ViaGenericWrite(ref values, 12, 0);
                Check(values[1] == 12, "generic helper writable conversion");
                try { _ = Cases.ReturnReadOnly(in values, 0)[4]; throw new InvalidOperationException("readonly bounds accepted"); }
                catch (IndexOutOfRangeException) { }
                try { Cases.ReturnWritable(ref values, 0)[4] = 1; throw new InvalidOperationException("writable bounds accepted"); }
                catch (IndexOutOfRangeException) { }
                return "PASS original int span return alias, foreach, fixed helpers, bounds";
            }
        }
        """;

    private const string GeneralizedRuntimeSource = """
        #nullable enable
        using System;

        public static class GeneralizedRuntimeCheck
        {
            private static void Check(bool value, string label)
            { if (!value) throw new InvalidOperationException(label); }

            public static string Run()
            {
                Buffer<string> words = default;
                string original = new('x', 3);
                string replacement = new('y', 3);
                words[1] = original;
                ReadOnlySpan<string> readOnly = Cases.ReturnReadOnly(in words, "tag");
                Check(readOnly.Length == 4 && ReferenceEquals(readOnly[1], original), "generic readonly identity");
                Span<string> writable = Cases.ReturnWritable(ref words, false);
                writable[1] = replacement;
                Check(ReferenceEquals(words[1], replacement), "generic writable alias identity");
                Check(Cases.DirectCount(words, "tag") == 4, "generic value foreach");
                Check(Cases.DirectCountIn(in words, false) == 4, "generic in foreach");
                Check(Cases.DirectCountRef(ref words, "tag") == 4, "generic ref foreach");
                Check(ReferenceEquals(Cases.ViaGeneric(in words, replacement, "tag"), replacement),
                    "generic helper readonly payload");
                Cases.ViaGenericWrite(ref words, original, false);
                Check(ReferenceEquals(words[1], original), "generic helper writable identity");
                try { _ = Cases.ReturnReadOnly(in words, false)[4]; throw new InvalidOperationException("generic readonly bounds accepted"); }
                catch (IndexOutOfRangeException) { }
                try { Cases.ReturnWritable(ref words, false)[4] = original; throw new InvalidOperationException("generic writable bounds accepted"); }
                catch (IndexOutOfRangeException) { }

                Buffer<int?> optional = default;
                Check(Cases.ReturnReadOnly(in optional, "tag")[0] is null, "physical nullable default");
                Cases.ReturnWritable(ref optional, false)[0] = 27;
                Check(optional[0] == 27 && Cases.ReturnReadOnly(in optional, "tag")[0] == 27,
                    "physical nullable writable/read-only return");
                Check(Cases.DirectCount(optional, false) == 4 && Cases.DirectCountIn(in optional, "tag") == 4
                    && Cases.DirectCountRef(ref optional, false) == 4, "physical nullable foreach");
                Cases.ViaGenericWrite(ref optional, (int?)31, "tag");
                Check(Cases.ViaGeneric(in optional, (int?)31, false) == 31, "physical nullable generic helper");
                return "PASS new string/int? span return alias, foreach, bounds";
            }
        }
        """;

    private const string ReturnOnlySource = """
        #nullable enable
        using System;
        using System.Runtime.CompilerServices;
        [InlineArray(4)] public struct Buffer<T> { private T _element0; }
        public static class ReturnOnly
        {
            public static ReadOnlySpan<int> Read(in Buffer<int> values, int ignored) => values;
            public static Span<int> Write(ref Buffer<int> values, int ignored) => values;
        }
        """;

    private const string ReturnOnlyRuntimeSource = """
        using System;
        public static class ReturnOnlyRuntime
        {
            public static string Run()
            {
                Buffer<string> words = default;
                words[1] = "first";
                ReadOnlySpan<string> view = ReturnOnly.Read(in words, "tag");
                if (view[1] != "first") throw new InvalidOperationException("C#12 readonly payload");
                ReturnOnly.Write(ref words, false)[1] = "second";
                if (words[1] != "second") throw new InvalidOperationException("C#12 writable alias");
                return "PASS C#12 applied Span/ReadOnlySpan returns and new string payload";
            }
        }
        """;

    private const string NegativeSource = """
        #nullable enable
        using System;

        namespace Mimic
        {
            [AttributeUsage(AttributeTargets.Struct)]
            public sealed class InlineArrayAttribute : Attribute
            {
                public InlineArrayAttribute(int length) { Length = length; }
                public int Length { get; }
            }

            public readonly struct Span<T>
            {
                public Span(T value) { Value = value; }
                public T Value { get; }
            }
        }

        [Mimic.InlineArray(4)]
        public struct FakeBuffer<T>
        {
            public T First;
            public static implicit operator ReadOnlySpan<T>(FakeBuffer<T> value) => new T[] { value.First };
            public static implicit operator Mimic.Span<T>(FakeBuffer<T> value) => new(value.First);
        }

        public static class NegativeConversions
        {
            public static ReadOnlySpan<int> UserToRealSpan(FakeBuffer<int> values, int ignored) => values;
            public static Mimic.Span<int> UserToFakeSpan(FakeBuffer<int> values, int ignored) => values;
        }
        """;

    private const string NegativeRuntimeSource = """
        using System;
        public static class NegativeRuntime
        {
            public static string Run()
            {
                var source = new FakeBuffer<int> { First = 7 };
                if (NegativeConversions.UserToRealSpan(source, 0)[0] != 7
                    || NegativeConversions.UserToFakeSpan(source, 0).Value != 7)
                { throw new InvalidOperationException("User-defined conversion changed."); }
                return "PASS unrelated InlineArrayAttribute/Span<T> and user conversion";
            }
        }
        """;
}
