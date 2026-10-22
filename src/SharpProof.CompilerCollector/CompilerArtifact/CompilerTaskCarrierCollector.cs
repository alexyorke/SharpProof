using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SharpProof.CompilerArtifact;

// Import private generated fields from a real emission. This metadata is source
// evidence; it supplies neither a physical layout nor an allocation verdict.
internal sealed class CompilerTaskCarrierCollector
{
    internal const int MaximumImageBytes = 16 * 1024 * 1024;
    internal const int MaximumCallbacks = 64;
    private readonly CSharpCompilation _compilation;
    private IAssemblySymbol? _assembly;
    private string? _moduleHash;
    private string? _emissionFailure;
    private byte[]? _image;

    internal CompilerTaskCarrierCollector(CSharpCompilation compilation)
    { _compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation)); }

    internal CompilerTaskCallbackActivationCapture CaptureCallbackActivations(IMethodSymbol method, string callableId, CancellationToken cancellation)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        cancellation.ThrowIfCancellationRequested();
        var capture = Capture(method, callableId, cancellation);
        if (capture.Shapes is not { } shapes)
        { return new(null, capture.UnknownReason); }
        var plan = ResourceForkJoinPlan.TryCreate(method, _compilation, cancellation)!;
        var targets = ImmutableHashSet.CreateBuilder<int>();
        foreach (var invocation in plan.Children)
        {
            cancellation.ThrowIfCancellationRequested();
            var child = invocation.TargetMethod;
            var imported = _assembly!.GetTypeByMetadataName(MetadataName(child.ContainingType));
            var candidates = imported?.GetMembers(child.MetadataName).OfType<IMethodSymbol>().Where(candidate =>
                candidate is null ? throw new ArgumentNullException(nameof(candidate)) : candidate.IsStatic && candidate.ReturnsVoid &&
                candidate.Arity == 0 && candidate.Parameters.Length == child.Parameters.Length && candidate.Parameters.All(static parameter =>
                    parameter is null ? throw new ArgumentNullException(nameof(parameter)) : parameter.RefKind == RefKind.None &&
                    parameter.Type.SpecialType == SpecialType.System_Int32)).ToArray();
            if (candidates is not { Length: 1 })
            { return new(null, "TaskCallbackSourceTargetUnavailable"); }
            targets.Add(candidates[0].MetadataToken);
        }
        var forwarders = ImmutableArray.CreateBuilder<CompilerTaskCallbackForwarder>();
        var pending = new Stack<INamedTypeSymbol>();
        pending.Push(_assembly!.GetTypeByMetadataName(MetadataName(method.ContainingType))!);
        try
        {
            using var stream = new MemoryStream(_image!, false);
            using var image = new PEReader(stream);
            var metadata = image.GetMetadataReader();
            while (pending.Count != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var current = pending.Pop();
                foreach (var nested in current.GetTypeMembers())
                { pending.Push(nested); }
                foreach (var callback in current.GetMembers().OfType<IMethodSymbol>())
                {
                    if (!callback.MetadataName.StartsWith("<" + method.MetadataName + ">b__", StringComparison.Ordinal))
                    { continue; }
                    var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(callback.MetadataToken);
                    var body = image.GetMethodBody(metadata.GetMethodDefinition(handle).RelativeVirtualAddress);
                    var check = ValidateCallbackForwarder(body, targets.ToImmutable(), cancellation);
                    if (check.Target is not { } target)
                    { return new(null, check.UnknownReason); }
                    forwarders.Add(new(callback.MetadataToken, target));
                }
            }
            // Capture already bounded the same immutable metadata traversal.
            if (forwarders.Count != shapes.MatchedCallbacks)
            { return new(null, "TaskCallbackForwarderCoverageIncomplete"); }
            return new(new(callableId, shapes.ModuleSha256, forwarders.ToImmutable()), null);
        }
        catch (Exception exception) when (exception is BadImageFormatException or ArgumentException or InvalidOperationException or OverflowException)
        { return new(null, "TaskCallbackForwarderEmissionInvalid"); }
    }

    // Counts compiler-owned wrapper activations only. CLR helpers, source-child
    // activations and exception trace frame enumeration are separate obligations.
    internal static (int? Target, string? UnknownReason) ValidateCallbackForwarder(MethodBodyBlock body,
        ImmutableHashSet<int> targets, CancellationToken cancellation)
    {
        body = ArgumentNullGuard.NotNull(body, nameof(body));
        targets = ArgumentNullGuard.NotNull(targets, nameof(targets));
        cancellation.ThrowIfCancellationRequested();
        if (targets.Count is < 1 or > ResourceForkJoinPlan.MaximumChildren ||
            targets.Any(static token => token >> 24 != 6 || (token & 0x00ffffff) == 0))
        { return (null, "TaskCallbackForwarderTargetUnavailable"); }
        var code = body.GetILBytes();
        if (!body.ExceptionRegions.IsEmpty || code is null || code.Length > 4096)
        { return (null, "TaskCallbackForwarderBodyUnsupported"); }
        int? target = null;
        var cursor = 0;
        while (cursor < code.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            CompilerIlToken instruction;
            try
            {
                if (!CompilerScalarIlDecoder.TryReadMetadataInstruction(code, ref cursor, out instruction))
                { return (null, "TaskCallbackForwarderBodyUnsupported"); }
            }
            catch (Exception exception) when (exception is BadImageFormatException or OverflowException)
            { return (null, "TaskCallbackForwarderBodyUnsupported"); }
            switch (instruction.RawCode)
            {
                case "Call":
                    var called = checked((int)instruction.Operand);
                    if (target is not null || !targets.Contains(called))
                    { return (null, "TaskCallbackForwarderCallUnsupported"); }
                    target = called;
                    break;
                case "Ret":
                    return cursor == code.Length && target is not null ? (target, null) : (null, "TaskCallbackForwarderBodyUnsupported");
                case "Br":
                case "Br_s":
                    if (instruction.TargetByteOffset <= instruction.Offset || instruction.TargetByteOffset >= code.Length)
                    { return (null, "TaskCallbackForwarderBodyUnsupported"); }
                    break;
                case "Nop":
                case "Ldarg_0":
                case "Ldfld":
                case "Ldc_i4":
                case "Ldc_i4_s":
                case "Ldc_i4_m1":
                case "Ldc_i4_0":
                case "Ldc_i4_1":
                case "Ldc_i4_2":
                case "Ldc_i4_3":
                case "Ldc_i4_4":
                case "Ldc_i4_5":
                case "Ldc_i4_6":
                case "Ldc_i4_7":
                case "Ldc_i4_8":
                case "Ldloc":
                case "Ldloc_s":
                case "Ldloc_0":
                case "Ldloc_1":
                case "Ldloc_2":
                case "Ldloc_3":
                case "Stloc":
                case "Stloc_s":
                case "Stloc_0":
                case "Stloc_1":
                case "Stloc_2":
                case "Stloc_3":
                    break;
                default:
                    return (null, "TaskCallbackForwarderBodyUnsupported");
            }
        }
        return (null, "TaskCallbackForwarderBodyUnsupported");
    }

    internal CompilerTaskRootExceptionBoundaryCapture CaptureExceptionBoundary(IMethodSymbol method, string callableId, CancellationToken cancellation)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        cancellation.ThrowIfCancellationRequested();
        var capture = Capture(method, callableId, cancellation);
        if (capture.Shapes is not { } shapes)
        { return new(null, capture.UnknownReason); }
        if (shapes.StateMachine is not { } machine)
        { return new(null, "TaskRootExceptionBoundaryAsyncRequired"); }
        var state = _assembly!.GetTypeByMetadataName(machine.MetadataName);
        var entries = state?.GetMembers("MoveNext").OfType<IMethodSymbol>().Where(static candidate =>
            candidate is null ? throw new ArgumentNullException(nameof(candidate)) : candidate.Parameters.IsEmpty && candidate.Arity == 0).ToArray();
        if (entries is not { Length: 1 })
        { return new(null, "TaskRootExceptionBoundaryMethodUnavailable"); }
        try
        {
            using var stream = new MemoryStream(_image!, false);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            var handle = MetadataTokens.EntityHandle(entries[0].MetadataToken);
            if (handle.Kind != HandleKind.MethodDefinition)
            { return new(null, "TaskRootExceptionBoundaryMethodUnavailable"); }
            var body = pe.GetMethodBody(metadata.GetMethodDefinition((MethodDefinitionHandle)handle).RelativeVirtualAddress);
            var framework = _compilation.GetTypeByMetadataName("System.Exception")?.ContainingAssembly.Identity;
            if (framework is null)
            { return new(null, "TaskRootExceptionBoundaryCatchUnsupported"); }
            var plan = ResourceForkJoinPlan.TryCreate(method, _compilation, cancellation)!;
            if (ValidateExceptionBoundary(metadata, body, framework, plan.Children.Length, cancellation) is { } failure)
            { return new(null, failure); }
            var region = body.ExceptionRegions[0];
            return new(new(callableId, shapes.ModuleSha256, machine.MetadataName, entries[0].MetadataToken, region.TryOffset, region.TryLength), null);
        }
        catch (Exception exception) when (exception is BadImageFormatException or ArgumentException or InvalidOperationException or OverflowException)
        { return new(null, "TaskRootExceptionBoundaryEmissionInvalid"); }
    }

    // Body facts only. The caller separately binds the emitted module, owning
    // state-machine method and callable before constructing a boundary record.
    internal static string? ValidateExceptionBoundary(MetadataReader metadata, MethodBodyBlock body,
        AssemblyIdentity framework, int children, CancellationToken cancellation)
    {
        metadata = ArgumentNullGuard.NotNull(metadata, nameof(metadata));
        body = ArgumentNullGuard.NotNull(body, nameof(body));
        framework = ArgumentNullGuard.NotNull(framework, nameof(framework));
        cancellation.ThrowIfCancellationRequested();
        if (children is < 2 or > 16)
        { return "TaskRootExceptionBoundaryCallsIncomplete"; }
        try
        {
            if (body.ExceptionRegions.Length != 1 || body.ExceptionRegions[0].Kind != ExceptionRegionKind.Catch)
            { return "TaskRootExceptionBoundaryHandlerUnsupported"; }
            var region = body.ExceptionRegions[0];
            if (!FrameworkType(metadata, region.CatchType, "System", "Exception", framework))
            { return "TaskRootExceptionBoundaryCatchUnsupported"; }
            var code = body.GetILBytes();
            if (code is null || code.Length > 4096)
            { return "TaskRootExceptionBoundaryBodyUnsupported"; }
            var cursor = 0;
            var runs = 0;
            var joins = 0;
            var awaits = 0;
            var records = 0;
            while (cursor < code.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!CompilerScalarIlDecoder.TryReadMetadataInstruction(code, ref cursor, out var instruction))
                { return "TaskRootExceptionBoundaryBodyUnsupported"; }
                var inside = instruction.Offset >= region.TryOffset && instruction.Offset < region.TryOffset + region.TryLength;
                if (instruction.RawCode == "Calli")
                { return "TaskRootExceptionBoundaryCallUnsupported"; }
                if (instruction.RawCode is "Newobj" or "Newarr" or "Throw" or "Rethrow" && !inside)
                { return "TaskRootExceptionBoundarySourceEscape"; }
                if (instruction.RawCode is not ("Call" or "Callvirt"))
                { continue; }
                var called = MetadataTokens.EntityHandle(checked((int)instruction.Operand));
                if (called.Kind == HandleKind.MethodSpecification)
                { called = metadata.GetMethodSpecification((MethodSpecificationHandle)called).Method; }
                if (called.Kind != HandleKind.MemberReference)
                { return "TaskRootExceptionBoundaryCallUnsupported"; }
                var member = metadata.GetMemberReference((MemberReferenceHandle)called);
                var name = metadata.GetString(member.Name);
                var taskCall = name is "Run" or "WhenAll" && FrameworkType(metadata, member.Parent, "System.Threading.Tasks", "Task", framework);
                var awaitCall = name == "GetResult" && FrameworkAwaiter(metadata, member.Parent, framework);
                if ((taskCall || awaitCall) && !inside)
                { return "TaskRootExceptionBoundarySourceEscape"; }
                if (taskCall)
                { if (name == "Run") { runs++; } else { joins++; } }
                if (awaitCall)
                { awaits++; }
                if (name == "SetException" && instruction.Offset >= region.HandlerOffset &&
                    instruction.Offset < region.HandlerOffset + region.HandlerLength &&
                    FrameworkType(metadata, member.Parent, "System.Runtime.CompilerServices", "AsyncTaskMethodBuilder", framework))
                { records++; }
            }
            if (runs != children || joins != 1 || awaits != 1 || records != 1)
            { return "TaskRootExceptionBoundaryCallsIncomplete"; }
            return null;
        }
        catch (Exception exception) when (exception is BadImageFormatException or ArgumentException or InvalidOperationException or OverflowException)
        { return "TaskRootExceptionBoundaryEmissionInvalid"; }
    }

    private static bool FrameworkType(MetadataReader metadata, EntityHandle handle, string space, string name, AssemblyIdentity framework)
    {
        metadata = ArgumentNullGuard.NotNull(metadata, nameof(metadata));
        space = ArgumentNullGuard.NotNull(space, nameof(space));
        name = ArgumentNullGuard.NotNull(name, nameof(name));
        framework = ArgumentNullGuard.NotNull(framework, nameof(framework));
        if (handle.Kind != HandleKind.TypeReference)
        { return false; }
        var type = metadata.GetTypeReference((TypeReferenceHandle)handle);
        if (metadata.GetString(type.Namespace) != space || metadata.GetString(type.Name) != name || type.ResolutionScope.Kind != HandleKind.AssemblyReference)
        { return false; }
        var assembly = metadata.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
        return metadata.GetString(assembly.Name) == framework.Name && assembly.Version == framework.Version &&
            (assembly.Flags & AssemblyFlags.PublicKey) == 0 && metadata.GetBlobContent(assembly.PublicKeyOrToken).SequenceEqual(framework.PublicKeyToken) &&
            (assembly.Culture.IsNil ? "" : metadata.GetString(assembly.Culture)) == framework.CultureName;
    }

    private static bool FrameworkAwaiter(MetadataReader metadata, EntityHandle handle, AssemblyIdentity framework)
    {
        metadata = ArgumentNullGuard.NotNull(metadata, nameof(metadata));
        framework = ArgumentNullGuard.NotNull(framework, nameof(framework));
        if (handle.Kind != HandleKind.TypeReference)
        { return false; }
        var type = metadata.GetTypeReference((TypeReferenceHandle)handle);
        return metadata.GetString(type.Name) == "ConfiguredTaskAwaiter" && metadata.GetString(type.Namespace).Length == 0 &&
            FrameworkType(metadata, type.ResolutionScope, "System.Runtime.CompilerServices", "ConfiguredTaskAwaitable", framework);
    }

    internal CompilerTaskCarrierCapture Capture(IMethodSymbol method, string callableId, CancellationToken cancellation)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        cancellation.ThrowIfCancellationRequested();
        var plan = ResourceForkJoinPlan.TryCreate(method, _compilation, cancellation);
        if (plan is null || string.IsNullOrWhiteSpace(callableId))
        { return Unknown("TaskCarrierRootUnsupported"); }
        if (!EnsureEmission(cancellation))
        { return Unknown(_emissionFailure ?? "TaskCarrierEmissionUnavailable"); }
        var type = _assembly!.GetTypeByMetadataName(MetadataName(method.ContainingType));
        if (type is null)
        { return Unknown("TaskCarrierRootMetadataUnavailable"); }
        var roots = type.GetMembers(method.MetadataName).OfType<IMethodSymbol>().Where(candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) :
            candidate.IsStatic && candidate.Arity == 0 && candidate.Parameters.Length == method.Parameters.Length &&
            candidate.Parameters.All(static parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) :
                parameter.Type.SpecialType == SpecialType.System_Int32)).ToArray();
        if (roots.Length != 1)
        { return Unknown("TaskCarrierRootMetadataAmbiguous"); }
        var framework = plan.Join.TargetMethod.ContainingAssembly.Identity.ToString();
        CompilerTaskCarrierType? stateMachine = null;
        if (plan.ConfiguredAwait)
        {
            var attributes = roots[0].GetAttributes().Where(attribute => attribute is null ? throw new ArgumentNullException(nameof(attribute)) :
                attribute.AttributeClass is { Name: "AsyncStateMachineAttribute" } attributeType &&
                NamespaceName(attributeType.ContainingNamespace) == "System.Runtime.CompilerServices" &&
                attributeType.ContainingAssembly.Identity.ToString() == framework).ToArray();
            if (attributes.Length != 1 || attributes[0].ConstructorArguments.Length != 1 ||
                attributes[0].ConstructorArguments[0].Value is not INamedTypeSymbol state ||
                !Generated(state, framework) || !Inside(state, type) || Shape(state, framework, cancellation) is not { } shape)
            { return Unknown("TaskCarrierStateMachineUnavailable"); }
            stateMachine = shape;
        }
        var frames = ImmutableArray.CreateBuilder<CompilerTaskCarrierType>();
        var callbacks = 0;
        var pending = new Stack<INamedTypeSymbol>();
        pending.Push(type);
        var remaining = ResourceExpression.MaximumNodes;
        while (pending.Count != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return Unknown("TaskCarrierMetadataBudgetExceeded"); }
            var current = pending.Pop();
            foreach (var nested in current.GetTypeMembers())
            { pending.Push(nested); }
            foreach (var callback in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (!callback.MetadataName.StartsWith("<" + method.MetadataName + ">b__", StringComparison.Ordinal))
                { continue; }
                if (++callbacks > MaximumCallbacks || !Generated(current, framework) && !Generated(callback, framework))
                { return Unknown("TaskCarrierCallbackMetadataUnsupported"); }
                if (!callback.IsStatic)
                {
                    if (Shape(current, framework, cancellation) is not { } frame || frame.IsValueType)
                    { return Unknown("TaskCarrierCallbackStorageUnsupported"); }
                    frames.Add(frame);
                }
            }
        }
        // Overloads with the same metadata name may contribute extra generated
        // callbacks. Keeping them gives an upper envelope, never fewer sites.
        var fieldBudget = ResourceExpression.MaximumNodes - (stateMachine == null ? 0 : stateMachine.Fields.Length + 1);
        foreach (var frame in frames)
        {
            cancellation.ThrowIfCancellationRequested();
            fieldBudget -= frame.Fields.Length + 1;
            if (fieldBudget < 0)
            { return Unknown("TaskCarrierMetadataBudgetExceeded"); }
        }
        if (callbacks < plan.Children.Length)
        { return Unknown("TaskCarrierCallbackMetadataIncomplete"); }
        return new(new(callableId, _moduleHash!, typeof(CSharpCompilation).Assembly.FullName!, callbacks,
            frames.ToImmutable(), stateMachine), null);
    }

    private bool EnsureEmission(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_assembly != null || _emissionFailure != null)
        { return _assembly != null; }
        var sourceBytes = 0L;
        foreach (var tree in _compilation.SyntaxTrees)
        {
            cancellation.ThrowIfCancellationRequested();
            sourceBytes += tree.GetText(cancellation).Length;
            if (sourceBytes > 1024 * 1024)
            { _emissionFailure = "TaskCarrierEmissionBudgetExceeded"; return false; }
        }
        using var stream = new CarrierImageStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emission;
        try
        { emission = _compilation.Emit(stream, cancellationToken: cancellation); }
        catch (InvalidDataException)
        { _emissionFailure = "TaskCarrierEmissionBudgetExceeded"; return false; }
        cancellation.ThrowIfCancellationRequested();
        if (!emission.Success)
        { _emissionFailure = "TaskCarrierEmissionFailed"; return false; }
        if (stream.Length > MaximumImageBytes)
        { _emissionFailure = "TaskCarrierEmissionBudgetExceeded"; return false; }
        var image = stream.ToArray();
        _image = image;
        var reference = MetadataReference.CreateFromImage(image);
        var inspection = CSharpCompilation.Create("SharpProof.TaskCarrierInspection", references: _compilation.References.Append(reference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithMetadataImportOptions(MetadataImportOptions.All));
        _assembly = inspection.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
        _moduleHash = HashEncoding.ComputeSha256Hex([.. image]);
        if (_assembly is null)
        { _emissionFailure = "TaskCarrierEmissionMetadataUnavailable"; }
        return _assembly != null;
    }

    private static CompilerTaskCarrierType? Shape(INamedTypeSymbol type, string framework, CancellationToken cancellation)
    {
        type = ArgumentNullGuard.NotNull(type, nameof(type));
        framework = ArgumentNullGuard.NotNull(framework, nameof(framework));
        if (!Generated(type, framework) || type.IsGenericType || type.BaseType?.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType) ||
            type.GetAttributes().Any(static attribute => attribute is null ? throw new ArgumentNullException(nameof(attribute)) : attribute.AttributeClass?.Name == "StructLayoutAttribute"))
        { return null; }
        var metadataName = MetadataName(type);
        if (string.IsNullOrWhiteSpace(metadataName) || metadataName.Length > 4096 || metadataName.Any(static character => char.IsControl(character)))
        { return null; }
        var fields = ImmutableArray.CreateBuilder<ResourceStorageKind>();
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            cancellation.ThrowIfCancellationRequested();
            if (field.IsStatic)
            { continue; }
            var storage = field.Type.SpecialType switch
            {
                SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => ResourceStorageKind.Byte,
                SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => ResourceStorageKind.Word16,
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => ResourceStorageKind.Word32,
                SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => ResourceStorageKind.Word64,
                _ => field.Type.IsReferenceType ? ResourceStorageKind.Reference : ResourceStorageKind.Unspecified
            };
            if (storage != ResourceStorageKind.Unspecified)
            { fields.Add(storage); }
            else if (field.Type is INamedTypeSymbol named && named.ContainingAssembly.Identity.ToString() == framework &&
                NamespaceName(named.ContainingNamespace) == "System.Runtime.CompilerServices" && named.MetadataName == "AsyncTaskMethodBuilder")
            { fields.Add(ResourceStorageKind.Reference); }
            else if (field.Type is INamedTypeSymbol awaiter && awaiter.ContainingAssembly.Identity.ToString() == framework &&
                awaiter.MetadataName == "ConfiguredTaskAwaiter" && awaiter.ContainingType?.MetadataName == "ConfiguredTaskAwaitable" &&
                NamespaceName(awaiter.ContainingNamespace) == "System.Runtime.CompilerServices")
            { fields.Add(ResourceStorageKind.Reference); fields.Add(ResourceStorageKind.Word32); }
            else
            { return null; }
            if (fields.Count > ResourceAllocationShape.MaximumFields)
            { return null; }
        }
        return new(metadataName, type.IsValueType, fields.ToImmutable());
    }

    private static bool Generated(ISymbol symbol, string framework)
    {
        symbol = ArgumentNullGuard.NotNull(symbol, nameof(symbol));
        framework = ArgumentNullGuard.NotNull(framework, nameof(framework));
        return symbol.GetAttributes().Any(attribute => attribute is null ? throw new ArgumentNullException(nameof(attribute)) :
            attribute.AttributeClass?.Name == "CompilerGeneratedAttribute" &&
            NamespaceName(attribute.AttributeClass.ContainingNamespace) == "System.Runtime.CompilerServices" &&
            attribute.AttributeClass.ContainingAssembly.Identity.ToString() == framework);
    }

    private static bool Inside(INamedTypeSymbol nested, INamedTypeSymbol owner)
    {
        nested = ArgumentNullGuard.NotNull(nested, nameof(nested));
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        for (var current = nested.ContainingType; current != null; current = current.ContainingType)
        { if (SymbolEqualityComparer.Default.Equals(current, owner)) { return true; } }
        return false;
    }

    private static string MetadataName(INamedTypeSymbol type)
    {
        type = ArgumentNullGuard.NotNull(type, nameof(type));
        return type.ContainingType is { } owner ? MetadataName(owner) + "+" + type.MetadataName :
            (type.ContainingNamespace.IsGlobalNamespace ? "" : NamespaceName(type.ContainingNamespace) + ".") + type.MetadataName;
    }

    private static string NamespaceName(INamespaceSymbol symbol)
    {
        symbol = ArgumentNullGuard.NotNull(symbol, nameof(symbol));
        if (symbol.IsGlobalNamespace)
        { return string.Empty; }
        return symbol.ContainingNamespace.IsGlobalNamespace ? symbol.MetadataName :
            NamespaceName(symbol.ContainingNamespace) + "." + symbol.MetadataName;
    }

    private static CompilerTaskCarrierCapture Unknown(string reason)
    { reason = ArgumentNullGuard.NotNull(reason, nameof(reason)); return new(null, reason); }


    private sealed class CarrierImageStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            buffer = ArgumentNullGuard.NotNull(buffer, nameof(buffer));
            if (count > MaximumImageBytes - Position)
            { throw new InvalidDataException("Task carrier image exceeds its budget."); }
            base.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value)
        {
            if (Position >= MaximumImageBytes)
            { throw new InvalidDataException("Task carrier image exceeds its budget."); }
            base.WriteByte(value);
        }

        public override void SetLength(long value)
        {
            if (value > MaximumImageBytes)
            { throw new InvalidDataException("Task carrier image exceeds its budget."); }
            base.SetLength(value);
        }
    }
}
