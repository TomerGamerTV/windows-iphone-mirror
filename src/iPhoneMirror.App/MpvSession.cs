using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace iPhoneMirror.App;

internal sealed class MpvSession : IAsyncDisposable
{
    private readonly string _mpvPath;
    private readonly IntPtr _hostHandle;
    private readonly bool _hardwareDecode;
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private NamedPipeServerStream? _videoPipe;
    private Task? _relayTask;

    public MpvSession(string mpvPath, IntPtr hostHandle, bool hardwareDecode)
    {
        _mpvPath = mpvPath;
        _hostHandle = hostHandle;
        _hardwareDecode = hardwareDecode;
    }

    public event EventHandler? Exited;

    public void Start(string pipeName)
    {
        _videoPipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 1024 * 1024, 1024 * 1024);
        var info = new ProcessStartInfo
        {
            FileName = _mpvPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in BuildArguments()) info.ArgumentList.Add(argument);
        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
        if (!_process.Start()) throw new InvalidOperationException("The bundled player could not be started.");
        _ = DrainAsync(_process.StandardOutput, _lifetime.Token);
        _ = DrainAsync(_process.StandardError, _lifetime.Token);
        _relayTask = RelayAsync(_lifetime.Token);
    }

    private IEnumerable<string> BuildArguments()
    {
        yield return "--no-config";
        yield return "--profile=low-latency";
        yield return $"--wid={_hostHandle.ToInt64()}";
        yield return "--osc=no";
        yield return "--input-default-bindings=no";
        yield return "--input-terminal=no";
        yield return "--no-terminal";
        yield return "--no-audio";
        yield return "--untimed";
        yield return "--cache=no";
        yield return "--demuxer-lavf-format=hevc";
        yield return "--demuxer-lavf-probesize=32";
        yield return "--demuxer-lavf-analyzeduration=0";
        yield return "--demuxer-lavf-o=fflags=+flush_packets,framerate=60";
        yield return "--vd-lavc-threads=1";
        yield return "--interpolation=no";
        yield return "--keep-open=no";
        yield return _hardwareDecode ? "--hwdec=auto-safe" : "--hwdec=no";
        yield return "-";
    }

    private async Task RelayAsync(CancellationToken cancellationToken)
    {
        if (_videoPipe is null || _process is null) return;
        try
        {
            await _videoPipe.WaitForConnectionAsync(cancellationToken);
            await _videoPipe.CopyToAsync(_process.StandardInput.BaseStream, 128 * 1024, cancellationToken);
            await _process.StandardInput.BaseStream.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && await reader.ReadLineAsync(cancellationToken) is not null) { }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _videoPipe?.Dispose();
        if (_process is not null)
        {
            try { _process.StandardInput.Close(); } catch { }
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            _process.Dispose();
            _process = null;
        }
        if (_relayTask is not null)
        {
            try { await _relayTask; } catch (OperationCanceledException) { }
        }
        _lifetime.Dispose();
    }
}
