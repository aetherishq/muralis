using System.Collections.Concurrent;

namespace Muralis.Services;

/// <summary>
/// Thread STA dédié, en file : l'imagerie WPF (<c>RenderTargetBitmap</c>) exige un thread STA,
/// mais pas l'UI thread. La composition (décodage + rendu + PNG, lourde en 4K) y tourne pour
/// que l'UI de Muralis ne gèle jamais (#41). Un seul thread : les compositions sont sérialisées.
/// </summary>
internal sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();

    public StaWorker(string name)
    {
        var thread = new Thread(() =>
        {
            foreach (var work in _queue.GetConsumingEnumerable())
                work();
        })
        {
            IsBackground = true,
            Name = name,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public Task<T> Run<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    public void Dispose() => _queue.CompleteAdding();
}
