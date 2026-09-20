namespace iPhoneMirror.Core;

public sealed record CommandLineOptions(string Command, ConnectionMode? Connection, string? Serial)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var command = args.Count > 0 && !args[0].StartsWith('-') ? args[0].ToLowerInvariant() : "start";
        if (command is not ("start" or "stop" or "restart" or "status"))
        {
            throw new ArgumentException("Command must be start, stop, restart, or status.");
        }

        ConnectionMode? connection = null;
        string? serial = null;
        var index = command == "start" && (args.Count == 0 || args[0].StartsWith('-')) ? 0 : 1;
        while (index < args.Count)
        {
            switch (args[index])
            {
                case "--connection" when index + 1 < args.Count:
                    connection = args[++index].ToLowerInvariant() switch
                    {
                        "auto" => ConnectionMode.Auto,
                        "usb" => ConnectionMode.Usb,
                        "wifi" => ConnectionMode.Wifi,
                        _ => throw new ArgumentException("Connection must be auto, usb, or wifi."),
                    };
                    break;
                case "--serial" when index + 1 < args.Count:
                    serial = args[++index];
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
            index++;
        }

        if ((connection is not null || serial is not null) && command is not ("start" or "restart"))
        {
            throw new ArgumentException("Connection options require start or restart.");
        }

        return new CommandLineOptions(command, connection, serial);
    }
}
