using iPhoneMirror.Core;
using System.Diagnostics;
using System.Text;

namespace iPhoneMirror.Core.Tests;

public sealed class CoreBehaviorTests
{
    [Fact]
    public void ViewportMapper_RejectsLetterboxAndMapsCorners()
    {
        var viewport = ViewportMapper.CalculateViewport(1000, 1000, 390, 844);
        Assert.True(viewport.Left > 250);
        Assert.Equal(0, viewport.Top, 6);

        Assert.Null(ViewportMapper.MapToPhone(10, 500, 1000, 1000, 390, 844));
        Assert.Equal(new PhonePoint(0, 0), ViewportMapper.MapToPhone(viewport.Left, viewport.Top, 1000, 1000, 390, 844));

        var bottomRight = ViewportMapper.MapToPhone(
            viewport.Left + viewport.Width - 1,
            viewport.Top + viewport.Height - 1,
            1000,
            1000,
            390,
            844);
        Assert.NotNull(bottomRight);
        Assert.Equal(ushort.MaxValue, bottomRight.Value.X);
        Assert.Equal(ushort.MaxValue, bottomRight.Value.Y);
    }

    [Fact]
    public void ViewportMapper_DpiScalingPreservesDipCoordinates()
    {
        var normal = ViewportMapper.MapToPhone(500, 500, 1000, 1000, 390, 844, 1.0);
        var scaled = ViewportMapper.MapToPhone(500, 500, 1000, 1000, 390, 844, 1.5);
        Assert.Equal(normal, scaled);
    }

    [Theory]
    [InlineData(new[] { "start" }, "start", null, null)]
    [InlineData(new[] { "start", "--connection", "usb", "--serial", "abc" }, "start", ConnectionMode.Usb, "abc")]
    [InlineData(new[] { "restart", "--connection", "wifi" }, "restart", ConnectionMode.Wifi, null)]
    [InlineData(new[] { "status" }, "status", null, null)]
    public void CommandLine_ParsesSupportedForms(string[] args, string command, ConnectionMode? mode, string? serial)
    {
        var parsed = CommandLineOptions.Parse(args);
        Assert.Equal(command, parsed.Command);
        Assert.Equal(mode, parsed.Connection);
        Assert.Equal(serial, parsed.Serial);
    }

