using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class PartialContractRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string definitions = """
            #nullable enable
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public partial class ContractProbe{
                public static partial dynamic Box(object value,int tag);
                public static partial object Static(dynamic value,int tag);
                public static partial string? Nullable(string? value,int tag);
                public static partial List<string?>? List(List<string?>? value,int tag);
                public static partial Task<string?> Await(Task<string?> value,int tag);
            }
            """;
        const string implementations = """
            #nullable enable
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public partial class ContractProbe{
                public static partial object Box(object value,int tag)=>value;
                public static partial object Static(object value,int tag)=>value;
                public static partial string Nullable(string value,int tag)=>value;
                public static partial List<string> List(List<string> value,int tag)=>value;
                public static async partial Task<string> Await(Task<string> value,int tag)=>await value;
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "PartialContractRuntime", "PartialContractRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var definition = project.AddDocument("Definition.cs", definitions);
        var implementation = definition.Project.AddDocument("Implementation.cs", implementations);
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(implementation, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var compilation = (await solution.GetDocument(implementation.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Partial contract runtime emission: " + string.Join("\n", emitted.Diagnostics)); }
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("PartialContractRuntime", isCollectible: true);
        try
        {
            image.Position = 0;
            var probe = loadContext.LoadFromStream(image).GetType("ContractProbe")!;
            var marker = new Uri("https://example.invalid/contract");
            var box = probe.GetMethod("Box")!;
            var direct = probe.GetMethod("Static")!;
            var returnDynamic = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Runtime.CompilerServices.DynamicAttribute>(box.ReturnParameter);
            var parameterDynamic = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Runtime.CompilerServices.DynamicAttribute>(direct.GetParameters()[0]);
            if (returnDynamic is null || !returnDynamic.TransformFlags.SequenceEqual([true])
                || parameterDynamic is null || !parameterDynamic.TransformFlags.SequenceEqual([true])
                || !ReferenceEquals(box.MakeGenericMethod(typeof(decimal)).Invoke(null, [marker, 3m]), marker)
                || !ReferenceEquals(direct.MakeGenericMethod(typeof(decimal)).Invoke(null, [marker, 3m]), marker))
            { throw new InvalidOperationException("Partial dynamic metadata or runtime identity changed."); }
            var nullable = probe.GetMethod("Nullable")!.MakeGenericMethod(typeof(Uri), typeof(decimal));
            var list = probe.GetMethod("List")!.MakeGenericMethod(typeof(Uri), typeof(decimal));
            List<Uri?> values = [marker, null];
            if (!ReferenceEquals(nullable.Invoke(null, [marker, 3m]), marker) || nullable.Invoke(null, [null, 3m]) is not null
                || !ReferenceEquals(list.Invoke(null, [values, 3m]), values) || list.Invoke(null, [null, 3m]) is not null)
            { throw new InvalidOperationException("Partial nullable contracts lost values, nulls or list identity."); }
            Program.Log.Info("PASS PartialContractMetadataNullsIdentityAndNewDomains");

            var method = probe.GetMethod("Await")!.MakeGenericMethod(typeof(Uri), typeof(decimal));
            var completion = new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = (Task<Uri?>)method.Invoke(null, [completion.Task, 3m])!;
            if (pending.IsCompleted)
            { throw new InvalidOperationException("Partial contract await failed to suspend."); }
            completion.SetResult(marker);
            if (!ReferenceEquals(await pending.WaitAsync(cancellationToken).ConfigureAwait(false), marker))
            { throw new InvalidOperationException("Partial contract await lost payload identity."); }
            var nullCompletion = new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nullPending = (Task<Uri?>)method.Invoke(null, [nullCompletion.Task, 3m])!;
            nullCompletion.SetResult(null);
            if (await nullPending.WaitAsync(cancellationToken).ConfigureAwait(false) is not null)
            { throw new InvalidOperationException("Partial contract await lost null."); }
            var failure = new InvalidOperationException("partial-contract");
            var fault = (Task<Uri?>)method.Invoke(null, [Task.FromException<Uri?>(failure), 3m])!;
            try
            { await fault.WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Missing partial contract fault."); }
            catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure)) { }
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(false);
#pragma warning disable SPINT003 // Deliberately canceled fixture token, independent of test cancellation.
            var cancellation = (Task<Uri?>)method.Invoke(null, [Task.FromCanceled<Uri?>(canceled.Token), 3m])!;
#pragma warning restore SPINT003
            try
            { await cancellation.WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Missing partial contract cancellation."); }
            catch (OperationCanceledException exception) when (exception.CancellationToken == canceled.Token) { }
            Program.Log.Info("PASS PartialContractNullableAwaitSuspensionFaultAndCancellation");
            return 2;
        }
        finally { loadContext.Unload(); }
    }
}
