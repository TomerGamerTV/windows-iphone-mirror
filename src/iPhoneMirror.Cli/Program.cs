using System.Diagnostics;
using System.Text.Json;
using iPhoneMirror.Core;

namespace iPhoneMirror.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CommandLineOptions options;
        try
        {
            options = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine($"iphone-mirror: {error.Message}");
            return 2;
        }

        try
        {
            return options.Command switch
            {
                "status" => await StatusAsync(),
                "stop" => await StopAsync(),
                "restart" => await StartOrRestartAsync(options, restart: true),
                _ => await StartOrRestartAsync(options, restart: false),
            };
        }
        catch (Exception)
        {
            Console.Error.WriteLine("iphone-mirror: command could not be completed");
            return 1;
        }
    }

    private static async Task<int> StatusAsync()
    {
        var response = await TrySendAsync(JsonSerializer.Serialize(new { command = "status" }));
        Console.WriteLine(response ?? "{\"running\":false,\"state\":\"stopped\"}");
        return 0;
    }

    private static async Task<int> StopAsync()
    {
        var response = await TrySendAsync(JsonSerializer.Serialize(new { command = "stop" }));
        if (response is null)
        {
            return 0;
        }
        return ResponseSucceeded(response) ? 0 : 1;
    }

    private static async Task<int> StartOrRestartAsync(CommandLineOptions options, bool restart)
    {
        var command = restart ? "restart" : "start";
        var request = JsonSerializer.Serialize(new
        {
            command,
            connection = options.Connection?.ToString().ToLowerInvariant(),
            serial = options.Serial,
        });
        var response = await TrySendAsync(request);
        if (response is not null)
        {
            if (ResponseSucceeded(response))
            {
                return 0;
            }
            Console.Error.WriteLine("iphone-mirror: the running session could not accept that command");
            return 1;
        }

        var gui = Path.Combine(AppContext.BaseDirectory, "iPhoneMirror.exe");
        if (!File.Exists(gui))
        {
            Console.Error.WriteLine("iphone-mirror: iPhoneMirror.exe is missing");
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = gui,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(restart ? "restart" : "start");
        if (options.Connection is not null)
        {
            startInfo.ArgumentList.Add("--connection");
            startInfo.ArgumentList.Add(options.Connection.Value.ToString().ToLowerInvariant());
        }
        if (!string.IsNullOrWhiteSpace(options.Serial))
        {
            startInfo.ArgumentList.Add("--serial");
            startInfo.ArgumentList.Add(options.Serial);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine("iphone-mirror: could not launch iPhone Mirror");
            return 1;
        }

        // Prove the GUI established its control channel. Device connection may
        // continue asynchronously and is reflected by the later status result.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await Task.Delay(100);
            var status = await TrySendAsync(JsonSerializer.Serialize(new { command = "status" }), TimeSpan.FromMilliseconds(250));
            if (status is not null)
            {
                return 0;
            }
            if (process.HasExited)
            {
                return process.ExitCode == 0 ? 1 : process.ExitCode;
            }
        }

        Console.Error.WriteLine("iphone-mirror: the application did not become ready");
        return 1;
    }

    private static async Task<string?> TrySendAsync(string command, TimeSpan? timeout = null)
    {
        try
        {
            return await SingleInstanceCoordinator.SendAsync(command, timeout ?? TimeSpan.FromMilliseconds(700));
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool ResponseSucceeded(string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            return !root.TryGetProperty("ok", out var ok) || ok.GetBoolean();
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
