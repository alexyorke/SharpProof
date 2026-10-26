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

internal static class InlineArrayChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        RunAsync(cancellationToken).GetAwaiter().GetResult();
        return 5;
    }

    private static async Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "InlineArrayIDE", "InlineArrayIDE", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
            metadataReferences: Program.Compile([]).References));
        var fixture = project.AddDocument("Fixture.cs", FixtureSource);
        var runtime = fixture.Project.AddDocument("RuntimeCheck.cs", RuntimeSource);
        fixture = runtime.Project.GetDocument(fixture.Id)!;
        await CheckAndRun(fixture.Project, "original", cancellationToken: cancellationToken).ConfigureAwait(false);

        var diagnostics = await Diagnostics(fixture, cancellationToken).ConfigureAwait(false);
        string[] required = ["ReadAt", "WriteAt", "AliasAt", "SliceAt", "ReadOut", "SliceOut",
            "ReadIn", "AliasIn", "SliceIn", "SliceRef", "ConditionalValue", "ConditionalReference",
            "ReadReference", "ReadNullable", "ReadAnnotated", "ReadFromEnd", "ReadIndex",
            "ReadIndexCount", "OpenRange", "OpenReadonlyRange"];
        var root = (await fixture.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        foreach (var name in required)
        {
            var method = FindMethod(root, name);
            if (!diagnostics.Any(d =>
            { ArgumentNullException.ThrowIfNull(d); return d.Location.SourceSpan == method.Identifier.Span; }))
            { throw new InvalidOperationException("Missing HM0001: " + name); }
        }
        Program.Log.Info("PASS analyzer HM0001 for " + required.Length + " inline-array forms; no AD0001");

        var provider = new HmSignatureCodeFixProvider();
        var conditional = FindMethod(root, "ConditionalValue");
        var diagnostic = diagnostics.Single(d =>
        { ArgumentNullException.ThrowIfNull(d); return d.Location.SourceSpan == conditional.Identifier.Span; });
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(fixture, diagnostic,
            (action, _) =>
            { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        if (actions.Count != 1)
        { throw new InvalidOperationException("Conditional inline-array IDE action missing."); }
        var oneSolution = await ChangedSolution(actions.Single(), cancellationToken).ConfigureAwait(false);
        var oneDocument = oneSolution.GetDocument(fixture.Id)!;
        await CheckAndRun(oneDocument.Project, "single IDE fix", cancellationToken: cancellationToken).ConfigureAwait(false);
        var oneText = (await oneDocument.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        if (!oneText.Contains("where T1 : struct", StringComparison.Ordinal))
        { throw new InvalidOperationException("Conditional value fix lost struct bound."); }
        Program.Log.Info("PASS applied IDE conditional fix with struct bound and runtime");

        var context = new FixAllContext(fixture, provider, FixAllScope.Document, "HM.GeneralizeSignature",
            provider.FixableDiagnosticIds, new DiagnosticSource(), cancellationToken);
        var allAction = await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Inline-array Fix All action missing.");
        var allDocument = (await ChangedSolution(allAction, cancellationToken).ConfigureAwait(false)).GetDocument(fixture.Id)!;
        await CheckAndRun(allDocument.Project, "document Fix All", cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = (await allDocument.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        var changedRoot = (await allDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        foreach (var name in required)
        {
            if (FindMethod(changedRoot, name).TypeParameterList is null)
            { throw new InvalidOperationException("Fix All missed " + name); }
        }
        if (!text.Contains("ReadOnlySpan<T1>", StringComparison.Ordinal)
            || !text.Contains("Span<T1>", StringComparison.Ordinal)
            || !text.Contains("where T1 : struct", StringComparison.Ordinal)
            || !text.Contains("where T1 : class", StringComparison.Ordinal))
        { throw new InvalidOperationException("Fix All lost span shape or nullable bounds."); }
        var generalized = allDocument.Project.AddDocument("GeneralizedRuntimeCheck.cs", GeneralizedRuntimeSource);
        await CheckAndRun(generalized.Project, "new generic payloads", "GeneralizedRuntimeCheck", cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS document Fix All applied " + required.Length + " signatures and runtime");
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
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;

        [InlineArray(4)] public struct Buffer<T> { private T _element0; }
        [InlineArray(4)] public struct AnnotatedBuffer<T> { [MaybeNull] private T _element0; }
        public static class Counter
        {
            public static int Calls;
            public static int Index() { Calls++; return 1; }
            public static int Begin() { Calls++; return 0; }
            public static int End() { Calls++; return 2; }
            public static int TooFar() { Calls++; return 5; }
            public static Index FromEnd() { Calls++; return ^1; }
        }
        public static class Forms
        {
            public static int ReadAt(Buffer<int> values, int ignored) => values[Counter.Index()];
            public static int WriteAt(ref Buffer<int> values, int replacement, int ignored)
            { values[Counter.Index()] = replacement; return replacement; }
            public static ref int AliasAt(ref Buffer<int> values, int ignored) => ref values[Counter.Index()];
            public static Span<int> SliceAt(ref Buffer<int> values, int ignored) => values[Counter.Begin()..Counter.End()];
            public static int ReadOut(Buffer<int> values, int ignored) => values[Counter.TooFar()];
            public static Span<int> SliceOut(ref Buffer<int> values, int ignored) => values[Counter.Begin()..Counter.TooFar()];
            public static int ReadIn(in Buffer<int> values, int index, int ignored) => values[index];
            public static ref readonly int AliasIn(in Buffer<int> values, int index, int ignored) => ref values[index];
            public static ReadOnlySpan<int> SliceIn(in Buffer<int> values, Range range, int ignored) => values[range];
            public static Span<int> SliceRef(ref Buffer<int> values, Range range, int ignored) => values[range];
            public static int? ConditionalValue(Buffer<int>? values, int index, int ignored) => values?[index];
            public static string? ConditionalReference(Buffer<string>? values, int index, int ignored) => values?[index];
            public static string? ReadReference(Buffer<string?> values, int ignored) => values[0];
            public static int? ReadNullable(Buffer<int?> values, int ignored) => values[0];
            public static string? ReadAnnotated(AnnotatedBuffer<string> values, int ignored) => values[0];
            public static T? ExistingUnconstrained<T>(Buffer<T?> values, int index) => values[index];
            public static int ReadFromEnd(Buffer<int> values, int ignored) => values[^1];
            public static int ReadIndex(Buffer<int> values, Index index, int ignored) => values[index];
            public static int ReadIndexCount(Buffer<int> values, int ignored) => values[Counter.FromEnd()];
            public static Span<int> OpenRange(ref Buffer<int> values, int ignored) => values[1..];
            public static ReadOnlySpan<int> OpenReadonlyRange(in Buffer<int> values, int ignored) => values[..2];
        }
        """;

    private const string RuntimeSource = """
        #nullable enable
        using System;
        using System.Reflection;

        public static class RuntimeCheck
        {
            private static void Check(bool condition, string name)
            { if (!condition) throw new InvalidOperationException(name); }

            private static object? Conditional(string name, Type payload, object? values, int index)
            {
                var method = typeof(Forms).GetMethod(name)!;
                if (method.IsGenericMethodDefinition)
                { method = method.MakeGenericMethod(payload, typeof(int)); }
                return method.Invoke(null, new[] { values, (object)index, 0 });
            }

            private static Type? ReadFailure(Buffer<int> values)
            {
                try { _ = Forms.ReadOut(values, 0); return null; }
                catch (Exception error) { return error.GetType(); }
            }

            private static Type? SliceFailure(ref Buffer<int> values)
            {
                try { _ = Forms.SliceOut(ref values, 0).Length; return null; }
                catch (Exception error) { return error.GetType(); }
            }

            public static string Run()
            {
                Buffer<int> numbers = default;
                numbers[1] = 7;
                Counter.Calls = 0;
                Check(Forms.ReadAt(numbers, 0) == 7 && Counter.Calls == 1, "read count");
                Counter.Calls = 0;
                Check(Forms.WriteAt(ref numbers, 8, 0) == 8 && numbers[1] == 8 && Counter.Calls == 1, "write and count");
                Counter.Calls = 0;
                ref int alias = ref Forms.AliasAt(ref numbers, 0);
                alias = 9;
                Check(numbers[1] == 9 && Counter.Calls == 1, "ref alias identity");
                Counter.Calls = 0;
                Forms.SliceAt(ref numbers, 0)[1] = 10;
                Check(numbers[1] == 10 && Counter.Calls == 2, "writable range and endpoint counts");
                Check(Forms.ReadIn(in numbers, 1, 0) == 10, "in read");
                ref readonly int readonlyAlias = ref Forms.AliasIn(in numbers, 1, 0);
                Check(readonlyAlias == 10, "ref readonly alias");
                ReadOnlySpan<int> readonlySlice = Forms.SliceIn(in numbers, 0..2, 0);
                Check(readonlySlice.Length == 2 && readonlySlice[1] == 10, "readonly span");
                Span<int> writableSlice = Forms.SliceRef(ref numbers, 0..2, 0);
                writableSlice[1] = 11;
                Check(numbers[1] == 11, "writable span identity");
                Counter.Calls = 0;
                Check(ReadFailure(numbers) == typeof(IndexOutOfRangeException) && Counter.Calls == 1, "index bounds and count");
                Counter.Calls = 0;
                Check(SliceFailure(ref numbers) == typeof(ArgumentOutOfRangeException) && Counter.Calls == 2, "range bounds and counts");

                Check(Conditional("ConditionalValue", typeof(int), numbers, 1) is int value && value == 11, "value conditional");
                Check(Conditional("ConditionalValue", typeof(int), null, 1) is null, "value conditional null receiver");
                Buffer<string> words = default;
                var word = new string('w', 3);
                words[1] = word;
                Check(ReferenceEquals(Conditional("ConditionalReference", typeof(string), words, 1), word), "reference conditional identity");
                Check(Conditional("ConditionalReference", typeof(string), null, 1) is null, "reference conditional null receiver");
                Buffer<string?> maybeWords = default;
                Check(Forms.ReadReference(maybeWords, 0) is null, "reference null element");
                maybeWords[0] = word;
                Check(ReferenceEquals(Forms.ReadReference(maybeWords, 0), word), "reference element identity");
                Check(ReferenceEquals(Forms.ExistingUnconstrained<string>(maybeWords, 0), word), "existing unconstrained reference");
                Check(Forms.ExistingUnconstrained<int>(numbers, 1) == 11, "existing unconstrained value");
                numbers[3] = 19;
                Check(Forms.ReadFromEnd(numbers, 0) == 19, "from-end literal");
                Check(Forms.ReadIndex(numbers, ^1, 0) == 19, "Index argument");
                Counter.Calls = 0;
                Check(Forms.ReadIndexCount(numbers, 0) == 19 && Counter.Calls == 1, "Index evaluation count");
                Span<int> open = Forms.OpenRange(ref numbers, 0);
                Check(open.Length == 3 && open[0] == 11, "open-ended writable range");
                open[0] = 12;
                Check(numbers[1] == 12, "open-ended writable identity");
                ReadOnlySpan<int> openReadonly = Forms.OpenReadonlyRange(in numbers, 0);
                Check(openReadonly.Length == 2 && openReadonly[1] == 12, "open-ended readonly range");
                try { _ = Forms.ReadIndex(numbers, ^0, 0); throw new InvalidOperationException("Index ^0 accepted"); }
                catch (IndexOutOfRangeException) { }
                Buffer<int?> maybeNumbers = default;
                Check(Forms.ReadNullable(maybeNumbers, 0) is null, "physical nullable empty");
                maybeNumbers[0] = 27;
                Check(Forms.ReadNullable(maybeNumbers, 0) == 27, "physical nullable value");
                AnnotatedBuffer<string> annotated = default;
                Check(Forms.ReadAnnotated(annotated, 0) is null, "MaybeNull default");
                annotated[0] = word;
                Check(ReferenceEquals(Forms.ReadAnnotated(annotated, 0), word), "MaybeNull payload");
                return "PASS int/reference/physical-nullable, ref/in, spans, conditional, counts, identity, bounds";
            }
        }
        """;

    private const string GeneralizedRuntimeSource = """
        #nullable enable
        using System;

        public static class GeneralizedRuntimeCheck
        {
            private static void Check(bool condition, string name)
            { if (!condition) throw new InvalidOperationException(name); }

            private static Type? ReadFailure(Buffer<object> values)
            {
                try { _ = Forms.ReadOut(values, "ignored"); return null; }
                catch (Exception error) { return error.GetType(); }
            }

            private static Type? SliceFailure(ref Buffer<int?> values)
            {
                try { _ = Forms.SliceOut(ref values, false).Length; return null; }
                catch (Exception error) { return error.GetType(); }
            }

            public static string Run()
            {
                Buffer<object> objects = default;
                object first = new();
                objects[1] = first;
                Counter.Calls = 0;
                Check(ReferenceEquals(Forms.ReadAt(objects, "tag"), first) && Counter.Calls == 1, "new reference read/count");
                object second = new();
                Counter.Calls = 0;
                Check(ReferenceEquals(Forms.WriteAt(ref objects, second, false), second)
                    && ReferenceEquals(objects[1], second) && Counter.Calls == 1, "new reference write/count");
                Counter.Calls = 0;
                ref object alias = ref Forms.AliasAt(ref objects, "tag");
                object third = new();
                alias = third;
                Check(ReferenceEquals(objects[1], third) && Counter.Calls == 1, "new reference alias identity");
                Counter.Calls = 0;
                Forms.SliceAt(ref objects, false)[1] = first;
                Check(ReferenceEquals(objects[1], first) && Counter.Calls == 2, "new reference writable slice/count");
                Check(ReferenceEquals(Forms.ReadIn(in objects, 1, "tag"), first), "new reference in read");
                ref readonly object readonlyAlias = ref Forms.AliasIn(in objects, 1, false);
                Check(ReferenceEquals(readonlyAlias, first), "new reference ref readonly alias");
                ReadOnlySpan<object> readOnly = Forms.SliceIn(in objects, 0..2, "tag");
                Check(ReferenceEquals(readOnly[1], first), "new reference readonly slice");
                Span<object> writable = Forms.SliceRef(ref objects, 0..2, false);
                writable[1] = second;
                Check(ReferenceEquals(objects[1], second), "new reference writable slice identity");
                objects[3] = first;
                Check(ReferenceEquals(Forms.ReadFromEnd(objects, "tag"), first), "new reference from-end");
                Check(ReferenceEquals(Forms.ReadIndex(objects, ^1, false), first), "new reference Index");
                Counter.Calls = 0;
                Check(ReferenceEquals(Forms.ReadIndexCount(objects, "tag"), first) && Counter.Calls == 1,
                    "new reference Index evaluation");
                Span<object> open = Forms.OpenRange(ref objects, false);
                open[0] = first;
                Check(ReferenceEquals(objects[1], first), "new reference open range identity");
                ReadOnlySpan<object> openReadonly = Forms.OpenReadonlyRange(in objects, "tag");
                Check(ReferenceEquals(openReadonly[1], first), "new reference open readonly range");
                Counter.Calls = 0;
                Check(ReadFailure(objects) == typeof(IndexOutOfRangeException) && Counter.Calls == 1, "new reference bounds/count");

                Buffer<int?> optional = default;
                Check(Forms.ReadAt(optional, "tag") is null, "new physical nullable default");
                Counter.Calls = 0;
                Check(Forms.WriteAt(ref optional, 5, false) == 5 && optional[1] == 5 && Counter.Calls == 1,
                    "new physical nullable write/count");
                Counter.Calls = 0;
                ref int? optionalAlias = ref Forms.AliasAt(ref optional, "tag");
                optionalAlias = 6;
                Check(optional[1] == 6 && Counter.Calls == 1, "new physical nullable alias");
                Counter.Calls = 0;
                Forms.SliceAt(ref optional, false)[1] = null;
                Check(optional[1] is null && Counter.Calls == 2, "new physical nullable range/count");
                optional[3] = 29;
                Check(Forms.ReadFromEnd(optional, "tag") == 29, "new physical nullable from-end");
                Check(Forms.ReadIndex(optional, ^1, false) == 29, "new physical nullable Index");
                Forms.OpenRange(ref optional, "tag")[0] = 8;
                Check(optional[1] == 8, "new physical nullable open range");
                Counter.Calls = 0;
                Check(SliceFailure(ref optional) == typeof(ArgumentOutOfRangeException) && Counter.Calls == 2,
                    "new physical nullable range bounds/count");

                Buffer<long> longs = default;
                longs[1] = 41;
                Check(Forms.ConditionalValue<long, string>(longs, 1, "tag") == 41,
                    "new constrained value conditional");
                Check(Forms.ConditionalValue<long, string>(null, 1, "tag") is null,
                    "new constrained value conditional null receiver");
                Check(ReferenceEquals(Forms.ConditionalReference<object, string>(objects, 1, "tag"), first),
                    "new constrained reference conditional");
                Check(Forms.ConditionalReference<object, string>(null, 1, "tag") is null,
                    "new constrained reference conditional null receiver");
                AnnotatedBuffer<object> annotated = default;
                Check(Forms.ReadAnnotated(annotated, "tag") is null, "new annotated default");
                annotated[0] = first;
                Check(ReferenceEquals(Forms.ReadAnnotated(annotated, "tag"), first), "new annotated identity");
                return "PASS new object/int? payloads, constraints, counts, alias, slices, bounds";
            }
        }
        """;
}
