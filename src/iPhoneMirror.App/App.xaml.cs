using System.Text.Json;
using System.Windows;
using iPhoneMirror.Core;

namespace iPhoneMirror.App;

public partial class App : Application
{
    private SingleInstanceCoordinator? _instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CommandLineOptions options;
        try
        {
            options = CommandLineOptions.Parse(e.Args);
        }
        catch (ArgumentException)
        {
            Shutdown(2);
            return;
        }

        _instance = new SingleInstanceCoordinator();
        if (!_instance.IsPrimary)
        {
            await HandleSecondaryAsync(options);
            return;
        }

        if (options.Command is "status" or "stop")
        {
            Shutdown(0);
            return;
        }

        var mainWindow = new MainWindow(options);
        MainWindow = mainWindow;
        _instance.StartServer(mainWindow.HandleInstanceCommandAsync);
        mainWindow.Show();
    }

    private async Task HandleSecondaryAsync(CommandLineOptions options)
    {
        try
        {
            var request = JsonSerializer.Serialize(new
            {
                command = options.Command,
                connection = options.Connection?.ToString().ToLowerInvariant(),
                serial = options.Serial,
            });
            var response = await SingleInstanceCoordinator.SendAsync(request);
            _ = response;
        }
        catch
        {
        }
        finally
        {
            Shutdown(0);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_instance is not null)
        {
            await _instance.DisposeAsync();
        }
        base.OnExit(e);
    }
}
