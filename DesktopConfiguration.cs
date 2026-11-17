using System.Text.Json;
using NAudio.CoreAudioApi;

namespace GhostMic;

internal static class DesktopConfiguration
{
    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "data", "settings.json");
    private static AppSettings ReadSettings() => File.Exists(SettingsPath)
        ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new() : new();
    internal static void Configure()
    {
        var settings = ReadSettings();
        using var enumerator = new MMDeviceEnumerator();
        var render = new List<AudioDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device) if (AudioDevice.IsCable(device.FriendlyName)) render.Add(new AudioDevice(device.ID, device.FriendlyName));
        var destination = render.FirstOrDefault(d => d.Id == settings.OutputId)
            ?? render.FirstOrDefault(d => d.Name.StartsWith("CABLE Input (", StringComparison.OrdinalIgnoreCase))
            ?? render.FirstOrDefault() ?? throw new InvalidOperationException("Install the virtual cable before setting up the default mic.");
        WindowsAudioDefaults.UseCable(destination.Id);
        settings = settings with { OutputId = destination.Id, StartMinimized = true, AutoStartMic = true, CloseToTray = true };
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(SettingsPath + ".tmp", SettingsPath, true);
        StartupRegistration.SetEnabled(true);
    }
    internal static string Status()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaults = WindowsAudioDefaults.ReadDefaults().Select(pair =>
        {
            using var device = enumerator.GetDevice(pair.Value);
            return new { Role = pair.Key.ToString(), Device = device.FriendlyName, device.ID };
        }).ToArray();
        return JsonSerializer.Serialize(new { StartupCommand = StartupRegistration.Command, Settings = ReadSettings(), DefaultMicrophones = defaults },
            new JsonSerializerOptions { WriteIndented = true });
    }
}
