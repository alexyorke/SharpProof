internal static class EffectSummaryMetadataReader
{
    private static readonly ConcurrentDictionary<string, Type?> RuntimeTypeCache = new(StringComparer.Ordinal);

    internal static bool TryResolveSameAssemblyMethodDefinitionHandle(
        MetadataReader reader,
        int metadataToken,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        out MethodDefinitionHandle handle)
    {
        handle = default;
        var resolvedHandle = MetadataTokens.Handle(metadataToken);
        switch (resolvedHandle.Kind)
        {
            case HandleKind.MethodDefinition:
                handle = (MethodDefinitionHandle)resolvedHandle;
                return true;
            case HandleKind.MethodSpecification:
                var specification = reader.GetMethodSpecification((MethodSpecificationHandle)resolvedHandle);
                if (specification.Method.Kind == HandleKind.MethodDefinition)
                {
                    handle = (MethodDefinitionHandle)specification.Method;
                    return true;
                }

                if (specification.Method.Kind == HandleKind.MemberReference)
                    return TryResolveMethodDefinitionHandleFromMemberReference(
                        reader,
                        (MemberReferenceHandle)specification.Method,
                        methodDefinitionHandlesByExactKey,
                        out handle);

                return false;
            case HandleKind.MemberReference:
                return TryResolveMethodDefinitionHandleFromMemberReference(
                    reader,
                    (MemberReferenceHandle)resolvedHandle,
                    methodDefinitionHandlesByExactKey,
                    out handle);
            default:
                return false;
        }
    }

    private static bool TryResolveMethodDefinitionHandleFromMemberReference(
        MetadataReader reader,
        MemberReferenceHandle handle,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        out MethodDefinitionHandle resolvedHandle)
    {
        var exactKey = GetMemberReferenceExactKey(reader, handle);
        if (methodDefinitionHandlesByExactKey.TryGetValue(exactKey, out resolvedHandle)) return true;

        var lookupKey = GetMemberReferenceMethodLookupExactKey(reader, handle);
        return !string.Equals(lookupKey, exactKey, StringComparison.Ordinal) &&
               methodDefinitionHandlesByExactKey.TryGetValue(lookupKey, out resolvedHandle);
    }

    internal static string ResolveMethodExactKey(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.Handle(token);
        return handle.Kind switch
        {
            HandleKind.MethodDefinition => GetMethodExactKey(reader, (MethodDefinitionHandle)handle),
            HandleKind.MemberReference => GetMemberReferenceExactKey(reader, (MemberReferenceHandle)handle),
            HandleKind.MethodSpecification => ResolveMethodSpecificationExactKey(reader,
                (MethodSpecificationHandle)handle),
            _ => $"metadata-token:0x{token:X8}"
        };
    }

    internal static StructuralMethodIdentity? TryResolveStructuralMethodIdentity(
        MetadataReader reader,
        int token,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByDisplaySignature)
    {
        try
        {
            if (TryResolveSameAssemblyMethodDefinitionHandle(
                    reader,
                    token,
                    methodDefinitionHandlesByDisplaySignature,
                    out var definitionHandle))
                return EcmaStructuralMethodIdentity.Create(reader, definitionHandle);

            var handle = MetadataTokens.Handle(token);
            if (handle.Kind == HandleKind.MethodSpecification)
                handle = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;

            return handle.Kind switch
            {
                HandleKind.MethodDefinition =>
                    EcmaStructuralMethodIdentity.Create(reader, (MethodDefinitionHandle)handle),
                HandleKind.MemberReference =>
                    EcmaStructuralMethodIdentity.Create(reader, (MemberReferenceHandle)handle),
                _ => null
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal static string ResolveMethodSpecificationExactKey(MetadataReader reader, MethodSpecificationHandle handle)
    {
        var specification = reader.GetMethodSpecification(handle);
        var method = specification.Method;
        return method.Kind switch
        {
            HandleKind.MethodDefinition => GetMethodExactKey(reader, (MethodDefinitionHandle)method),
            HandleKind.MemberReference => GetMemberReferenceExactKey(reader, (MemberReferenceHandle)method),
            _ => $"method-spec:0x{MetadataTokens.GetToken(handle):X8}"
        };
    }

    internal static string ResolveFieldToken(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.Handle(token);
        return handle.Kind switch
        {
            HandleKind.FieldDefinition => GetFieldDefinitionSymbol(reader, (FieldDefinitionHandle)handle),
            HandleKind.MemberReference => GetMemberReferenceSymbol(reader, (MemberReferenceHandle)handle),
            _ => $"metadata-token:0x{token:X8}"
        };
    }

    internal static string GetFieldExactKey(MetadataReader reader, FieldDefinitionHandle handle)
    {
        var definition = reader.GetFieldDefinition(handle);
        var typeName = NormalizeExactTypeName(GetTypeName(reader, definition.GetDeclaringType()));
        var fieldName = reader.GetString(definition.Name);
        var fieldType = DecodeFieldDefinitionExactType(definition);
        return $"{typeName}.{fieldName}:{fieldType}";
    }

    internal static string GetMemberReferenceFieldExactKey(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName = NormalizeExactTypeName(GetMemberReferenceParentName(reader, memberReference.Parent));
        var fieldName = reader.GetString(memberReference.Name);
        var fieldType = DecodeMemberReferenceFieldExactType(memberReference);
        return $"{parentName}.{fieldName}:{fieldType}";
    }

    internal static string GetMemberReferenceFieldLookupSymbol(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName = GetMemberReferenceFieldLookupParentName(reader, memberReference.Parent);
        return $"{parentName}.{reader.GetString(memberReference.Name)}";
    }

    internal static string GetMemberReferenceFieldLookupExactKey(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName =
            NormalizeExactTypeName(GetMemberReferenceFieldLookupParentName(reader, memberReference.Parent));
        var fieldName = reader.GetString(memberReference.Name);
        var fieldType = DecodeMemberReferenceFieldExactType(memberReference);
        return $"{parentName}.{fieldName}:{fieldType}";
    }

    internal static string GetMethodDisplaySymbol(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var definition = reader.GetMethodDefinition(handle);
        var typeName = GetTypeName(reader, definition.GetDeclaringType());
        var methodName = reader.GetString(definition.Name);
        var signature = DecodeMethodSignature(definition, includeReturnType: false);
        return $"{typeName}.{methodName}{signature}";
    }

    internal static string GetMethodExactKey(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var definition = reader.GetMethodDefinition(handle);
        var typeName = NormalizeExactTypeName(GetTypeName(reader, definition.GetDeclaringType()));
        var methodName = reader.GetString(definition.Name);
        var signature = DecodeMethodSignature(definition, includeReturnType: true);
        return $"{typeName}.{methodName}{signature}";
    }

    internal static string GetFieldDefinitionSymbol(MetadataReader reader, FieldDefinitionHandle handle)
    {
        var definition = reader.GetFieldDefinition(handle);
        var typeName = GetTypeName(reader, definition.GetDeclaringType());
        return $"{typeName}.{reader.GetString(definition.Name)}";
    }

    internal static string DecodeFieldDefinitionExactType(FieldDefinition definition)
    {
        try
        {
            return definition.DecodeSignature(new TypeNameProvider(), null);
        }
        catch (BadImageFormatException)
        {
            return "?";
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    internal static string DecodeMemberReferenceFieldExactType(MemberReference memberReference)
    {
        try
        {
            return memberReference.DecodeFieldSignature(new TypeNameProvider(), null);
        }
        catch (BadImageFormatException)
        {
            return "?";
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    internal static bool ShouldTreatCallvirtAsDynamicDispatch(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.Handle(token);
        return handle.Kind switch
        {
            HandleKind.MethodDefinition => IsVirtualDispatchCandidate(reader, (MethodDefinitionHandle)handle),
            HandleKind.MethodSpecification => IsVirtualDispatchCandidate(reader, (MethodSpecificationHandle)handle),
            HandleKind.MemberReference => IsVirtualDispatchCandidate(reader, (MemberReferenceHandle)handle),
            _ => true
        };
    }

    internal static bool IsVirtualDispatchCandidate(MetadataReader reader, MethodSpecificationHandle handle)
    {
        var specification = reader.GetMethodSpecification(handle);
        return specification.Method.Kind switch
        {
            HandleKind.MethodDefinition => IsVirtualDispatchCandidate(reader,
                (MethodDefinitionHandle)specification.Method),
            _ => true
        };
    }

    internal static bool IsVirtualDispatchCandidate(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var definition = reader.GetMethodDefinition(handle);
        var attributes = definition.Attributes;
        if ((attributes & MethodAttributes.Virtual) == 0) return false;

        if ((attributes & MethodAttributes.Final) != 0) return false;

        var declaringType = reader.GetTypeDefinition(definition.GetDeclaringType());
        return (declaringType.Attributes & TypeAttributes.Sealed) == 0;
    }

    internal static bool IsVirtualDispatchCandidate(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var runtimeType = TryResolveRuntimeType(reader, memberReference.Parent);
        if (runtimeType == null) return true;

        if (runtimeType.IsValueType || runtimeType.IsSealed) return false;

        var parameterCount = TryGetMemberReferenceParameterCount(memberReference);
        if (parameterCount == null) return true;

        var methodName = reader.GetString(memberReference.Name);
        var candidates = runtimeType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                method.GetParameters().Length == parameterCount.Value)
            .ToArray();
        if (candidates.Length == 0) return true;

        return candidates.Any(static method =>
            method.IsVirtual && !method.IsFinal && method.DeclaringType?.IsSealed != true);
    }

    internal static int? TryGetMemberReferenceParameterCount(MemberReference memberReference)
    {
        try
        {
            return memberReference.DecodeMethodSignature(new TypeNameProvider(), null).ParameterTypes.Length;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static Type? TryResolveRuntimeType(MetadataReader reader, EntityHandle handle)
    {
        var typeName = handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeName(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetTypeReferenceName(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => DecodeTypeSpecificationForMethodLookup(
                reader,
                (TypeSpecificationHandle)handle),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(typeName)) return null;

        return TryResolveRuntimeType(typeName);
    }

    internal static Type? TryResolveRuntimeType(string typeName)
    {
        return RuntimeTypeCache.GetOrAdd(typeName, static fullName =>
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var resolved = assembly.GetType(fullName, false);
                if (resolved != null) return resolved;
            }

            if (fullName.IndexOfAny(new[] { '<', '>', ',', '!', '*' }) >= 0) return null;

            try
            {
                return Type.GetType(fullName, false);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (FileLoadException)
            {
                return null;
            }
        });
    }

    internal static string GetMemberReferenceSymbol(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName = GetMemberReferenceParentName(reader, memberReference.Parent);
        var name = reader.GetString(memberReference.Name);
        var signature = DecodeMethodSignature(memberReference, includeReturnType: false);
        return $"{parentName}.{name}{signature}";
    }

    internal static string GetMemberReferenceExactKey(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName = NormalizeExactTypeName(GetMemberReferenceParentName(reader, memberReference.Parent));
        var name = reader.GetString(memberReference.Name);
        var signature = DecodeMethodSignature(memberReference, includeReturnType: true);
        return $"{parentName}.{name}{signature}";
    }

    internal static string GetMemberReferenceMethodLookupExactKey(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName =
            NormalizeExactTypeName(GetMemberReferenceMethodLookupParentName(reader, memberReference.Parent));
        var name = reader.GetString(memberReference.Name);
        var signature = DecodeMethodSignature(memberReference, includeReturnType: true);
        return $"{parentName}.{name}{signature}";
    }

    internal static string GetMemberReferenceParentName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeName(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetTypeReferenceName(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => DecodeTypeSpecification(reader, (TypeSpecificationHandle)handle),
            HandleKind.MethodDefinition => GetMethodDisplaySymbol(reader, (MethodDefinitionHandle)handle),
            HandleKind.ModuleReference => reader.GetString(
                reader.GetModuleReference((ModuleReferenceHandle)handle).Name),
            _ => $"metadata-parent:0x{MetadataTokens.GetToken(handle):X8}"
        };
    }

    internal static string GetMemberReferenceFieldLookupParentName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeSpecification => DecodeTypeSpecificationForFieldLookup(reader,
                (TypeSpecificationHandle)handle),
            _ => GetMemberReferenceParentName(reader, handle)
        };
    }

    internal static string GetMemberReferenceMethodLookupParentName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeSpecification => DecodeTypeSpecificationForMethodLookup(reader,
                (TypeSpecificationHandle)handle),
            _ => GetMemberReferenceParentName(reader, handle)
        };
    }

    public static string GetTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        if (handle.IsNil) return "<module>";

        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaringType = definition.GetDeclaringType();
        if (!declaringType.IsNil) return $"{GetTypeName(reader, declaringType)}+{name}";

        var ns = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    public static string GetTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        var ns = reader.GetString(reference.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    internal static string DecodeMethodSignature(
        MethodDefinition definition,
        bool includeReturnType)
    {
        try
        {
            var signature = definition.DecodeSignature(new TypeNameProvider(), null);
            return FormatMethodSignature(signature.ParameterTypes, signature.ReturnType, includeReturnType);
        }
        catch (BadImageFormatException)
        {
            return includeReturnType ? "(?)->?" : "(?)";
        }
    }

    internal static string DecodeMethodSignature(
        MemberReference memberReference,
        bool includeReturnType)
    {
        try
        {
            var signature = memberReference.DecodeMethodSignature(new TypeNameProvider(), null);
            return FormatMethodSignature(signature.ParameterTypes, signature.ReturnType, includeReturnType);
        }
        catch (BadImageFormatException)
        {
            return includeReturnType ? "(?)->?" : string.Empty;
        }
        catch (InvalidOperationException)
        {
            return includeReturnType ? "(?)->?" : string.Empty;
        }
    }

    internal static string FormatMethodSignature(
        IReadOnlyList<string> parameterTypes,
        string returnType,
        bool includeReturnType)
    {
        var parameters = $"({string.Join(", ", parameterTypes)})";
        return includeReturnType ? $"{parameters}->{returnType}" : parameters;
    }

    internal static string NormalizeExactTypeName(string typeName)
    {
        return typeName switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.Char" => "char",
            "System.Decimal" => "decimal",
            "System.Double" => "double",
            "System.Int16" => "short",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.IntPtr" => "nint",
            "System.Object" => "object",
            "System.SByte" => "sbyte",
            "System.Single" => "float",
            "System.String" => "string",
            "System.UInt16" => "ushort",
            "System.UInt32" => "uint",
            "System.UInt64" => "ulong",
            "System.UIntPtr" => "nuint",
            "System.Void" => "void",
            _ => typeName
        };
    }

    internal static string DecodeTypeSpecification(MetadataReader reader, TypeSpecificationHandle handle)
    {
        try
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(new TypeNameProvider(), null);
        }
        catch (BadImageFormatException)
        {
            return "type-spec";
        }
    }

    internal static string DecodeTypeSpecificationForFieldLookup(MetadataReader reader, TypeSpecificationHandle handle)
    {
        return DecodeTypeSpecificationForMemberLookup(reader, handle);
    }

    internal static string DecodeTypeSpecificationForMethodLookup(MetadataReader reader, TypeSpecificationHandle handle)
    {
        return DecodeTypeSpecificationForMemberLookup(reader, handle);
    }

    private static string DecodeTypeSpecificationForMemberLookup(MetadataReader reader, TypeSpecificationHandle handle)
    {
        try
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(
                new TypeNameProvider(true),
                null);
        }
        catch (BadImageFormatException)
        {
            return DecodeTypeSpecification(reader, handle);
        }
        catch (InvalidOperationException)
        {
            return DecodeTypeSpecification(reader, handle);
        }
    }

    internal readonly record struct KnownThrownExceptionSite(int InstructionOffset, string ExceptionType);
}
