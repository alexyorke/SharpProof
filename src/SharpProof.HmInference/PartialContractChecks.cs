using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class PartialContractChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Definition, string Implementation)[] cases =
        [
            ("ArrayNullable", "string?[]? Echo(string?[]? value,int tag)", "string[] Echo(string[] value,int tag)=>value"),
            ("ArrayDynamic", "(dynamic[],int) Echo(object[] value,int tag)", "(object[],int) Echo(object[] value,int tag)=>(value,tag)"),
            ("ContainerDynamic", "(System.Collections.Generic.List<dynamic>,int) Echo(System.Collections.Generic.List<object> value,int tag)", "(System.Collections.Generic.List<object>,int) Echo(System.Collections.Generic.List<object> value,int tag)=>(value,tag)"),
            ("NestedOwner", "(Owner<dynamic>.Inner<string?>?,int) Echo(Owner<dynamic>.Inner<string?>? value,int tag)", "(Owner<object>.Inner<string>,int) Echo(Owner<object>.Inner<string> value,int tag)=>(value,tag)"),
            ("AwaitNullable", "System.Threading.Tasks.Task<string?> Echo(System.Threading.Tasks.Task<string?> value,int tag)", "async System.Threading.Tasks.Task<string> Echo(System.Threading.Tasks.Task<string> value,int tag)=>await value"),
            ("AwaitDynamic", "System.Threading.Tasks.Task<dynamic> Echo(System.Threading.Tasks.Task<object> value,int tag)", "async System.Threading.Tasks.Task<object> Echo(System.Threading.Tasks.Task<object> value,int tag)=>await value"),
            ("ExistingNullableParameter", "U? Echo<U>(U value,int tag)", "T Echo<T>(T value,int tag)=>value"),
            ("ValueNullable", "(int?,string?) Echo(int? value,string other,int tag)", "(int?,string) Echo(int? value,string other,int tag)=>(value,other)"),

            ("NullableDefinition", "string? Echo(string? value,int tag)", "string Echo(string value,int tag)=>value"),
            ("NullableImplementation", "string Echo(string value,int tag)", "string? Echo(string? value,int tag)=>value"),
            ("NestedNullable", "System.Collections.Generic.List<string?>? Echo(System.Collections.Generic.List<string?>? value,int tag)", "System.Collections.Generic.List<string> Echo(System.Collections.Generic.List<string> value,int tag)=>value"),
            ("DynamicReturn", "dynamic Echo(object value,int tag)", "object Echo(object value,int tag)=>value"),
            ("DynamicParameter", "object Echo(dynamic value,int tag)", "object Echo(object value,int tag)=>value"),
            ("DynamicImplementation", "object Echo(object value,int tag)", "dynamic Echo(dynamic value,int tag)=>value"),
            ("NestedDynamicReturn", "(dynamic,int) Echo(object value,int tag)", "(object,int) Echo(object value,int tag)=>(value,tag)")
        ];
        var failures = new List<string>();
        foreach (var test in cases)
        {
            foreach (var sameFile in new[] { false, true })
            {
                using var workspace = new AdhocWorkspace();
                var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "PartialContract" + test.Name, "PartialContract" + test.Name, LanguageNames.CSharp,
                    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, warningLevel: 9999), metadataReferences: Program.Compile([]).References));
                var definitionText = "#nullable enable\npublic partial class Probe{public static partial " + test.Definition + ";public class Owner<T>{public class Inner<U>{}}}";
                var implementationSignature = test.Implementation.StartsWith("async ", StringComparison.Ordinal)
                    ? string.Concat("async partial ", test.Implementation.AsSpan(6)) : "partial " + test.Implementation;
                var implementationText = "#nullable enable\npublic partial class Probe{public static " + implementationSignature + ";}";
                var definition = project.AddDocument("Definition.cs", sameFile ? definitionText + "\n" + implementationText : definitionText);
                var implementation = sameFile ? definition : definition.Project.AddDocument("Implementation.cs", implementationText);
                var original = (await implementation.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
                var errors = original.GetDiagnostics(cancellationToken).Where(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return Inference.IsCompilationError(diagnostic); }).ToArray();
                if (errors.Length != 0)
                { throw new InvalidOperationException("Invalid contract fixture " + test.Name + ": " + errors[0]); }
                var before = original.GetTypeByMetadataName("Probe")!.GetMembers("Echo").OfType<IMethodSymbol>().Single();

                var provider = new HmSignatureCodeFixProvider();
                var context = new FixAllContext(implementation, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
                    new CodeFixChecks.DiagnosticSource(), cancellationToken);
                var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
                var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
                var changed = (await solution.GetDocument(implementation.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
                var after = changed.GetTypeByMetadataName("Probe")!.GetMembers("Echo").OfType<IMethodSymbol>().Single();
                var beforeMetadata = EmittedMethod(original, cancellationToken);
                var afterMetadata = EmittedMethod(changed, cancellationToken);
                if (!after.IsGenericMethod || Flags(before.ReturnType) != Flags(after.ReturnType)
                    || Flags(before.Parameters[0].Type) != Flags(after.Parameters[0].Type)
                    || Flags(before.PartialImplementationPart!.ReturnType) != Flags(after.PartialImplementationPart!.ReturnType)
                    || Flags(before.PartialImplementationPart.Parameters[0].Type) != Flags(after.PartialImplementationPart.Parameters[0].Type)
                    || Flags(beforeMetadata.ReturnType) != Flags(afterMetadata.ReturnType)
                    || Flags(beforeMetadata.Parameters[0].Type) != Flags(afterMetadata.Parameters[0].Type))
                { failures.Add(test.Name + (sameFile ? " SameFile" : " CrossFile") + ": lost contract flags or suggestion: " + Flags(before.ReturnType) + "/" + Flags(before.Parameters[0].Type) + " -> " + Flags(after.ReturnType) + "/" + Flags(after.Parameters[0].Type)); }
                else
                { Program.Log.Info("PASS PartialContract" + test.Name + (sameFile ? "SameFile" : "CrossFile")); }
            }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return cases.Length * 2 + await PartialContractRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }

    private static IMethodSymbol EmittedMethod(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Partial contract emission failed: " + string.Join("\n", emitted.Diagnostics)); }
        var reference = MetadataReference.CreateFromImage(image.ToArray());
        var metadata = CSharpCompilation.Create("PartialContractMetadata", references: compilation.References.Concat([reference]),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.All));
        var assembly = (IAssemblySymbol)metadata.GetAssemblyOrModuleSymbol(reference)!;
        return assembly.GetTypeByMetadataName("Probe")!.GetMembers("Echo").OfType<IMethodSymbol>().Single();
    }

    private static string Flags(ITypeSymbol type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var top = (type.TypeKind == TypeKind.Dynamic ? "dynamic" : "static") + ":" + type.NullableAnnotation;
        return type switch
        {
            IArrayTypeSymbol array => top + "[" + Flags(array.ElementType) + "]",
            INamedTypeSymbol { IsTupleType: true } tuple => top + "(" + string.Join(",", tuple.TupleElements.Select(field =>
                { ArgumentNullException.ThrowIfNull(field); return Flags(field.Type); })) + ")",
            INamedTypeSymbol { IsGenericType: true } named => top + (named.ContainingType is { IsGenericType: true } owner ? "{" + Flags(owner) + "}" : "") + "<" + string.Join(",", named.TypeArguments.Select(Flags)) + ">",
            _ => top
        };
    }
}
