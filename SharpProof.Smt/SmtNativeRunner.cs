namespace SharpProof.Smt;

// Serializes native checks and drains admitted work before releasing the context.
// Cancellation registration belongs only to the active check, never a queued one.
internal sealed class SmtNativeRunner : IDisposable
{
    private readonly Context _context;
    private readonly object _gate = new();
    private readonly object _lifecycleGate = new();
    private readonly SemaphoreSlim _queryGate = new(1, 1);
    private readonly TaskCompletionSource<bool> _checksDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _retireAfterFailure;
    private readonly Action? _disposeOwned;
    private int _activeCheckCount;
    private int _disposeStarted;
    private int _retired;

    internal SmtNativeRunner(Func<Context> createContext, bool retireAfterFailure = false, Action? disposeOwned = null)
    {
        _context = ArgumentNullGuard.NotNull(
            ArgumentNullGuard.NotNull(createContext, nameof(createContext))(), nameof(createContext));
        _retireAfterFailure = retireAfterFailure;
        _disposeOwned = disposeOwned;
    }

    internal Context Context => _context;

    internal Task<BackendCheckResult> CheckAsync(Func<BackendCheckResult> check, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifecycleGate)
        {
            if (_disposeStarted != 0)
            {
                return Task.FromResult(BackendCheckResult.Unknown(BackendFailureReason.Unavailable));
            }
            _activeCheckCount++;
            return CheckSerializedAsync(check, cancellationToken);
        }
    }

    private async Task<BackendCheckResult> CheckSerializedAsync(Func<BackendCheckResult> check, CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            await _queryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            return await Task.Run(() =>
            {
                lock (_gate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref _retired) != 0 || Volatile.Read(ref _disposeStarted) != 0)
                    {
                        return BackendCheckResult.Unknown(BackendFailureReason.Unavailable);
                    }
                    using var registration = cancellationToken.Register(static state => ((SmtNativeRunner)state!).Interrupt(), this);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var result = check();
                        cancellationToken.ThrowIfCancellationRequested();
                        if (_retireAfterFailure && result.FailureReason is BackendFailureReason.Timeout or BackendFailureReason.InfrastructureFailure)
                        {
                            Interlocked.Exchange(ref _retired, 1);
                        }
                        return result;
                    }
                    catch (OperationCanceledException)
                    {
                        RetireIfRequired();
                        throw;
                    }
                    catch (SmtResourceLimitException)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return BackendCheckResult.Unknown(BackendFailureReason.ResourceLimit);
                    }
                    catch (UnsupportedIrEncodingException)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return BackendCheckResult.Unknown(BackendFailureReason.UnsupportedEncoding);
                    }
                    catch (Exception exception) when (exception is Z3Exception or InvalidOperationException or ArgumentException or ArithmeticException)
                    {
                        // Persistent assertions may have changed before native
                        // bookkeeping failed. A candidate must never reuse them.
                        RetireIfRequired();
                        if (cancellationToken.IsCancellationRequested)
                        {
                            RetireIfRequired();
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        return BackendCheckResult.Unknown(BackendFailureReason.InfrastructureFailure);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (acquired)
            {
                _queryGate.Release();
            }
            lock (_lifecycleGate)
            {
                _activeCheckCount--;
                if (_disposeStarted != 0 && _activeCheckCount == 0)
                {
                    _checksDrained.TrySetResult(true);
                }
            }
        }
    }

    private void Interrupt()
    {
        RetireIfRequired();
        _context.Interrupt();
    }

    private void RetireIfRequired()
    {
        if (_retireAfterFailure)
        {
            Interlocked.Exchange(ref _retired, 1);
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposeStarted != 0)
            {
                return;
            }
            _disposeStarted = 1;
            if (_activeCheckCount == 0)
            {
                _checksDrained.TrySetResult(true);
            }
        }
        _queryGate.Wait();
        try
        {
            lock (_gate)
            {
                try
                {
                    _disposeOwned?.Invoke();
                }
                finally
                {
                    _context.Dispose();
                }
            }
        }
        finally
        {
            _queryGate.Release();
            try
            {
                _checksDrained.Task.GetAwaiter().GetResult();
            }
            finally
            {
                _queryGate.Dispose();
            }
        }
    }
}
