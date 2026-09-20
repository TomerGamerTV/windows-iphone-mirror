using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace iPhoneMirror.Core;

public sealed class WorkerProtocolClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Process _process;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _stdoutTask;
    private Task? _stderrTask;

    public WorkerProtocolClient(Process process)
    {
        _process = process;
    }

    public event EventHandler<WorkerEvent>? EventReceived;
    public event EventHandler? WorkerExited;

    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;

    public static async Task<WorkerProtocolClient> StartAsync(
        string pythonExe,
        string workerScript,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pythonExe))
        {
            throw new FileNotFoundException("Private Python runtime was not found.", pythonExe);
        }
        if (!File.Exists(workerScript))
        {
            throw new FileNotFoundException("CoreDevice worker was not found.", workerScript);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(workerScript)!,
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(workerScript);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("The CoreDevice worker could not be started.");
        }

        var client = new WorkerProtocolClient(process);
        client._stdoutTask = client.ReadStdoutAsync(client._lifetime.Token);
        client._stderrTask = client.DrainStderrAsync(client._lifetime.Token);
        process.Exited += (_, _) => client.OnExited();
        await client.SendCommandAsync("hello", new { protocol_version = 1 }, TimeSpan.FromSeconds(8), cancellationToken);
        return client;
    }

    public async Task<JsonElement> SendCommandAsync(
        string command,
        object? payload = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_lifetime.IsCancellationRequested, this);
        if (_process.HasExited)
        {
            throw new InvalidOperationException("The CoreDevice worker is not running.");
        }

        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Unable to allocate a worker request id.");
        }

        try
        {
            var line = JsonSerializer.Serialize(new { id, command, payload = payload ?? new { } }, JsonOptions);
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
                await _process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
            return await completion.Task.WaitAsync(timeoutCts.Token);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Writes an input command without waiting for the worker's response.
    /// High-frequency touch and scroll events must not serialize their UI
    /// latency behind a round trip over Wi-Fi. The worker still processes the
    /// commands in stdin order; any response is intentionally ignored.
    /// </summary>
    public async Task SendCommandNoWaitAsync(string command, object? payload = null, CancellationToken cancellationToken = default)
    {
        try
        {
            ObjectDisposedException.ThrowIf(_lifetime.IsCancellationRequested, this);
            if (_process.HasExited) return;

            var id = Guid.NewGuid().ToString("N");
            var line = JsonSerializer.Serialize(new { id, command, payload = payload ?? new { } }, JsonOptions);
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
                await _process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async Task ReadStdoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }
                HandleLine(line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            FailPending(new InvalidOperationException("The worker protocol stream failed."));
        }
    }

    private async Task DrainStderrAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && await _process.StandardError.ReadLineAsync(cancellationToken) is not null)
            {
                // stderr is intentionally diagnostic-only. Do not surface or persist its text here.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void HandleLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "response")
            {
                var id = root.GetProperty("id").GetString();
                if (id is null || !_pending.TryGetValue(id, out var pending))
                {
                    return;
                }

                if (root.GetProperty("ok").GetBoolean())
                {
                    var data = root.TryGetProperty("data", out var value) ? value.Clone() : JsonSerializer.SerializeToElement(new { });
                    pending.TrySetResult(data);
                }
                else
                {
                    var code = root.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var codeValue)
                        ? codeValue.GetString()
                        : "worker_error";
                    pending.TrySetException(new WorkerCommandException(code ?? "worker_error", ErrorCatalog.MessageFor(code)));
                }
                return;
            }

            if (type == "event" && root.TryGetProperty("event", out var eventName))
            {
                var data = root.TryGetProperty("data", out var value) ? value.Clone() : JsonSerializer.SerializeToElement(new { });
                EventReceived?.Invoke(this, new WorkerEvent(eventName.GetString() ?? "unknown", data));
            }
        }
        catch (JsonException)
        {
            EventReceived?.Invoke(this, new WorkerEvent("protocol_error", JsonSerializer.SerializeToElement(new { code = "malformed_worker_event" })));
        }
    }

    private void OnExited()
    {
        FailPending(new InvalidOperationException(ErrorCatalog.MessageFor("worker_crashed")));
        WorkerExited?.Invoke(this, EventArgs.Empty);
    }

    private void FailPending(Exception error)
    {
        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }
        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    await SendCommandAsync("shutdown", timeout: TimeSpan.FromSeconds(4));
                }
                catch
                {
                }
            }
        }
        finally
        {
            _lifetime.Cancel();
            try
            {
                _process.StandardInput.Close();
            }
            catch
            {
            }
            if (!_process.HasExited)
            {
                if (!await Task.Run(() => _process.WaitForExit(2000)))
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
            if (_stdoutTask is not null)
            {
                await IgnoreCancellation(_stdoutTask);
            }
            if (_stderrTask is not null)
            {
                await IgnoreCancellation(_stderrTask);
            }
            _process.Dispose();
            _writeLock.Dispose();
            _lifetime.Dispose();
        }
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}

public sealed class WorkerCommandException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
