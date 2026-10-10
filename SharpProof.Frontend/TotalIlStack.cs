namespace SharpProof.Frontend;

internal static class TotalIlStack
{
    internal const int ObjectReference = 128;
    internal const int StringReference = 129;
    internal const int NullReference = 130;

    internal static bool Supported(SpecialType type)
    { return Width(type) != 0 || type is SpecialType.System_Object or SpecialType.System_String; }

    internal static IrTypeId StorageType(IrFactory factory, int shape)
    {
        return shape switch
        {
            ObjectReference or NullReference => factory.ObjectType,
            StringReference => factory.StringType,
            _ => factory.GetOrCreateIntegerType(Math.Abs(shape), true)
        };
    }

    internal static int Width(SpecialType type)
    {
        return type == SpecialType.System_Boolean ? 32 :
            CSharpOperationSemantics.TryGetScalarInteger(type, out var integer) ? Math.Max(32, integer.BitWidth) : 0;
    }

    // Validate every reachable merge before allocating frame/stack storage.
    // Unknown opcodes also close unreachable code, rather than hiding evidence.
    internal static bool TryValidate(TotalIlBody body, Func<int, bool> spend,
        out ImmutableArray<int>?[] shapes)
    {
        var code = body.Instructions;
        shapes = [];
        if (code.IsEmpty || code.Length > RoslynTotalProgramLowerer.MaximumRegionSteps ||
            body.MaximumStack is < 0 or > 128 || body.Locals.Length > 128 || body.Locals.Any(type => !Supported(type)) ||
            !body.InitializeLocals && !body.Locals.IsEmpty || !spend(code.Length + body.Locals.Length))
        { return false; }
        shapes = new ImmutableArray<int>?[code.Length];
        foreach (var instruction in code)
        {
            if (!Known(instruction.Code) || !spend(1) ||
                instruction.Code.StartsWith("B", StringComparison.Ordinal) && (instruction.Target < 0 || instruction.Target >= code.Length))
            { return false; }
        }
        shapes[0] = [];
        var pending = new Queue<int>();
        pending.Enqueue(0);
        while (pending.Count != 0)
        {
            var index = pending.Dequeue();
            var stack = shapes[index]!.Value.ToList();
            var instruction = code[index];
            if (!spend(stack.Count + 1) || !Transfer(body, instruction, stack) || stack.Count > body.MaximumStack)
            { return false; }
            foreach (var target in Successors(code, index))
            {
                if (target < 0 || target >= code.Length || !spend(stack.Count + 1))
                { return false; }
                if (shapes[target] is { } previous)
                {
                    if (previous.Length != stack.Count)
                    { return false; }
                    var merged = ImmutableArray.CreateBuilder<int>(stack.Count);
                    for (var slot = 0; slot < stack.Count; slot++)
                    {
                        var left = previous[slot];
                        var right = stack[slot];
                        if (left == NullReference && Reference(right))
                        { merged.Add(right); }
                        else if (right == NullReference && Reference(left))
                        { merged.Add(left); }
                        else if (Math.Abs(left) == Math.Abs(right))
                        { merged.Add(left < 0 && right < 0 ? left : Math.Abs(left)); }
                        else
                        { return false; }
                    }
                    var joined = merged.ToImmutable();
                    if (!previous.SequenceEqual(joined))
                    { shapes[target] = joined; pending.Enqueue(target); }
                }
                else
                { shapes[target] = [.. stack]; pending.Enqueue(target); }
            }
        }
        return shapes.All(shape => shape != null);
    }

    internal static IEnumerable<int> Successors(ImmutableArray<TotalIlInstruction> code, int index)
    {
        var instruction = code[index];
        if (instruction.Code == "Ret")
        { yield break; }
        if (instruction.Target >= 0)
        { yield return instruction.Target; }
        if (instruction.Code != "Br")
        { yield return index + 1; }
    }

