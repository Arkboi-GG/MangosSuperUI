namespace MangosSuperUI.Services;

/// <summary>
/// Coordinates ordinary queue work with destructive replacement of the world/admin
/// databases. Once maintenance is requested, new shared leases fail immediately while
/// the writer waits for operations that already hold a lease to finish.
/// </summary>
public sealed class WorldMaintenanceGate
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _maintenanceSerial = new(1, 1);
    private int _activeOperations;
    private bool _maintenanceRequested;
    private bool _maintenanceActive;
    private TaskCompletionSource<bool>? _operationsDrained;

    /// <summary>
    /// Attempts to enter ordinary operation mode. This deliberately does not wait:
    /// callers arriving after a restore request must fail closed instead of racing it.
    /// </summary>
    public ValueTask<Lease?> TryAcquireOperationAsync()
    {
        lock (_sync)
        {
            if (_maintenanceRequested)
                return ValueTask.FromResult<Lease?>(null);

            _activeOperations++;
            return ValueTask.FromResult<Lease?>(new Lease(this, maintenance: false));
        }
    }

    /// <summary>
    /// Requests exclusive maintenance and asynchronously drains existing operations.
    /// Cancellation is honored before the request becomes visible; once visible, the
    /// drain completes so the gate cannot be stranded half-closed.
    /// </summary>
    public async ValueTask<Lease> AcquireMaintenanceAsync(
        CancellationToken cancellationToken = default)
    {
        await _maintenanceSerial.WaitAsync(cancellationToken);
        try
        {
            Task drained;
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _maintenanceRequested = true;
                if (_activeOperations == 0)
                {
                    _maintenanceActive = true;
                    drained = Task.CompletedTask;
                }
                else
                {
                    _operationsDrained = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    drained = _operationsDrained.Task;
                }
            }

            // Do not abandon a published maintenance request. Existing operation
            // holders are bounded and must drain before destructive restore starts.
            await drained;
            return new Lease(this, maintenance: true);
        }
        catch
        {
            _maintenanceSerial.Release();
            throw;
        }
    }

    private void ReleaseOperation()
    {
        TaskCompletionSource<bool>? drained = null;
        lock (_sync)
        {
            if (_activeOperations <= 0)
                throw new InvalidOperationException("World maintenance operation lease underflow.");

            _activeOperations--;
            if (_activeOperations == 0 && _maintenanceRequested)
            {
                _maintenanceActive = true;
                drained = _operationsDrained;
                _operationsDrained = null;
            }
        }

        drained?.TrySetResult(true);
    }

    private void ReleaseMaintenance()
    {
        lock (_sync)
        {
            if (!_maintenanceRequested || !_maintenanceActive)
                throw new InvalidOperationException("No world maintenance lease is active.");

            _maintenanceActive = false;
            _maintenanceRequested = false;
        }

        _maintenanceSerial.Release();
    }

    public sealed class Lease : IAsyncDisposable
    {
        private WorldMaintenanceGate? _owner;
        private readonly bool _maintenance;

        internal Lease(WorldMaintenanceGate owner, bool maintenance)
        {
            _owner = owner;
            _maintenance = maintenance;
        }

        public ValueTask DisposeAsync()
        {
            WorldMaintenanceGate? owner = Interlocked.Exchange(ref _owner, null);
            if (owner != null)
            {
                if (_maintenance)
                    owner.ReleaseMaintenance();
                else
                    owner.ReleaseOperation();
            }

            return ValueTask.CompletedTask;
        }
    }
}
