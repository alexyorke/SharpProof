using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class ControlFlowRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ControlFlowFixes", "ControlFlowFixes", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Probe.cs", """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Threading.Tasks;
            public sealed class Flag {
                public static int Checks;
                public static implicit operator bool(Flag value) {Checks++;return true;}
            }
            public sealed class Truth {
                public static int Checks;
                public static bool operator true(Truth value) {Checks++;return true;}
                public static bool operator false(Truth value) {Checks++;return false;}
            }
            public sealed class IndentedProbe {
                /* retained declaration */ int Identity(int x)=>x;
            }
            public static class ControlFlowProbe {
                public static int Trace;
                public static int Forward(int x) {goto Done;Done:return x;}
                public static int Back(int count,int x) {Again:if(count-->0)goto Again;return x;}
                public static int Choose(int tag,int x,int y) {switch(tag) {case 0:goto case 1;case 1:return x;default:return y;} }
                public static int Finally(int x) {try{goto Done;}finally{Trace++;}Done:return x;}
                public static int Resource(MemoryStream stream,int x) {using(stream){goto Done;}Done:return x;}
                public static int Infinite(int x) {while(true)return x;}
                public static long Typed(int x,bool ignored) {long value=x;return value;}
                public static long Uninitialized(int x,bool ignored) {long value;goto Assign;Assign:value=x;return value;}
                public static int Constant(int tag,int x,int y) {const int Route=1;switch(tag){case 0:goto case Route;case Route:return x;default:return y;} }
                public static Func<int> Nested(int x)=>()=>{for(;;)return x;};
                public static async Task<int> Async(int x) {await Task.Yield();goto Done;Done:return x;}
                public static IEnumerable<int> Iterate(int x) {goto Done;Done:yield return x;}
                public static bool Boxing(int x,bool ignored) {object value=x;return object.ReferenceEquals(value,value);}
                public static int Boolean(Flag flag,int x) {if(flag)return x;return x;}
                public static int Operator(Truth flag,int x) {while(flag)return x;return x;}
                public static int Filter(Flag flag,int x) {try{throw new Exception();}catch(Exception)when(flag){return x;} }
            }
            """);
        var provider = new HmSignatureCodeFixProvider();
        var applied = 0;
        for (var round = 0; round < 21; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001"; });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            applied++;
        }
        if (applied != 17)
        { throw new InvalidOperationException($"Expected seventeen control-flow code fixes; applied {applied}."); }
        var finalCompilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var problems = finalCompilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (problems.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", problems.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        using var image = new MemoryStream();
        var emitted = finalCompilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        var probe = System.Reflection.Assembly.Load(image.ToArray()).GetType("ControlFlowProbe")!;
        var marker = Guid.NewGuid();
        var other = Guid.NewGuid();
        foreach (var name in new[] { "Forward", "Infinite", "Finally" })
        {
            if (!Equals(probe.GetMethod(name)!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker }), marker))
            { throw new InvalidOperationException("Control-flow result changed: " + name); }
        }
        if (!Equals(probe.GetField("Trace")!.GetValue(null), 1)
            || !Equals(probe.GetMethod("Back")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { 3, marker }), marker))
        { throw new InvalidOperationException("Backward goto or pending finalizer changed."); }
        foreach (var name in new[] { "Choose", "Constant" })
        {
            var method = probe.GetMethod(name)!.MakeGenericMethod(typeof(Guid));
            if (!Equals(method.Invoke(null, new object[] { 0, marker, other }), marker) || !Equals(method.Invoke(null, new object[] { 2, marker, other }), other))
            { throw new InvalidOperationException("Goto case routing changed: " + name); }
        }
        Program.Log.Info("PASS GotoLoopCaseAndFinallyRuntime");
        using var resource = new MemoryStream();
        if (!Equals(probe.GetMethod("Resource")!.MakeGenericMethod(typeof(MemoryStream), typeof(Guid)).Invoke(null, new object[] { resource, marker }), marker) || resource.CanRead)
        { throw new InvalidOperationException("Goto bypassed resource disposal."); }
        Program.Log.Info("PASS GotoResourceDisposalRuntime");
        foreach (var name in new[] { "Typed", "Uninitialized" })
        {
            if (!Equals(probe.GetMethod(name)!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { int.MaxValue, marker }), (long)int.MaxValue))
            { throw new InvalidOperationException("Local declaration conversion changed: " + name); }
        }
        if (!Equals(probe.GetMethod("Boxing")!.MakeGenericMethod(typeof(int), typeof(Guid)).Invoke(null, new object[] { 42, marker }), true))
        { throw new InvalidOperationException("Typed local boxing identity changed."); }
        if (!Equals(probe.GetMethod("Boxing")!.MakeGenericMethod(typeof(decimal), typeof(Guid)).Invoke(null, new object[] { 23m, marker }), true))
        { throw new InvalidOperationException("Typed local boxing lost its new struct domain."); }
        Program.Log.Info("PASS TypedUninitializedLocalConversionsRuntime");
        if (((Func<Guid>)probe.GetMethod("Nested")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!)() != marker
            || await ((Task<Guid>)probe.GetMethod("Async")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!).WaitAsync(cancellationToken).ConfigureAwait(false) != marker
            || !((IEnumerable<Guid>)probe.GetMethod("Iterate")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!).SequenceEqual(new[] { marker }))
        { throw new InvalidOperationException("Lambda, async or iterator goto result changed."); }
        Program.Log.Info("PASS LambdaAsyncIteratorControlFlowRuntime");
        var flagType = probe.Assembly.GetType("Flag")!;
        var flag = Activator.CreateInstance(flagType)!;
        var truthType = probe.Assembly.GetType("Truth")!;
        var truth = Activator.CreateInstance(truthType)!;
        if (!Equals(probe.GetMethod("Boolean")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { flag, marker }), marker)
            || !Equals(probe.GetMethod("Filter")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { flag, marker }), marker)
            || !Equals(probe.GetMethod("Operator")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { truth, marker }), marker)
            || !Equals(flagType.GetField("Checks")!.GetValue(null), 2) || !Equals(truthType.GetField("Checks")!.GetValue(null), 1))
        { throw new InvalidOperationException("Boolean conversion or true operator side effects changed."); }
        var indentedType = probe.Assembly.GetType("IndentedProbe")!;
        var indented = Activator.CreateInstance(indentedType)!;
        var identity = indentedType.GetMethod("Identity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.MakeGenericMethod(typeof(Guid));
        var text = (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        if (!Equals(identity.Invoke(indented, new object[] { marker }), marker) || !text.Contains("/* retained declaration */", StringComparison.Ordinal))
        { throw new InvalidOperationException("Indented declaration code fix or trivia preservation failed."); }
        Program.Log.Info("PASS IndentedDeclarationCodeFixRuntime");
        Program.Log.Info("PASS BooleanConversionAndTrueOperatorRuntime");
        return 6;
    }
}