    private static bool Transfer(TotalIlBody body, TotalIlInstruction instruction, List<int> stack)
    {
        var code = instruction.Code;
        var ordinal = (int)instruction.Operand;
        int Pop()
        {
            if (stack.Count == 0)
            { return 0; }
            var value = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return value;
        }
        bool Binary(bool comparison)
        {
            var right = Pop();
            var left = Pop();
            if (Reference(left) || Reference(right))
            {
                if (!(code == "Ceq" && CompatibleReferences(left, right) ||
                    code == "Cgt_un" && Reference(left) && right == NullReference))
                { return false; }
                stack.Add(-32);
                return true;
            }
            if (left == 0 || Math.Abs(right) != Math.Abs(left))
            { return false; }
            if (code is "And" or "Or" or "Xor")
            {
                stack.Add(left == -32 && right == -32 ? -32 : Math.Abs(left));
                return true;
            }
            stack.Add(comparison ? -32 : Math.Abs(left));
            return true;
        }
        switch (code)
        {
            case "Nop":
            case "Br":
                return true;
            case "Ldarg":
                if (ordinal < 0 || ordinal >= body.Method.Parameters.Length)
                { return false; }
                stack.Add(StackType(body.Method.Parameters[ordinal].Type.SpecialType));
                return stack[stack.Count - 1] != 0;
            case "Ldloc":
                if (ordinal < 0 || ordinal >= body.Locals.Length)
                { return false; }
                stack.Add(StackType(body.Locals[ordinal]));
                return true;
            case "Starg":
                return ordinal >= 0 && ordinal < body.Method.Parameters.Length && Accept(Pop(), body.Method.Parameters[ordinal].Type.SpecialType);
            case "Stloc":
                return ordinal >= 0 && ordinal < body.Locals.Length && Accept(Pop(), body.Locals[ordinal]);
            case "Ldc_i4":
                stack.Add(instruction.Operand is 0 or 1 ? -32 : 32);
                return true;
            case "Ldc_i8":
                stack.Add(64);
                return true;
            case "Ldnull":
                stack.Add(NullReference);
                return true;
            case "Dup":
                if (stack.Count == 0)
                { return false; }
                stack.Add(stack[stack.Count - 1]);
                return true;
            case "Pop":
            case "Brtrue":
            case "Brfalse":
                return Pop() != 0;
            case "Neg":
            case "Not":
                var unary = Pop();
                if (unary == 0 || Reference(unary))
                { return false; }
                stack.Add(Math.Abs(unary));
                return true;
            case "Call":
                if (instruction.Method is not { } method)
                { return false; }
                for (var parameter = method.Parameters.Length - 1; parameter >= 0; parameter--)
                { if (!Accept(Pop(), method.Parameters[parameter].Type.SpecialType)) { return false; } }
                if (!method.ReturnsVoid)
                { stack.Add(StackType(method.ReturnType.SpecialType)); }
                return method.ReturnsVoid || stack[stack.Count - 1] != 0;
            case "Ret":
                return (body.Method.ReturnsVoid || Accept(Pop(), body.Method.ReturnType.SpecialType)) && stack.Count == 0;
        }
        if (code.StartsWith("Conv_", StringComparison.Ordinal))
        {
            var value = Pop();
            if (value == 0 || Reference(value))
            { return false; }
            stack.Add(code.Contains("i8") || code.Contains("u8") ? 64 : 32);
            return true;
        }
        if (code.StartsWith("B", StringComparison.Ordinal))
        {
            var right = Pop();
            var left = Pop();
            return Reference(left) || Reference(right)
                ? code is "Beq" or "Bne_un" && CompatibleReferences(left, right)
                : right != 0 && Math.Abs(left) == Math.Abs(right);
        }
        return Binary(code is "Ceq" or "Cgt" or "Cgt_un" or "Clt" or "Clt_un");
    }

    private static int StackType(SpecialType type)
    {
        return type switch
        {
            SpecialType.System_Boolean => -32,
            SpecialType.System_Object => ObjectReference,
            SpecialType.System_String => StringReference,
            _ => Width(type)
        };
    }

    private static bool Reference(int value)
    { return value is ObjectReference or StringReference or NullReference; }

    private static bool CompatibleReferences(int left, int right)
    { return Reference(left) && Reference(right) && (left == right || left == NullReference || right == NullReference); }

    private static bool Accept(int value, SpecialType type)
    {
        return type is SpecialType.System_Object or SpecialType.System_String
            ? value == StackType(type) || value == NullReference
            : type == SpecialType.System_Boolean ? value == -32 : value != 0 && Math.Abs(value) == Width(type);
    }

    private static bool Known(string code)
    {
        return code is "Nop" or "Ldarg" or "Starg" or "Ldloc" or "Stloc" or "Ldc_i4" or "Ldc_i8" or "Ldnull" or
            "Dup" or "Pop" or "Neg" or "Not" or "And" or "Or" or "Xor" or "Ret" or "Call" or "Br" or "Brtrue" or "Brfalse" or
            "Beq" or "Bne_un" or "Bge" or "Bge_un" or "Bgt" or "Bgt_un" or "Ble" or "Ble_un" or "Blt" or "Blt_un" or
            "Add" or "Add_ovf" or "Add_ovf_un" or "Sub" or "Sub_ovf" or "Sub_ovf_un" or
            "Mul" or "Mul_ovf" or "Mul_ovf_un" or "Div" or "Div_un" or "Rem" or "Rem_un" or
            "Ceq" or "Cgt" or "Cgt_un" or "Clt" or "Clt_un" or
            "Conv_i1" or "Conv_u1" or "Conv_i2" or "Conv_u2" or "Conv_i4" or "Conv_u4" or "Conv_i8" or "Conv_u8" or
            "Conv_ovf_i1" or "Conv_ovf_u1" or "Conv_ovf_i2" or "Conv_ovf_u2" or "Conv_ovf_i4" or "Conv_ovf_u4" or "Conv_ovf_i8" or "Conv_ovf_u8" or
            "Conv_ovf_i1_un" or "Conv_ovf_u1_un" or "Conv_ovf_i2_un" or "Conv_ovf_u2_un" or "Conv_ovf_i4_un" or "Conv_ovf_u4_un" or "Conv_ovf_i8_un" or "Conv_ovf_u8_un";
    }
}
