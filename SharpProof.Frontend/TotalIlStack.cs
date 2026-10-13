namespace SharpProof.Frontend;

internal static class TotalIlStack
{
    internal static bool Supported(SpecialType type)
    { return TotalIlStackType.TryFromSpecialType(type, out _); }

    // Validate every reachable merge before allocating frame/stack storage.
    // Unknown opcodes also close unreachable code, rather than hiding evidence.
    internal static bool TryValidate(TotalIlBody body, Func<int, bool> spend,
        out ImmutableArray<TotalIlStackType>?[] shapes)
    {
        var code = body.Instructions;
        shapes = [];
        if (code.IsEmpty || code.Length > RoslynTotalProgramLowerer.MaximumRegionSteps ||
            body.MaximumStack is < 0 or > 128 || body.Locals.Length > 128 || body.Locals.Any(type => !Supported(type)) ||
            !body.InitializeLocals && !body.Locals.IsEmpty || !spend(code.Length + body.Locals.Length))
        { return false; }
        shapes = new ImmutableArray<TotalIlStackType>?[code.Length];
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
                    var merged = ImmutableArray.CreateBuilder<TotalIlStackType>(stack.Count);
                    for (var slot = 0; slot < stack.Count; slot++)
                    {
                        if (!previous[slot].TryJoin(stack[slot], out var joinedValue))
                        { return false; }
                        merged.Add(joinedValue);
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

    private static bool Transfer(TotalIlBody body, TotalIlInstruction instruction, List<TotalIlStackType> stack)
    {
        var code = instruction.Code;
        // An operand outside the int range can never name a parameter or local;
        // -1 makes the bounds checks below reject it instead of truncating it.
        var ordinal = instruction.Operand is >= int.MinValue and <= int.MaxValue
            ? (int)instruction.Operand
            : -1;
        TotalIlStackType Pop()
        {
            if (stack.Count == 0)
            { return default; }
            var value = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return value;
        }
        bool Binary(bool comparison)
        {
            var right = Pop();
            var left = Pop();
            if (left.IsReference || right.IsReference)
            {
                if (!(code == "Ceq" && TotalIlStackType.CompatibleReferences(left, right) ||
                    code == "Cgt_un" && left.IsReference && right.IsNullReference))
                { return false; }
                stack.Add(TotalIlStackType.BooleanCompatibleI4);
                return true;
            }
            if (!left.IsValid || right.NumericWidth != left.NumericWidth)
            { return false; }
            if (code is "And" or "Or" or "Xor")
            {
                stack.Add(left.IsBooleanCompatibleI4 && right.IsBooleanCompatibleI4
                    ? TotalIlStackType.BooleanCompatibleI4 : left.WidenBooleanCompatibleI4());
                return true;
            }
            stack.Add(comparison ? TotalIlStackType.BooleanCompatibleI4 : left.WidenBooleanCompatibleI4());
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
                if (!TotalIlStackType.TryFromTypeSymbol(body.Method.Parameters[ordinal].Type, out var parameterType))
                { return false; }
                stack.Add(parameterType);
                return true;
            case "Ldloc":
                if (ordinal < 0 || ordinal >= body.Locals.Length)
                { return false; }
                stack.Add(TotalIlStackType.FromSupportedSpecialType(body.Locals[ordinal]));
                return true;
            case "Starg":
                return ordinal >= 0 && ordinal < body.Method.Parameters.Length && Pop().Accepts(body.Method.Parameters[ordinal].Type);
            case "Stloc":
                return ordinal >= 0 && ordinal < body.Locals.Length && Pop().Accepts(body.Locals[ordinal]);
            case "Ldc_i4":
                stack.Add(TotalIlStackType.FromInt32Constant(instruction.Operand));
                return true;
            case "Ldc_i8":
                stack.Add(TotalIlStackType.Integer64);
                return true;
            case "Ldnull":
                stack.Add(TotalIlStackType.NullFromIl);
                return true;
            case "Dup":
                if (stack.Count == 0)
                { return false; }
                stack.Add(stack[stack.Count - 1]);
                return true;
            case "Pop":
            case "Brtrue":
            case "Brfalse":
                return Pop().IsValid;
            case "Neg":
            case "Not":
                var unary = Pop();
                if (!unary.IsValid || unary.IsReference)
                { return false; }
                stack.Add(unary.WidenBooleanCompatibleI4());
                return true;
            case "Call":
                if (instruction.Method is not { } method)
                { return false; }
                for (var parameter = method.Parameters.Length - 1; parameter >= 0; parameter--)
                { if (!Pop().Accepts(method.Parameters[parameter].Type)) { return false; } }
                if (!method.ReturnsVoid)
                {
                    if (!TotalIlStackType.TryFromTypeSymbol(method.ReturnType, out var returnType))
                    { return false; }
                    stack.Add(returnType);
                }
                return method.ReturnsVoid || stack[stack.Count - 1].IsValid;
            case "Ret":
                return (body.Method.ReturnsVoid || Pop().Accepts(body.Method.ReturnType)) && stack.Count == 0;
        }
        if (code.StartsWith("Conv_", StringComparison.Ordinal))
        {
            var value = Pop();
            if (!value.IsValid || value.IsReference)
            { return false; }
            stack.Add(code.Contains("i8") || code.Contains("u8") ? TotalIlStackType.Integer64 : TotalIlStackType.Integer32);
            return true;
        }
        if (code.StartsWith("B", StringComparison.Ordinal))
        {
            var right = Pop();
            var left = Pop();
            return left.IsReference || right.IsReference
                ? code is "Beq" or "Bne_un" && TotalIlStackType.CompatibleReferences(left, right)
                : right.IsValid && left.NumericWidth == right.NumericWidth;
        }
        if (code is "Shl" or "Shr" or "Shr_un")
        {
            var count = Pop();
            var value = Pop();
            if (count.NumericWidth != 32 || value.NumericWidth is not (32 or 64))
            { return false; }
            stack.Add(value.WidenBooleanCompatibleI4());
            return true;
        }
        return Binary(code is "Ceq" or "Cgt" or "Cgt_un" or "Clt" or "Clt_un");
    }


    private static bool Known(string code)
    {
        return code is "Nop" or "Ldarg" or "Starg" or "Ldloc" or "Stloc" or "Ldc_i4" or "Ldc_i8" or "Ldnull" or
            "Dup" or "Pop" or "Neg" or "Not" or "And" or "Or" or "Xor" or "Shl" or "Shr" or "Shr_un" or "Ret" or "Call" or "Br" or "Brtrue" or "Brfalse" or
            "Beq" or "Bne_un" or "Bge" or "Bge_un" or "Bgt" or "Bgt_un" or "Ble" or "Ble_un" or "Blt" or "Blt_un" or
            "Add" or "Add_ovf" or "Add_ovf_un" or "Sub" or "Sub_ovf" or "Sub_ovf_un" or
            "Mul" or "Mul_ovf" or "Mul_ovf_un" or "Div" or "Div_un" or "Rem" or "Rem_un" or
            "Ceq" or "Cgt" or "Cgt_un" or "Clt" or "Clt_un" or
            "Conv_i1" or "Conv_u1" or "Conv_i2" or "Conv_u2" or "Conv_i4" or "Conv_u4" or "Conv_i8" or "Conv_u8" or
            "Conv_ovf_i1" or "Conv_ovf_u1" or "Conv_ovf_i2" or "Conv_ovf_u2" or "Conv_ovf_i4" or "Conv_ovf_u4" or "Conv_ovf_i8" or "Conv_ovf_u8" or
            "Conv_ovf_i1_un" or "Conv_ovf_u1_un" or "Conv_ovf_i2_un" or "Conv_ovf_u2_un" or "Conv_ovf_i4_un" or "Conv_ovf_u4_un" or "Conv_ovf_i8_un" or "Conv_ovf_u8_un";
    }
}