    [Fact]
    public void CommandLine_RejectsConnectionOptionsForStatus()
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["status", "--connection", "usb"]));
    }

    [Fact]
    public void Settings_RoundTripAndMalformedFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "iphone-mirror-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.json");
        try
        {
            var expected = new AppSettings { Connection = ConnectionMode.Wifi, Serial = "device-1", PreferHardwareDecode = false };
            SettingsStore.Save(path, expected);
            var loaded = SettingsStore.Load(path);
            Assert.Equal(expected.Connection, loaded.Connection);
            Assert.Equal(expected.Serial, loaded.Serial);
            Assert.Equal(expected.PreferHardwareDecode, loaded.PreferHardwareDecode);

            File.WriteAllText(path, "{broken-json");
            var fallback = SettingsStore.Load(path);
            Assert.Equal(ConnectionMode.Auto, fallback.Connection);
            Assert.True(fallback.PreferHardwareDecode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RuntimePaths_UseWindowsApplicationFolders()
    {
        var paths = new RuntimePaths(@"C:\Local", @"C:\Roaming");
        Assert.Equal(@"C:\Local\iPhoneMirror", paths.LocalRoot);
        Assert.Equal(@"C:\Roaming\iPhoneMirror", paths.SettingsRoot);
        Assert.EndsWith(@"iPhoneMirror\state.json", paths.StateFile);
    }

    [Fact]
    public void ErrorCatalog_NeverEchoesUnknownCode()
    {
        const string secretLikeCode = "secret-pairing-payload-123";
        var message = ErrorCatalog.MessageFor(secretLikeCode);
        Assert.DoesNotContain(secretLikeCode, message, StringComparison.Ordinal);
        Assert.Contains("could not be completed", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false, false, "USB · Trust required")]
    [InlineData(true, false, false, "USB · Developer Mode required")]
    [InlineData(true, true, false, "USB · Trusted")]
    [InlineData(true, true, true, "USB · Wi-Fi paired")]
    public void DeviceInfo_DisplayDetails_ExplainsSetupState(
        bool trusted,
        bool developerMode,
        bool wifiPaired,
        string expected)
    {
        var device = new DeviceInfo("device", "Test iPhone", "usb", trusted, developerMode, wifiPaired);
        Assert.Equal(expected, device.DisplayDetails);
    }

    [Fact]
    public void DeviceInfo_DisplayDetails_PrefersActionableError()
    {
        var device = new DeviceInfo("device", null, "usb", false, false, false, "iphone_locked");
        Assert.Equal(ErrorCatalog.MessageFor("iphone_locked"), device.DisplayDetails);
    }

    [Fact]
    public async Task SingleInstance_NamedPipeRoundTripWorks()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var primary = new SingleInstanceCoordinator($"Local\\iPhoneMirror.Tests.{suffix}", $"iPhoneMirror.Tests.{suffix}");
        Assert.True(primary.IsPrimary);
        primary.StartServer(command => Task.FromResult(command == "ping" ? "pong" : "bad"));

        var response = await SingleInstanceCoordinator.SendAsync(
            "ping",
            timeout: TimeSpan.FromSeconds(3),
            pipeName: $"iPhoneMirror.Tests.{suffix}");

        Assert.Equal("pong", response);
    }

    [Fact]
    public async Task SingleInstance_SecondCoordinatorIsNotPrimary()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $"Local\\iPhoneMirror.Tests.{suffix}";
        await using var primary = new SingleInstanceCoordinator(mutexName, $"iPhoneMirror.Tests.{suffix}.primary");
        await using var secondary = new SingleInstanceCoordinator(mutexName, $"iPhoneMirror.Tests.{suffix}.secondary");

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        Assert.Throws<InvalidOperationException>(() => secondary.StartServer(_ => Task.FromResult("bad")));
    }

    [Theory]
    [InlineData("bluetooth")]
    [InlineData("ethernet")]
    [InlineData("")]
    public void CommandLine_RejectsInvalidConnectionMode(string mode)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["start", "--connection", mode]));
    }

    [Fact]
    public async Task FakeWorkerExitRaisesEventAndFailsPendingRequest()
    {
        var python = FindBundledPython();
        if (python is null)
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "iphone-mirror-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "fake-worker.py");
        await File.WriteAllTextAsync(script, """
            import json
            import sys
            import time

            for line in sys.stdin:
                request = json.loads(line)
                command = request.get("command")
                if command == "hang":
                    time.sleep(60)
                    continue
                if command == "shutdown":
                    response = {"type": "response", "id": request["id"], "ok": True, "data": {}}
                    print(json.dumps(response), flush=True)
                    break
                response = {"type": "response", "id": request["id"], "ok": True, "data": {"pong": command == "ping"}}
                print(json.dumps(response), flush=True)
            """, Encoding.UTF8);

        try
        {
            await using var client = await WorkerProtocolClient.StartAsync(python, script);
            var response = await client.SendCommandAsync("ping", timeout: TimeSpan.FromSeconds(3));
            Assert.True(response.GetProperty("pong").GetBoolean());

            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.WorkerExited += (_, _) => exited.TrySetResult();
            var pending = client.SendCommandAsync("hang", timeout: TimeSpan.FromSeconds(30));

            using (var process = Process.GetProcessById(client.ProcessId))
            {
                process.Kill(entireProcessTree: true);
            }

            await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NoWaitCommandDoesNotBlockOnWorkerResponse()
    {
        var python = FindBundledPython();
        if (python is null)
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "iphone-mirror-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "fake-worker.py");
        await File.WriteAllTextAsync(script, """
            import json
            import sys

            for line in sys.stdin:
                request = json.loads(line)
                command = request.get("command")
                if command == "touch":
                    # Model an input command whose response is intentionally ignored.
                    continue
                if command == "shutdown":
                    print(json.dumps({"type": "response", "id": request["id"], "ok": True, "data": {}}), flush=True)
                    break
                print(json.dumps({"type": "response", "id": request["id"], "ok": True, "data": {"pong": command == "ping"}}), flush=True)
            """, Encoding.UTF8);

        try
        {
            await using var client = await WorkerProtocolClient.StartAsync(python, script);
            var started = Stopwatch.GetTimestamp();
            await client.SendCommandNoWaitAsync("touch", new { phase = "down", x = 123, y = 456 });
            var elapsed = Stopwatch.GetElapsedTime(started);

            Assert.True(elapsed < TimeSpan.FromSeconds(1), $"No-wait input took {elapsed}.");
            var response = await client.SendCommandAsync("ping", timeout: TimeSpan.FromSeconds(3));
            Assert.True(response.GetProperty("pong").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedWorkerEventIsSanitized()
    {
        var python = FindBundledPython();
        if (python is null)
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "iphone-mirror-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "fake-worker.py");
        await File.WriteAllTextAsync(script, """
            import json
            import sys

            for line in sys.stdin:
                request = json.loads(line)
                command = request.get("command")
                if command == "emit_bad_event":
                    print("{not-json}", flush=True)
                    print(json.dumps({"type": "response", "id": request["id"], "ok": True, "data": {}}), flush=True)
                elif command == "shutdown":
                    print(json.dumps({"type": "response", "id": request["id"], "ok": True, "data": {}}), flush=True)
                    break
                else:
                    print(json.dumps({"type": "response", "id": request["id"], "ok": True, "data": {}}), flush=True)
            """, Encoding.UTF8);

        try
        {
            await using var client = await WorkerProtocolClient.StartAsync(python, script);
            var received = new TaskCompletionSource<WorkerEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.EventReceived += (_, value) => received.TrySetResult(value);
            await client.SendCommandAsync("emit_bad_event", timeout: TimeSpan.FromSeconds(3));

            var workerEvent = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("protocol_error", workerEvent.Name);
            Assert.Equal("malformed_worker_event", workerEvent.Data.GetProperty("code").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string? FindBundledPython()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            foreach (var relative in new[]
            {
                Path.Combine("artifacts", "publish", "win-x64", "runtime", "python", "python.exe"),
                Path.Combine("artifacts", "runtime", "python", "python.exe"),
            })
            {
                var candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
