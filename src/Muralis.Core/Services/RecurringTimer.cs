namespace Muralis.Services;

/// <summary>
/// Minuterie périodique au-dessus de <see cref="TimeProvider"/> (remplace
/// <c>DispatcherTimer</c>, #52) : chaque tick est posté sur le
/// <see cref="SynchronizationContext"/> fourni (l'UI thread dans l'app), ou exécuté sur place
/// sans contexte (tests, avec un <c>FakeTimeProvider</c>). Démarrage/arrêt idempotents.
/// </summary>
public sealed class RecurringTimer : IDisposable
{
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _context;
    private readonly Action _tick;
    private ITimer? _timer;

    public RecurringTimer(TimeProvider time, SynchronizationContext? context, TimeSpan interval, Action tick)
    {
        _time = time;
        _context = context;
        Interval = interval;
        _tick = tick;
    }

    public TimeSpan Interval { get; }

    public bool IsEnabled { get; private set; }

    /// <summary>(Re)démarre : le prochain tick tombe un intervalle complet plus tard.</summary>
    public void Start()
    {
        IsEnabled = true;
        if (_timer is null)
            _timer = _time.CreateTimer(_ => OnElapsed(), null, Interval, Interval);
        else
            _timer.Change(Interval, Interval);
    }

    public void Stop()
    {
        IsEnabled = false;
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void OnElapsed()
    {
        if (_context is null)
            Run();
        else
            _context.Post(_ => Run(), null);
    }

    private void Run()
    {
        // Un tick déjà posté juste avant un Stop ne doit plus s'exécuter.
        if (!IsEnabled)
            return;
        try
        {
            _tick();
        }
        catch (Exception ex)
        {
            Log.Error("Timer tick failed", ex);
        }
    }

    public void Dispose()
    {
        IsEnabled = false;
        _timer?.Dispose();
        _timer = null;
    }
}
