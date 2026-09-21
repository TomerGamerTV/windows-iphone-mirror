using System.Text.Json;
using System.Text.Json.Serialization;

namespace iPhoneMirror.Core;

public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter<ConnectionMode>))]
    public ConnectionMode Connection { get; set; } = ConnectionMode.Auto;
    public string? Serial { get; set; }
    public string? WifiAddress { get; set; }
    public int WifiPort { get; set; } = 49152;
    public bool PreferHardwareDecode { get; set; } = true;
    public string Backdrop { get; set; } = "Acrylic";
    public bool AlwaysOnTop { get; set; } = false;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
