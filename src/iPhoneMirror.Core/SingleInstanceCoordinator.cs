using System.IO.Pipes;
using System.Text;

namespace iPhoneMirror.Core;

public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    public const string MutexName = "Local\\iPhoneMirror.Instance";
    public const string PipeName = "iPhoneMirror.Control";

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _ownsMutex;
    private Task? _serverTask;

    public SingleInstanceCoordinator(string? mutexName = null, string? pipeName = null)
    {
        _pipeName = pipeName ?? PipeName;
        // The named kernel object itself is the single-instance lease. Avoid
        // owning the mutex because Mutex ownership is thread-affine and this
        // coordinator is intentionally disposed across async continuations.
        _mutex = new Mutex(initiallyOwned: false, mutexName ?? MutexName, out var createdNew);
        _ownsMutex = createdNew;
    }

    public bool IsPrimary => _ownsMutex;

    public void StartServer(Func<string, Task<string>> handler)
    {
        if (!IsPrimary)
        {
            throw new InvalidOperationException("Only the primary instance can host the command pipe.");
        }
        if (_serverTask is not null)
        {
            return;
        }
        _serverTask = RunServerAsync(_pipeName, handler, _lifetime.Token);
    }

    private static async Task RunServerAsync(string pipeName, Func<string, Task<string>> handler, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var command = await reader.ReadLineAsync(cancellationToken);
                var result = command is null ? "error" : await handler(command);
                await writer.WriteLineAsync(result.AsMemory(), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
            }
        }
    }

    public static async Task<string> SendAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        string? pipeName = null)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(3));
        await using var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeoutCts.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(command.AsMemory(), timeoutCts.Token);
        return await reader.ReadLineAsync(timeoutCts.Token) ?? "error";
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_serverTask is not null)
        {
            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _ownsMutex = false;
        _mutex.Dispose();
        _lifetime.Dispose();
    }
}
