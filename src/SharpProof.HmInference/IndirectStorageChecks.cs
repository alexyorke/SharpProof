using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class IndirectStorageChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading;
            using System.Threading.Tasks;
            public sealed class IndirectBox<T>{
                public T Value;public T Other;public int Gets;
                public IndirectBox(T value,T other){Value=value;Other=other;}
                public ref T Slot{get{Gets++;return ref Value;}}
            }
            public sealed class IndirectPair<T,U>{
                public T Value;public U Other;
                public IndirectPair(T value,U other){Value=value;Other=other;}
            }
            public sealed class HintMarker{}
            public readonly struct HintInput{public static implicit operator HintMarker?(HintInput value)=>null;}
            public sealed class IndirectSource<T>{
                [return:MaybeNull]public T Read(T value)=>default;
                [MaybeNull]public T Property=>default;
                [MaybeNull]public T Priority{[return:NotNull]get=>default!;}
                [MaybeNull]public T Field=default!;
            }
            [return:MaybeNull]public delegate T IndirectReader<T>(T value);
            public static class IndirectProbe{
                [return:MaybeNull]public static T Maybe<T>(T value)=>default;
                public static T? Optional<T>(T value)=>default;
                public static int Field(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Maybe(fill);return box.Value;}
                public static int Chain(IndirectBox<int> box,int fill){ref var first=ref box.Value;ref var second=ref first;second=Maybe(fill);return box.Value;}
                public static int Element(int[] values,int fill){ref var alias=ref values[0];alias=Maybe(fill);return values[0];}
                public static int Property(IndirectBox<int> box,int fill){ref var alias=ref box.Slot;alias=Maybe(fill);return box.Value;}
                public static int Owner(IndirectBox<int> box,IndirectSource<int> source,int fill){ref var alias=ref box.Value;alias=source.Read(fill);return box.Value;}
                public static int Delegate(IndirectBox<int> box,IndirectReader<int> read,int fill){ref var alias=ref box.Value;alias=read(fill);return box.Value;}
                public static int SourceProperty(IndirectBox<int> box,IndirectSource<int> source){ref var alias=ref box.Value;alias=source.Property;return box.Value;}
                public static int PriorityProperty(IndirectBox<int> box,IndirectSource<int> source){ref var alias=ref box.Value;alias=source.Priority;return box.Value;}
                public static int SourceField(IndirectBox<int> box,IndirectSource<int> source){ref var alias=ref box.Value;alias=source.Field;return box.Value;}
                public static int OptionalStore(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Optional(fill);return box.Value;}
                public static int Copy(IndirectBox<int> box,int fill){var copy=box.Value;ref var alias=ref box.Value;alias=Maybe(fill);return copy;}
                public static int Sibling(IndirectPair<int,int> box,int fill){ref var alias=ref box.Value;alias=Maybe(fill);return box.Other;}
                public static int? Physical(IndirectBox<int?> box,int? fill){ref var alias=ref box.Value;alias=Maybe(fill);return box.Value;}
                public static async Task<int> Async(Task<int> fill,IndirectBox<int> box){var value=await fill;ref var alias=ref box.Value;alias=Maybe(value);return box.Value;}
                public static int Typed(IndirectBox<int> box,int fill){ref int alias=ref box.Value;alias=Maybe(fill);return box.Value;}
                [return:MaybeNull][return:NotNullIfNotNull(nameof(value))]
                public static T Guarded<T>(T value)=>value;
                [return:MaybeNull][return:NotNullIfNotNull("hint")]
                public static T Conditional<T>(T value,object? hint){if(hint is null)return default!;if(value is null)throw new InvalidOperationException();return value;}
                [return:MaybeNull][return:NotNullIfNotNull("hint")]
                public static T ConditionalHint<T>(T value,HintMarker? hint){if(hint is null)return default!;if(value is null)throw new InvalidOperationException();return value;}
                public static int ConditionalStore(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Conditional(fill,null);return box.Value;}
                public static int ConditionalDefault(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Conditional(fill,default(object?));return box.Value;}
                public static int ConditionalConstant(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Conditional(fill,"ready");return box.Value;}
                public static int ConditionalNew(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Conditional(fill,new object());return box.Value;}
                public static int ConditionalNullableNew(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Conditional(fill,new int?());return box.Value;}
                public static int ConditionalUserConversion(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=ConditionalHint(fill,new HintInput());return box.Value;}
                public static int UnknownGuardedStore(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Guarded(fill);return box.Value;}
                public static int ModifiedGuardedStore(IndirectBox<int> box,int fill){fill=default;ref var alias=ref box.Value;alias=Guarded(fill);return box.Value;}
                public static int NullableGuardedStore(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Guarded(Maybe(fill));return box.Value;}
                public static int GuardedStore(IndirectBox<int> box,int fill){ref var alias=ref box.Value;alias=Guarded(fill!);return box.Value;}
                public static int TupleGuarded(IndirectPair<int,string> box,int fill,string other){ref var alias=ref box.Value;(alias,box.Other)=(Guarded(fill),other);return box.Value;}
                public static int TupleOut(IndirectBox<int> box,int fill,string tag,out string copied){ref var alias=ref box.Value;(alias,copied)=(Guarded(fill),tag);return box.Value;}
                public static int ConditionalTuple(IndirectBox<int> box,int fill,string tag,bool choose,out string copied){ref var alias=ref box.Value;(alias,copied)=(choose?Guarded(fill):Guarded(fill),tag);return box.Value;}
                public static int SwitchTuple(IndirectBox<int> box,int fill,string tag,bool choose,out string copied){ref var alias=ref box.Value;(alias,copied)=(choose switch{true=>Guarded(fill),_=>Guarded(fill)},tag);return box.Value;}
                public static int ConditionalOptionalTuple(IndirectBox<int> box,int fill,string tag,bool choose,out string copied){ref var alias=ref box.Value;(alias,copied)=(choose?Guarded(fill):Maybe(fill),tag);return box.Value;}
                public static int SwitchOptionalTuple(IndirectBox<int> box,int fill,string tag,bool choose,out string copied){ref var alias=ref box.Value;(alias,copied)=(choose switch{true=>Guarded(fill),_=>Maybe(fill)},tag);return box.Value;}
                public static int ReboundNullableTuple(IndirectBox<int> box,int fill,string tag,out string copied){ref var alias=ref box.Value;(alias,copied)=(fill,tag);alias=ref box.Other;(alias,copied)=(Maybe(fill),tag);return box.Other;}
                public static int DirectField(IndirectBox<int> box,int fill){box.Value=Maybe(fill);return box.Value;}
                public static int DirectElement(int[] values,int fill){values[0]=Maybe(fill);return values[0];}
                public static int DirectProperty(IndirectBox<int> box,int fill){box.Slot=Maybe(fill);return box.Value;}
                public static int DirectFieldGuardNull(IndirectBox<int> box,int fill){box.Value=Conditional(fill,null);return box.Value;}
                public static int DirectFieldGuardUnknown(IndirectBox<int> box,int fill){box.Value=Guarded(fill);return box.Value;}
                public static int DirectFieldGuardModified(IndirectBox<int> box,int fill){fill=default;box.Value=Guarded(fill);return box.Value;}
            }
            public static class OriginalIndirectRunner{
                public static async Task<bool> Run(CancellationToken token){
                    var field=new IndirectBox<int>(7,11);if(IndirectProbe.Field(field,9)!=0||field.Value!=0||field.Other!=11)return false;
                    var chain=new IndirectBox<int>(7,11);if(IndirectProbe.Chain(chain,9)!=0||chain.Value!=0)return false;
                    var values=new[]{7};if(IndirectProbe.Element(values,9)!=0||values[0]!=0)return false;
                    var property=new IndirectBox<int>(7,11);if(IndirectProbe.Property(property,9)!=0||property.Value!=0||property.Gets!=1)return false;
                    var source=new IndirectSource<int>();var owner=new IndirectBox<int>(7,11);
                    if(IndirectProbe.Owner(owner,source,9)!=0||owner.Value!=0)return false;
                    var delegated=new IndirectBox<int>(7,11);IndirectReader<int> reader=IndirectProbe.Maybe;
                    if(IndirectProbe.Delegate(delegated,reader,9)!=0||delegated.Value!=0)return false;
                    var sourceProperty=new IndirectBox<int>(7,11);if(IndirectProbe.SourceProperty(sourceProperty,source)!=0||sourceProperty.Value!=0)return false;
                    var priority=new IndirectBox<int>(7,11);if(IndirectProbe.PriorityProperty(priority,source)!=0||priority.Value!=0)return false;
                    var sourceField=new IndirectBox<int>(7,11);if(IndirectProbe.SourceField(sourceField,source)!=0||sourceField.Value!=0)return false;
                    var conditional=new IndirectBox<int>(7,11);if(IndirectProbe.ConditionalStore(conditional,9)!=0||conditional.Value!=0)return false;
                    var conditionalDefault=new IndirectBox<int>(7,11);if(IndirectProbe.ConditionalDefault(conditionalDefault,9)!=0||conditionalDefault.Value!=0)return false;
                    var conditionalNullable=new IndirectBox<int>(7,11);if(IndirectProbe.ConditionalNullableNew(conditionalNullable,9)!=0||conditionalNullable.Value!=0)return false;
                    var conditionalUser=new IndirectBox<int>(7,11);if(IndirectProbe.ConditionalUserConversion(conditionalUser,9)!=0||conditionalUser.Value!=0)return false;
                    var unknownGuarded=new IndirectBox<int>(7,11);if(IndirectProbe.UnknownGuardedStore(unknownGuarded,9)!=9||unknownGuarded.Value!=9)return false;
                    var modifiedGuarded=new IndirectBox<int>(7,11);if(IndirectProbe.ModifiedGuardedStore(modifiedGuarded,9)!=0||modifiedGuarded.Value!=0)return false;
                    var tupleGuarded=new IndirectPair<int,string>(7,"before");if(IndirectProbe.TupleGuarded(tupleGuarded,9,"after")!=9||tupleGuarded.Value!=9||tupleGuarded.Other!="after")return false;
                    var tupleOut=new IndirectBox<int>(7,11);if(IndirectProbe.TupleOut(tupleOut,9,"tag",out var copied)!=9||tupleOut.Value!=9||copied!="tag")return false;
                    var conditionalTuple=new IndirectBox<int>(7,11);if(IndirectProbe.ConditionalTuple(conditionalTuple,9,"tag",false,out var conditionalCopied)!=9||conditionalTuple.Value!=9||conditionalCopied!="tag")return false;
                    var switchTuple=new IndirectBox<int>(7,11);if(IndirectProbe.SwitchTuple(switchTuple,9,"tag",true,out var switchCopied)!=9||switchTuple.Value!=9||switchCopied!="tag")return false;
                    var conditionalOptional=new IndirectBox<int>(7,11);if(IndirectProbe.ConditionalOptionalTuple(conditionalOptional,9,"tag",false,out var optionalCopied)!=0||conditionalOptional.Value!=0||optionalCopied!="tag")return false;
                    var switchOptional=new IndirectBox<int>(7,11);if(IndirectProbe.SwitchOptionalTuple(switchOptional,9,"tag",false,out var switchOptionalCopied)!=0||switchOptional.Value!=0||switchOptionalCopied!="tag")return false;
                    var reboundNullable=new IndirectBox<int>(7,11);if(IndirectProbe.ReboundNullableTuple(reboundNullable,9,"tag",out var reboundCopied)!=0||reboundNullable.Value!=9||reboundNullable.Other!=0||reboundCopied!="tag")return false;
                    var directField=new IndirectBox<int>(7,11);if(IndirectProbe.DirectField(directField,9)!=0||directField.Value!=0||directField.Other!=11)return false;
                    var directElement=new[]{7};if(IndirectProbe.DirectElement(directElement,9)!=0||directElement[0]!=0)return false;
                    var directProperty=new IndirectBox<int>(7,11);if(IndirectProbe.DirectProperty(directProperty,9)!=0||directProperty.Value!=0||directProperty.Gets!=1)return false;
                    var directNull=new IndirectBox<int>(7,11);if(IndirectProbe.DirectFieldGuardNull(directNull,9)!=0||directNull.Value!=0)return false;
                    var directUnknown=new IndirectBox<int>(7,11);if(IndirectProbe.DirectFieldGuardUnknown(directUnknown,9)!=9||directUnknown.Value!=9)return false;
                    var directModified=new IndirectBox<int>(7,11);if(IndirectProbe.DirectFieldGuardModified(directModified,9)!=0||directModified.Value!=0)return false;
                    var nullableGuarded=new IndirectBox<int>(7,11);if(IndirectProbe.NullableGuardedStore(nullableGuarded,9)!=0||nullableGuarded.Value!=0)return false;
                    var guarded=new IndirectBox<int>(7,11);if(IndirectProbe.GuardedStore(guarded,9)!=9||guarded.Value!=9)return false;
                    var conditionalConstant=new IndirectBox<int>(7,11);if(IndirectProbe.ConditionalConstant(conditionalConstant,9)!=9||conditionalConstant.Value!=9)return false;
                    var conditionalNew=new IndirectBox<int>(7,11);if(IndirectProbe.ConditionalNew(conditionalNew,9)!=9||conditionalNew.Value!=9)return false;
                    var optionalStore=new IndirectBox<int>(7,11);if(IndirectProbe.OptionalStore(optionalStore,9)!=0||optionalStore.Value!=0)return false;
                    var copy=new IndirectBox<int>(7,11);if(IndirectProbe.Copy(copy,9)!=7||copy.Value!=0)return false;
                    var sibling=new IndirectPair<int,int>(7,11);if(IndirectProbe.Sibling(sibling,9)!=11||sibling.Value!=0||sibling.Other!=11)return false;
                    var optional=new IndirectBox<int?>(7,11);if(IndirectProbe.Physical(optional,9)!=null||optional.Value!=null)return false;
                    var asyncBox=new IndirectBox<int>(7,11);var gate=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pending=IndirectProbe.Async(gate.Task,asyncBox);if(pending.IsCompleted||asyncBox.Value!=7)return false;
                    gate.SetResult(9);if(await pending.WaitAsync(token)!=0||asyncBox.Value!=0)return false;
                    var fault=new InvalidOperationException("indirect fault");
                    try{await IndirectProbe.Async(Task.FromException<int>(fault),new IndirectBox<int>(7,11));return false;}
                    catch(InvalidOperationException caught){if(!ReferenceEquals(caught,fault))return false;}
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    try{await IndirectProbe.Async(Task.FromCanceled<int>(canceled.Token),new IndirectBox<int>(7,11));return false;}
                    catch(OperationCanceledException caught){return caught.CancellationToken==canceled.Token;}
                }
            }
            """;
        const string newSource = """
            #nullable enable
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            public static class NewIndirectRunner{
                public static async Task<bool> Run(CancellationToken token){
                    var value=new object();var other=new object();
                    var field=new IndirectBox<object?>(value,other);if(IndirectProbe.Field(field,value)!=null||field.Value!=null||!ReferenceEquals(field.Other,other))return false;
                    var chain=new IndirectBox<object?>(value,other);if(IndirectProbe.Chain(chain,value)!=null||chain.Value!=null)return false;
                    object?[] values={value};if(IndirectProbe.Element(values,value)!=null||values[0]!=null)return false;
                    var property=new IndirectBox<object?>(value,other);if(IndirectProbe.Property(property,value)!=null||property.Value!=null||property.Gets!=1)return false;
                    var source=new IndirectSource<object>();var owner=new IndirectBox<object?>(value,other);
                    if(IndirectProbe.Owner(owner,source,value)!=null||owner.Value!=null)return false;
                    var delegated=new IndirectBox<object?>(value,other);IndirectReader<object> reader=IndirectProbe.Maybe;
                    if(IndirectProbe.Delegate(delegated,reader,value)!=null||delegated.Value!=null)return false;
                    var sourceProperty=new IndirectBox<object?>(value,other);if(IndirectProbe.SourceProperty(sourceProperty,source)!=null||sourceProperty.Value!=null)return false;
                    var priority=new IndirectBox<object?>(value,other);if(IndirectProbe.PriorityProperty(priority,source)!=null||priority.Value!=null)return false;
                    var sourceField=new IndirectBox<object?>(value,other);if(IndirectProbe.SourceField(sourceField,source)!=null||sourceField.Value!=null)return false;
                    var conditional=new IndirectBox<object?>(value,other);if(IndirectProbe.ConditionalStore(conditional,value)!=null||conditional.Value!=null)return false;
                    var conditionalDefault=new IndirectBox<object?>(value,other);if(IndirectProbe.ConditionalDefault(conditionalDefault,value)!=null||conditionalDefault.Value!=null)return false;
                    var conditionalNullable=new IndirectBox<object?>(value,other);if(IndirectProbe.ConditionalNullableNew(conditionalNullable,value)!=null||conditionalNullable.Value!=null)return false;
                    var conditionalUser=new IndirectBox<object?>(value,other);if(IndirectProbe.ConditionalUserConversion(conditionalUser,value)!=null||conditionalUser.Value!=null)return false;
                    var unknownGuarded=new IndirectBox<object>(value,other);if(!ReferenceEquals(IndirectProbe.UnknownGuardedStore(unknownGuarded,value),value)||!ReferenceEquals(unknownGuarded.Value,value))return false;
                    var modifiedGuarded=new IndirectBox<object?>(value,other);if(IndirectProbe.ModifiedGuardedStore(modifiedGuarded,value)!=null||modifiedGuarded.Value!=null)return false;
                    var tupleGuarded=new IndirectPair<object,Uri>(value,new Uri("https://example.invalid/before"));var updatedOther=new Uri("https://example.invalid/after");
                    if(!ReferenceEquals(IndirectProbe.TupleGuarded(tupleGuarded,value,updatedOther),value)||!ReferenceEquals(tupleGuarded.Value,value)||!ReferenceEquals(tupleGuarded.Other,updatedOther))return false;
                    var tupleOut=new IndirectBox<object>(value,other);var tag=new Uri("https://example.invalid/out");
                    if(!ReferenceEquals(IndirectProbe.TupleOut(tupleOut,value,tag,out Uri copied),value)||!ReferenceEquals(tupleOut.Value,value)||!ReferenceEquals(copied,tag))return false;
                    var conditionalTuple=new IndirectBox<object>(value,other);if(!ReferenceEquals(IndirectProbe.ConditionalTuple(conditionalTuple,value,tag,false,out Uri conditionalCopied),value)||!ReferenceEquals(conditionalTuple.Value,value)||!ReferenceEquals(conditionalCopied,tag))return false;
                    var switchTuple=new IndirectBox<object>(value,other);if(!ReferenceEquals(IndirectProbe.SwitchTuple(switchTuple,value,tag,true,out Uri switchCopied),value)||!ReferenceEquals(switchTuple.Value,value)||!ReferenceEquals(switchCopied,tag))return false;
                    var conditionalOptional=new IndirectBox<object?>(value,other);if(IndirectProbe.ConditionalOptionalTuple(conditionalOptional,value,tag,false,out Uri optionalCopied)!=null||conditionalOptional.Value!=null||!ReferenceEquals(optionalCopied,tag))return false;
                    var switchOptional=new IndirectBox<object?>(value,other);if(IndirectProbe.SwitchOptionalTuple(switchOptional,value,tag,false,out Uri switchOptionalCopied)!=null||switchOptional.Value!=null||!ReferenceEquals(switchOptionalCopied,tag))return false;
                    var reboundNullable=new IndirectBox<object?>(value,other);if(IndirectProbe.ReboundNullableTuple(reboundNullable,value,tag,out Uri reboundCopied)!=null||!ReferenceEquals(reboundNullable.Value,value)||reboundNullable.Other!=null||!ReferenceEquals(reboundCopied,tag))return false;
                    var directField=new IndirectBox<object?>(value,other);if(IndirectProbe.DirectField(directField,value)!=null||directField.Value!=null||!ReferenceEquals(directField.Other,other))return false;
                    object?[] directElement={value};if(IndirectProbe.DirectElement(directElement,value)!=null||directElement[0]!=null)return false;
                    var directProperty=new IndirectBox<object?>(value,other);if(IndirectProbe.DirectProperty(directProperty,value)!=null||directProperty.Value!=null||directProperty.Gets!=1)return false;
                    var directNull=new IndirectBox<object?>(value,other);if(IndirectProbe.DirectFieldGuardNull(directNull,value)!=null||directNull.Value!=null)return false;
                    var directUnknown=new IndirectBox<object?>(value,other);if(!ReferenceEquals(IndirectProbe.DirectFieldGuardUnknown(directUnknown,value),value)||!ReferenceEquals(directUnknown.Value,value))return false;
                    var directModified=new IndirectBox<object?>(value,other);if(IndirectProbe.DirectFieldGuardModified(directModified,value)!=null||directModified.Value!=null)return false;
                    var nullableGuarded=new IndirectBox<object?>(value,other);if(IndirectProbe.NullableGuardedStore(nullableGuarded,value)!=null||nullableGuarded.Value!=null)return false;
                    var guarded=new IndirectBox<object>(value,other);if(!ReferenceEquals(IndirectProbe.GuardedStore(guarded,value),value)||!ReferenceEquals(guarded.Value,value))return false;
                    var optionalStore=new IndirectBox<object?>(value,other);if(IndirectProbe.OptionalStore(optionalStore,value)!=null||optionalStore.Value!=null)return false;
                    var copy=new IndirectBox<object?>(value,other);if(!ReferenceEquals(IndirectProbe.Copy(copy,value),value)||copy.Value!=null)return false;
                    var sibling=new IndirectPair<object?,Uri>(value,new Uri("https://example.invalid/sibling"));
                    if(!ReferenceEquals(IndirectProbe.Sibling(sibling,value),sibling.Other)||sibling.Value!=null)return false;
                    var optional=new IndirectBox<int?>(23,29);if(IndirectProbe.Physical(optional,19)!=null||optional.Value!=null)return false;
                    var modifiedPhysical=new IndirectBox<int?>(23,29);if(IndirectProbe.ModifiedGuardedStore(modifiedPhysical,19)!=null||modifiedPhysical.Value!=null)return false;
                    var optionalPhysical=new IndirectBox<int?>(23,29);if(IndirectProbe.ConditionalOptionalTuple(optionalPhysical,19,tag,false,out Uri optionalPhysicalCopied)!=null||optionalPhysical.Value!=null||!ReferenceEquals(optionalPhysicalCopied,tag))return false;
                    var reboundPhysical=new IndirectBox<int?>(23,29);if(IndirectProbe.ReboundNullableTuple(reboundPhysical,19,tag,out Uri reboundPhysicalCopied)!=null||reboundPhysical.Value!=19||reboundPhysical.Other!=null||!ReferenceEquals(reboundPhysicalCopied,tag))return false;
                    var asyncBox=new IndirectBox<object?>(value,other);var gate=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pending=IndirectProbe.Async(gate.Task,asyncBox);if(pending.IsCompleted||!ReferenceEquals(asyncBox.Value,value))return false;
                    gate.SetResult(value);if(await pending.WaitAsync(token)!=null||asyncBox.Value!=null)return false;
                    var fault=new InvalidOperationException("indirect fault");
                    try{await IndirectProbe.Async(Task.FromException<object>(fault),new IndirectBox<object?>(value,other));return false;}
                    catch(InvalidOperationException caught){if(!ReferenceEquals(caught,fault))return false;}
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    try{await IndirectProbe.Async(Task.FromCanceled<object>(canceled.Token),new IndirectBox<object?>(value,other));return false;}
                    catch(OperationCanceledException caught){return caught.CancellationToken==canceled.Token;}
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "IndirectStorage", "IndirectStorage", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("IndirectStorage.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var originalErrors = original.GetDiagnostics(cancellationToken).Where(diagnostic =>
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return diagnostic.Severity == DiagnosticSeverity.Error;
        }).ToArray();
        if (originalErrors.Length != 0)
        { throw new InvalidOperationException("Indirect fixture: " + originalErrors[0]); }
        var expected = new Dictionary<string, string>
        {
            ["Field"] = "T1? Field<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["Chain"] = "T1? Chain<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["Element"] = "T1? Element<T1>(T1?[] values, T1 fill)",
            ["Property"] = "T1? Property<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["Owner"] = "T1? Owner<T1>(global::IndirectBox<T1?> box, global::IndirectSource<T1> source, T1 fill)",
            ["Delegate"] = "T1? Delegate<T1>(global::IndirectBox<T1?> box, global::IndirectReader<T1> read, T1 fill)",
            ["SourceProperty"] = "T1? SourceProperty<T1>(global::IndirectBox<T1?> box, global::IndirectSource<T1> source)",
            ["PriorityProperty"] = "T1? PriorityProperty<T1>(global::IndirectBox<T1?> box, global::IndirectSource<T1> source)",
            ["SourceField"] = "T1? SourceField<T1>(global::IndirectBox<T1?> box, global::IndirectSource<T1> source)",
            ["ConditionalStore"] = "T1? ConditionalStore<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["ConditionalDefault"] = "T1? ConditionalDefault<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["ConditionalNullableNew"] = "T1? ConditionalNullableNew<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["ConditionalUserConversion"] = "T1? ConditionalUserConversion<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["UnknownGuardedStore"] = "T1 UnknownGuardedStore<T1>(global::IndirectBox<T1> box, T1 fill)",
            ["NullableGuardedStore"] = "T1? NullableGuardedStore<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["GuardedStore"] = "T1 GuardedStore<T1>(global::IndirectBox<T1> box, T1 fill)",
            ["ModifiedGuardedStore"] = "T1? ModifiedGuardedStore<T1>(global::IndirectBox<T1?> box, T1? fill)",
            ["TupleGuarded"] = "T1 TupleGuarded<T1, T2>(global::IndirectPair<T1, T2> box, T1 fill, T2 other)",
            ["TupleOut"] = "T1 TupleOut<T1, T2>(global::IndirectBox<T1> box, T1 fill, T2 tag, out T2 copied)",
            ["ConditionalTuple"] = "T1 ConditionalTuple<T1, T2>(global::IndirectBox<T1> box, T1 fill, T2 tag, bool choose, out T2 copied)",
            ["SwitchTuple"] = "T1 SwitchTuple<T1, T2>(global::IndirectBox<T1> box, T1 fill, T2 tag, bool choose, out T2 copied)",
            ["ConditionalOptionalTuple"] = "T1? ConditionalOptionalTuple<T1, T2>(global::IndirectBox<T1?> box, T1 fill, T2 tag, bool choose, out T2 copied)",
            ["SwitchOptionalTuple"] = "T1? SwitchOptionalTuple<T1, T2>(global::IndirectBox<T1?> box, T1 fill, T2 tag, bool choose, out T2 copied)",
            ["ReboundNullableTuple"] = "T1? ReboundNullableTuple<T1, T2>(global::IndirectBox<T1?> box, T1 fill, T2 tag, out T2 copied)",
            ["DirectField"] = "T1? DirectField<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["DirectElement"] = "T1? DirectElement<T1>(T1?[] values, T1 fill)",
            ["DirectProperty"] = "T1? DirectProperty<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["DirectFieldGuardNull"] = "T1? DirectFieldGuardNull<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["DirectFieldGuardUnknown"] = "T1? DirectFieldGuardUnknown<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["DirectFieldGuardModified"] = "T1? DirectFieldGuardModified<T1>(global::IndirectBox<T1?> box, T1? fill)",
            ["OptionalStore"] = "T1? OptionalStore<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["Copy"] = "T1? Copy<T1>(global::IndirectBox<T1?> box, T1 fill)",
            ["Sibling"] = "T2 Sibling<T1, T2>(global::IndirectPair<T1?, T2> box, T1 fill)",
            ["Async"] = "global::System.Threading.Tasks.Task<T1?> Async<T1>(global::System.Threading.Tasks.Task<T1> fill, global::IndirectBox<T1?> box)"
        };
        var results = Program.Results((CSharpCompilation)original, cancellationToken)
            .Where(result =>
            {
                ArgumentNullException.ThrowIfNull(result);
                return expected.ContainsKey(result.Method) || result.Method is "Typed"
                    or "ConditionalConstant" or "ConditionalNew";
            })
            .ToDictionary(result =>
            {
                ArgumentNullException.ThrowIfNull(result);
                return result.Method;
            }, StringComparer.Ordinal);
        foreach (var (name, signature) in expected)
        {
            if (!results.TryGetValue(name, out var result) || result.Status != "Suggestion" || result.Signature != signature)
            { throw new InvalidOperationException("Indirect " + name + ": expected " + signature + "; got " + (result?.Status ?? "absent") + ": " + (result?.Signature ?? result?.Message)); }
            Program.Log.Info("PASS IndirectMaybe" + name);
        }
        foreach (var control in new[] { "Typed", "ConditionalConstant", "ConditionalNew" })
        {
            if (results[control].Signature?.Contains("?>", StringComparison.Ordinal) == true)
            { throw new InvalidOperationException("Indirect nonnullable control widened: " + control); }
        }
        CheckMetadata(cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature",
            provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false))
            .OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var changedErrors = changed.GetDiagnostics(cancellationToken).Where(diagnostic =>
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return diagnostic.Severity == DiagnosticSeverity.Error;
        }).ToArray();
        if (changedErrors.Length != 0)
        { throw new InvalidOperationException("Indirect Fix All: " + changedErrors[0]); }
        await Execute(original.WithAssemblyName("OriginalIndirect"), "OriginalIndirectRunner", cancellationToken).ConfigureAwait(false);
        await Execute(changed.WithAssemblyName("ChangedIndirect"), "OriginalIndirectRunner", cancellationToken).ConfigureAwait(false);
        var newDomain = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken));
        await Execute(newDomain.WithAssemblyName("NewIndirect"), "NewIndirectRunner", cancellationToken).ConfigureAwait(false);
        Program.Log.Info("PASS IndirectMaybeOriginalAndNewDomainsEffectsFaultCancellation");
        return expected.Count + 5;
    }

    private static void CheckMetadata(CancellationToken cancellationToken)
    {
        var library = CSharpSyntaxTree.ParseText("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public sealed class ImportedSource<T>{
                [return:MaybeNull]public T Read(T value)=>default;
                [MaybeNull]public T Priority{[return:NotNull]get=>default!;}
            }
            """, cancellationToken: cancellationToken);
        using var image = new MemoryStream();
        var emitted = Program.Compile([library]).WithAssemblyName("IndirectContracts").Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Indirect contract emission: " + string.Join("\n", emitted.Diagnostics)); }
        var site = CSharpSyntaxTree.ParseText("""
            #nullable enable
            public sealed class ImportedBox<T>{public T Value=default!;}
            public static class ImportedUse{
                public static int M(ImportedBox<int> box,ImportedSource<int> source,int fill){
                    ref var alias=ref box.Value;alias=source.Read(fill);return box.Value;
                }
                public static int M2(ImportedBox<int> box,ImportedSource<int> source){
                    ref var alias=ref box.Value;alias=source.Priority;return box.Value;
                }
            }
            """, cancellationToken: cancellationToken);
        var compilation = Program.Compile([site]).AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        // Emission places both property and getter contracts on the getter
        // return. Roslyn then accepts a bare-T generic storage consumer.
        var native = CSharpSyntaxTree.ParseText("""
            #nullable enable
            public static class NativeImportedUse{
                public static T Bare<T>(ImportedBox<T> box,ImportedSource<T> source){
                    ref var alias=ref box.Value;alias=source.Priority;return box.Value;
                }
            }
            """, cancellationToken: cancellationToken);
        var nativeDiagnostics = compilation.AddSyntaxTrees(native).GetDiagnostics(cancellationToken)
            .Where(diagnostic =>
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
                return diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning;
            }).ToArray();
        if (nativeDiagnostics.Length != 0)
        { throw new InvalidOperationException("Indirect imported native generic consumer: " + nativeDiagnostics[0]); }
        var results = Program.Results(compilation, cancellationToken).Where(candidate =>
        {
            ArgumentNullException.ThrowIfNull(candidate);
            return candidate.Method is "M" or "M2";
        }).ToDictionary(candidate =>
        {
            ArgumentNullException.ThrowIfNull(candidate);
            return candidate.Method;
        }, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>
        {
            ["M"] = "T1? M<T1>(global::ImportedBox<T1?> box, global::ImportedSource<T1> source, T1 fill)",
            ["M2"] = "T1 M2<T1>(global::ImportedBox<T1> box, global::ImportedSource<T1> source)"
        };
        foreach (var (name, signature) in expected)
        {
            if (!results.TryGetValue(name, out var result) || result.Status != "Suggestion" || result.Signature != signature)
            { throw new InvalidOperationException("Indirect imported " + name + ": expected " + signature + "; got " + (result?.Status ?? "absent") + ": " + (result?.Signature ?? result?.Message)); }
        }
        Program.Log.Info("PASS IndirectMaybeImportedOwnerContract");
    }

    private static async Task Execute(Compilation compilation, string runner, CancellationToken cancellationToken)
    {
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Indirect emission: " + string.Join("\n", emitted.Diagnostics)); }
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("IndirectStorage", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var pending = (Task<bool>)assembly.GetType(runner)!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
            if (!await pending.WaitAsync(cancellationToken).ConfigureAwait(false))
            { throw new InvalidOperationException("Indirect runtime: " + runner); }
        }
        finally { context.Unload(); }
    }
}
