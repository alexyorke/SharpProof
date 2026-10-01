namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private TotalBodyValue? InlineMetadataCall(IInvocationOperation invocation, TotalIlBody body, IrBlockId block, int depth)
    {
        if (invocation.Instance != null || invocation.Arguments.Length != body.Method.Parameters.Length)
        { return null; }
        var arguments = new IrTerm[body.Method.Parameters.Length];
        var seen = new HashSet<int>();
        foreach (var argument in invocation.Arguments)
        {
            SpendRegion();
            if (argument.Parameter is not { } parameter || !seen.Add(parameter.Ordinal) ||
                argument.ArgumentKind is not (ArgumentKind.Explicit or ArgumentKind.DefaultValue))
            { return null; }
            var value = _expressions.LowerBodyValue(argument.Value, block, depth + 1);
            block = value.Continuation;
            if (!value.Classification.IsExact)
            { return null; }
            arguments[parameter.Ordinal] = value.Value;
        }
        var region = _regionGraph == null ? null : _regionSource.EnclosingRegion;
        var filter = region == null ? null : EnclosingRegionFilter(region);
        var anchor = _context.Site(invocation);
        return ExpandIl(body, arguments, block, anchor,
            (kind, site) => ContinueSourceException(region, filter, kind, site, static target => target));
    }

    private TotalBodyValue ExpandIl(TotalIlBody body, IrTerm[] arguments, IrBlockId caller, OperationId anchor,
        Func<IrExceptionKind, OperationId, IrBlockId> exceptionTarget)
    {
        if (_calls == null || !_calls.EnterIl(body.Method))
        { throw new RegionIncompleteException(); }
        try
        {
            if (!TotalIlStack.TryValidate(body, _calls.Spend, out var shapes) ||
                !_calls.Spend(body.Method.Parameters.Length * 3 + body.Locals.Length + 3))
            { throw new RegionIncompleteException(); }
            var factory = _context.Factory;
            var frame = _context.CreateFrame(body.Method);
            var entry = _builder.CreateBlock("il:entry");
            var continued = _builder.CreateBlock("il:continued");
            var result = body.Method.ReturnsVoid ? (IrVarId?)null : _context.Temporary(frame.Type(body.Method.ReturnType));
            if (result is { } initialized)
            {
                var type = factory.GetVariableInfo(initialized).Type;
                _builder.Assign(caller, anchor, initialized, CSharpOperationSemantics.DefaultValue(factory, type));
            }
            foreach (var parameter in frame.Parameters)
            {
                _builder.Assign(caller, anchor, parameter.Entry, arguments[parameter.Parameter.Ordinal]);
                _builder.Assign(entry, anchor, parameter.Current, factory.Variable(parameter.Entry));
                _builder.Assign(entry, anchor, parameter.PreState, factory.Variable(parameter.Entry));
            }
            var locals = body.Locals.Select(type => _context.Temporary(type == SpecialType.System_Object ? factory.ObjectType :
                type == SpecialType.System_String ? factory.StringType : CSharpOperationSemantics.MapType(factory, type)!.Value)).ToArray();
            foreach (var local in locals)
            {
                var type = factory.GetVariableInfo(local).Type;
                _builder.Assign(entry, anchor, local, CSharpOperationSemantics.DefaultValue(factory, type));
            }
            var blocks = new IrBlockId[shapes.Length];
            var storage = new IrVarId[shapes.Length][];
            for (var index = 0; index < shapes.Length; index++)
            {
                if (shapes[index] is not { } shape)
                { continue; }
                if (!_calls.Spend(shape.Length + 1))
                { throw new RegionIncompleteException(); }
                blocks[index] = _builder.CreateBlock("il:" + body.Instructions[index].Offset.ToString(CultureInfo.InvariantCulture));
                storage[index] = [.. shape.Select(value => _context.Temporary(TotalIlStack.StorageType(factory, value)))];
            }
            _builder.Goto(caller, anchor, entry);
            _builder.Goto(entry, anchor, blocks[0]);
            for (var index = 0; index < shapes.Length; index++)
            {
                if (shapes[index] == null)
                { continue; }
                SpendRegion();
                var instruction = body.Instructions[index];
                var site = factory.CreateOperation("il:" + body.Module + ":" + body.Method.MetadataToken.ToString("X8", CultureInfo.InvariantCulture) +
                    ":" + instruction.Offset.ToString("X4", CultureInfo.InvariantCulture) + ":" + body.ImageSha256,
                    factory.GetOperationInfo(anchor).SourceSpan);
                var block = blocks[index];
                var stack = storage[index].Select((variable, slot) => shapes[index]!.Value[slot] == TotalIlStack.NullReference
                    ? (IrTerm)factory.Null(factory.ObjectType) : factory.Variable(variable)).ToList();
                IrTerm Pop()
                { var value = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1); return value; }
                IrBlockId Edge(int target)
                {
                    if (!_calls.Spend(stack.Count + 2))
                    { throw new RegionIncompleteException(); }
                    var edge = _builder.CreateBlock("il:edge");
                    for (var slot = 0; slot < stack.Count; slot++)
                    {
                        _builder.Assign(edge, site, storage[target][slot], stack[slot] is IrNullTerm
                        ? factory.Null(factory.GetVariableInfo(storage[target][slot]).Type) : stack[slot]);
                    }
                    _builder.Goto(edge, site, blocks[target]);
                    return edge;
                }
                IrTerm Rule(TotalScalarRule rule)
                {
                    foreach (var fault in rule.Throws)
                    {
                        if (!_calls.Spend(4))
                        { throw new RegionIncompleteException(); }
                        var thrown = _builder.CreateBlock("il:throw");
                        var normal = _builder.CreateBlock("il:normal");
                        _builder.Branch(block, site, fault.Condition, thrown, normal);
                        _builder.Throw(thrown, site, fault.Kind, exceptionTarget(fault.Kind, site));
                        block = normal;
                    }
                    return rule.Value;
                }
                var code = instruction.Code;
                var ordinal = (int)instruction.Operand;
                var terminal = false;
                switch (code)
                {
                    case "Nop":
                        break;
                    case "Ldarg":
                        stack.Add(IlLoad(factory, factory.Variable(frame.Parameters[ordinal].Current)));
                        break;
                    case "Ldloc":
                        stack.Add(IlLoad(factory, factory.Variable(locals[ordinal])));
                        break;
                    case "Starg":
                        _builder.Assign(block, site, frame.Parameters[ordinal].Current,
                        IlStore(factory, Pop(), frame.Type(body.Method.Parameters[ordinal].Type)));
                        break;
                    case "Stloc":
                        _builder.Assign(block, site, locals[ordinal],
                        IlStore(factory, Pop(), factory.GetVariableInfo(locals[ordinal]).Type));
                        break;
                    case "Ldc_i4":
                        stack.Add(factory.Integer(factory.GetOrCreateIntegerType(32, true), instruction.Operand));
                        break;
                    case "Ldc_i8":
                        stack.Add(factory.Integer(factory.GetOrCreateIntegerType(64, true), instruction.Operand));
                        break;
                    case "Ldnull":
                        stack.Add(factory.Null(factory.ObjectType));
                        break;
                    case "Dup":
                        stack.Add(stack[stack.Count - 1]);
                        break;
                    case "Pop":
                        Pop();
                        break;
                    case "Neg":
                        stack.Add(factory.Unary(IrUnaryOperator.Negate, Pop()));
                        break;
                    case "Ret":
                        if (result is { } target)
                        { _builder.Assign(block, site, target, IlStore(factory, Pop(), factory.GetVariableInfo(target).Type)); }
                        _builder.Goto(block, site, continued);
                        terminal = true;
                        break;
                    case "Call":
                        var dependency = _calls.PrepareIl(instruction.Method!) ?? throw new RegionIncompleteException();
                        var values = new IrTerm[dependency.Method.Parameters.Length];
                        for (var parameter = values.Length - 1; parameter >= 0; parameter--)
                        { values[parameter] = IlStore(factory, Pop(), frame.Type(dependency.Method.Parameters[parameter].Type)); }
                        var called = ExpandIl(dependency, values, block, site, exceptionTarget);
                        block = called.Continuation;
                        if (!dependency.Method.ReturnsVoid)
                        { stack.Add(IlLoad(factory, called.Value)); }
                        break;
                    case "Br":
                        _builder.Goto(block, site, Edge(instruction.Target));
                        terminal = true;
                        break;
                    case "Brtrue":
                    case "Brfalse":
                        var tested = Pop();
                        var condition = factory.Binary(IrBinaryOperator.NotEqual, tested, CSharpOperationSemantics.DefaultValue(factory, tested.Type));
                        if (code == "Brfalse")
                        { condition = factory.Unary(IrUnaryOperator.Not, condition); }
                        _builder.Branch(block, site, condition, Edge(instruction.Target), Edge(index + 1));
                        terminal = true;
                        break;
                    default:
                        if (code.StartsWith("Conv_", StringComparison.Ordinal))
                        { stack.Add(IlLoad(factory, Rule(IlConversion(factory, code, Pop())))); }
                        else
                        {
                            var right = Pop();
                            var left = Pop();
                            if (code.StartsWith("B", StringComparison.Ordinal))
                            { _builder.Branch(block, site, IlComparison(factory, code, left, right), Edge(instruction.Target), Edge(index + 1)); terminal = true; }
                            else if (code.StartsWith("C", StringComparison.Ordinal))
                            { stack.Add(IlLoad(factory, IlComparison(factory, code, left, right))); }
                            else
                            { stack.Add(IlLoad(factory, Rule(IlArithmetic(factory, code, left, right)))); }
                        }
                        break;
                }
                if (!terminal)
                { _builder.Goto(block, site, Edge(index + 1)); }
            }
            return new(result is { } returned ? factory.Variable(returned) : factory.Boolean(false), continued, FrontendSubsetClassification.Exact);
        }
        finally { _calls.LeaveIl(body.Method); }
    }

    private static IrTerm IlLoad(IrFactory factory, IrTerm value)
    {
        var info = factory.GetTypeInfo(value.Type);
        if (info.Kind is IrTypeKind.Reference or IrTypeKind.String)
        { return value; }
        var stack = factory.GetOrCreateIntegerType(info.Width == 64 ? 64 : 32, true);
        return value.Type == factory.BooleanType
            ? factory.Conditional(value, factory.Integer(stack, 1), factory.Integer(stack, 0)) : factory.Cast(stack, value);
    }

    private static IrTerm IlStore(IrFactory factory, IrTerm value, IrTypeId type)
    {
        // Stack validation admits only normalized 0/1 producers at Bool stores.
        if (factory.GetTypeInfo(type).Kind is IrTypeKind.Reference or IrTypeKind.String)
        { return value is IrNullTerm ? factory.Null(type) : value; }
        return type == factory.BooleanType ? factory.Binary(IrBinaryOperator.NotEqual, value, factory.Integer(value.Type, 0)) : factory.Cast(type, value);
    }

    private static TotalScalarRule IlConversion(IrFactory factory, string code, IrTerm value)
    {
        var checkedConversion = code.StartsWith("Conv_ovf_", StringComparison.Ordinal);
        var suffix = code.Substring(checkedConversion ? 9 : 5);
        var unsignedSource = suffix.EndsWith("_un", StringComparison.Ordinal);
        if (unsignedSource)
        { suffix = suffix.Substring(0, suffix.Length - 3); }
        // conv.u8 zero-extends an i32 stack value; conv.i8 sign-extends it.
        if (unsignedSource || !checkedConversion && suffix == "u8")
        { value = factory.Cast(factory.GetOrCreateIntegerType(factory.GetTypeInfo(value.Type).Width, false), value); }
        var target = factory.GetOrCreateIntegerType(int.Parse(suffix.Substring(1), CultureInfo.InvariantCulture) * 8, suffix[0] == 'i');
        return CSharpOperationSemantics.ConvertInteger(factory, value, target, checkedConversion);
    }

    private static IrTerm IlComparison(IrFactory factory, string code, IrTerm left, IrTerm right)
    {
        if (factory.GetTypeInfo(left.Type).Kind is IrTypeKind.Reference or IrTypeKind.String)
        {
            if (left is IrNullTerm)
            { left = factory.Null(right.Type); }
            if (right is IrNullTerm)
            { right = factory.Null(left.Type); }
            return factory.Binary(code is "Bne_un" or "Cgt_un" ? IrBinaryOperator.NotEqual : IrBinaryOperator.Equal, left, right);
        }
        if (code.EndsWith("_un", StringComparison.Ordinal))
        { var type = factory.GetOrCreateIntegerType(factory.GetTypeInfo(left.Type).Width, false); left = factory.Cast(type, left); right = factory.Cast(type, right); }
        var operation = code switch
        {
            "Beq" or "Ceq" => IrBinaryOperator.Equal,
            "Bne_un" => IrBinaryOperator.NotEqual,
            "Bge" or "Bge_un" => IrBinaryOperator.GreaterThanOrEqual,
            "Ble" or "Ble_un" => IrBinaryOperator.LessThanOrEqual,
            "Bgt" or "Bgt_un" or "Cgt" or "Cgt_un" => IrBinaryOperator.GreaterThan,
            _ => IrBinaryOperator.LessThan
        };
        return factory.Binary(operation, left, right);
    }

    private static TotalScalarRule IlArithmetic(IrFactory factory, string code, IrTerm left, IrTerm right)
    {
        if (code.EndsWith("_un", StringComparison.Ordinal))
        { var type = factory.GetOrCreateIntegerType(factory.GetTypeInfo(left.Type).Width, false); left = factory.Cast(type, left); right = factory.Cast(type, right); }
        var operation = code.StartsWith("Add", StringComparison.Ordinal) ? IrBinaryOperator.Add :
            code.StartsWith("Sub", StringComparison.Ordinal) ? IrBinaryOperator.Subtract :
            code.StartsWith("Mul", StringComparison.Ordinal) ? IrBinaryOperator.Multiply :
            code.StartsWith("Div", StringComparison.Ordinal) ? IrBinaryOperator.Divide : IrBinaryOperator.Remainder;
        if (operation is IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply)
        { return CSharpOperationSemantics.IntegerArithmetic(factory, operation, left, right, code.Contains("_ovf")); }
        return CSharpOperationSemantics.DivideOrRemainder(factory, operation, left, right);
    }
}
