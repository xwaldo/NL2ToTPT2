namespace Importer.Windows.Automation;

internal sealed class AsyncPauseGate
{
    private TaskCompletionSource<bool>? _resumeSource;

    public bool IsPaused => Volatile.Read(ref _resumeSource) is not null;

    public bool Pause()
    {
        var source = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return Interlocked.CompareExchange(ref _resumeSource, source, null) is null;
    }

    public bool Resume()
    {
        var source = Interlocked.Exchange(ref _resumeSource, null);
        return source?.TrySetResult(true) == true;
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        var source = Volatile.Read(ref _resumeSource);
        if (source is not null)
        {
            await source.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

