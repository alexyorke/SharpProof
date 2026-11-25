using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Frontend;

// An admitted async method (TotalLoweringContext.IsAdmittedAsync): a root
// verified on its own (AdmitsAsync), or a callee whose call is the operand of
// an await (AwaitsAsync). Calling it runs its body on the caller's thread:
// the compiler's builder starts the state machine, which runs up to the
// first await of an operation that has not completed. The rest of the body
// runs once that operation completes. Every await is lowered as described in
// RoslynTotalExpressionLowerer.Await.cs; otherwise the body is lowered as
// written, with these differences from a synchronous method.
//
// - The call may allocate the returned task, its state machine box and its
//   exception holder, or may return a cached completed task: the body records
//   a possible allocation where the body starts, which no allocation claim
//   can prove absent.
// - `return e` supplies the task's result, so the program's result is e.
// - At the root, an exception that leaves the body does not leave the call.
//   The builder stores it in the returned task, which faults, and the call
//   has returned or will return. A faulted task that nothing observes
//   publishes its exception through TaskScheduler.UnobservedTaskException
//   when it is finalized, and that event runs whatever handlers the process
//   registered. So such an exception reaches an unknown call, then returns.
//   An awaited callee's exception is observed by the await, which rethrows
//   it to the caller as a synchronous call would.
// - `await foreach` and `await using` call the enumerator's and resource's
//   async members, which are not lowered: such a body abstains.
internal sealed partial class RoslynTotalProgramLowerer
{
    private const string AsyncTaskMember = "async-task";
    private const string AsyncFaultMember = "async-fault";
    private const string AsyncResumeMember = "async-resume";

    private bool IsAsyncBody => _context.Target.IsAsync && _context.HasScalarSignature;

    // A root, or a callee whose task the caller does not await: an exception
    // that leaves the body faults the task rather than reaching the caller.
    private bool IsAsyncRoot => IsAsyncBody && (_context.AdmitsAsync || _context.DetachesAsync);

    private void EmitAsyncEntry(IrBlockId entry, IOperation body)
    {
        RepositoryParameterGuard.ThrowIfInvalid(entry.IsDefault, nameof(entry));
        body = RepositoryParameterGuard.NotNull(body, nameof(body));
        if (IsAsyncBody)
        { EmitAsyncCall(entry, body, IrOpaqueCallEffects.Allocates, AsyncTaskMember); }
    }

    private void EmitAsyncCall(IrBlockId block, IOperation site, IrOpaqueCallEffects effects, string name)
    {
        var member = _context.Factory.GetOrCreateMember(_context.Factory.CreateIdentity(), _context.Factory.ObjectType,
            "opaque-call:" + name, _context.Factory.BooleanType, true);
        _builder.Call(block, _context.OpaqueCallSite(site, effects, name), null, member, null);
    }

    // The block an exception that leaves the body reaches.
    private void EmitRootExceptionalExit(IrBlockId block, OperationId structural, IOperation body)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        RepositoryParameterGuard.ThrowIfInvalid(structural.IsDefault, nameof(structural));
        body = RepositoryParameterGuard.NotNull(body, nameof(body));
        if (!IsAsyncRoot)
        {
            _builder.ExceptionalExit(block, structural);
            return;
        }
        EmitAsyncCall(block, body, IrOpaqueCallEffects.All, AsyncFaultMember);
        // At a root the exception stays visible to the exception claims, which
        // state that the returned task never faults or is canceled with it.
        if (_context.AdmitsAsync)
        {
            _builder.ExceptionalExit(block, structural);
            return;
        }
        // A detached task's fault is not the caller's: the call returns.
        Return(block, structural, _context.Result is { } result
            ? CSharpOperationSemantics.DefaultValue(_context.Factory, _context.Factory.GetVariableInfo(result).Type) : null);
    }

    // An awaited callee that may have suspended completes its task from
    // whatever thread resumed it; the caller then resumes later, posted to the
    // synchronization context it captured unless it configured the await not
    // to, after any other code ran meanwhile. That is an unknown call that
    // does not throw: the callee's exceptions already reached the caller.
    private void EmitAsyncResume(IrBlockId block, IOperation site)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        EmitAsyncCall(block, site, IrOpaqueCallEffects.All & ~IrOpaqueCallEffects.Throws, AsyncResumeMember);
    }

    // `await foreach` and `await using` in an async body's own code.
    private bool HasAsyncDisposal(IOperation body)
    {
        body = RepositoryParameterGuard.NotNull(body, nameof(body));
        return IsAsyncBody && OwnNodes(body.Syntax).Any(static node => node is CommonForEachStatementSyntax { AwaitKeyword.RawKind: not 0 } or
            UsingStatementSyntax { AwaitKeyword.RawKind: not 0 } or LocalDeclarationStatementSyntax { AwaitKeyword.RawKind: not 0 });
    }

    // Whether a body may suspend: it has an await of its own. A nested lambda
    // or local function is a separate method with its own state machine.
    internal static bool MaySuspend(SyntaxNode body)
    {
        body = RepositoryParameterGuard.NotNull(body, nameof(body));
        return OwnNodes(body).Any(static node => node is AwaitExpressionSyntax or
            CommonForEachStatementSyntax { AwaitKeyword.RawKind: not 0 } or
            UsingStatementSyntax { AwaitKeyword.RawKind: not 0 } or LocalDeclarationStatementSyntax { AwaitKeyword.RawKind: not 0 });
    }

    private static IEnumerable<SyntaxNode> OwnNodes(SyntaxNode body)
    {
        return body.DescendantNodes(node => node == body || node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));
    }

    // `await M(...)` of a source async method: its frame is lowered and its
    // value is the frame's result. Dispatched calls stay with their target.
    private TotalBodyValue? InlineAwaitedSourceCall(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        if (_preserveSourceCall != null || _calls == null)
        { return null; }
        return ExactVirtualTarget(invocation.TargetMethod, invocation.Instance, baseCall: !invocation.IsVirtual) is { } exact
            ? InlineSourceMember(invocation, exact, invocation.Instance, invocation.Arguments, block, depth, exactTarget: true, awaited: true)
            : InlineSourceMember(invocation, invocation.TargetMethod, invocation.Instance, invocation.Arguments, block, depth, awaited: true);
    }
}
