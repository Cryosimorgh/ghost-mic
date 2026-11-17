using Microsoft.Win32;
using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostMic;

internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string? Command
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(KeyPath); return key?.GetValue("GhostMic") as string; }
    }
    internal static bool Enabled => Command != null;
    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (enabled) key.SetValue("GhostMic", $"\"{Application.ExecutablePath}\" --startup", RegistryValueKind.String);
        else key.DeleteValue("GhostMic", false);
    }
}

internal static class WindowsAudioDefaults
{
    internal static readonly Role[] Roles = [Role.Console, Role.Multimedia, Role.Communications];
    internal static string BackupPath => Path.Combine(AppContext.BaseDirectory, "data", "previous-default-mic.json");
    internal static Dictionary<Role, string> ReadDefaults()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new Dictionary<Role, string>();
        foreach (Role role in Roles)
        {
            try { using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role); result[role] = device.ID; }
            catch (COMException) { }
        }
        return result;
    }
    internal static string ExpectedCaptureName(string renderName) => Regex.Replace(
        Regex.Replace(renderName, @"\bInput\b", "Output", RegexOptions.IgnoreCase), @"\bIn\b", "Out", RegexOptions.IgnoreCase);

    internal static AudioDevice FindCapture(string renderId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var render = enumerator.GetDevice(renderId);
        if (render.DataFlow != DataFlow.Render || !AudioDevice.IsCable(render.FriendlyName))
            throw new InvalidOperationException("Choose a virtual cable destination first.");
        string expected = ExpectedCaptureName(render.FriendlyName);
        var captures = new List<AudioDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            using (device) if (AudioDevice.IsCable(device.FriendlyName)) captures.Add(new AudioDevice(device.ID, device.FriendlyName));
        return captures.FirstOrDefault(d => d.Name.Equals(expected, StringComparison.OrdinalIgnoreCase))
            ?? (captures.Count == 1 ? captures[0] : throw new InvalidOperationException("Could not identify the matching cable microphone. Select it in Windows sound settings."));
    }

    internal static AudioDevice UseCable(string renderId)
    {
        var mic = FindCapture(renderId);
        var previous = ReadDefaults();
        if (!File.Exists(BackupPath) && previous.Values.Any(id => id != mic.Id))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
            File.WriteAllText(BackupPath, JsonSerializer.Serialize(previous));
        }
        SetRoles(Roles.ToDictionary(role => role, _ => mic.Id), previous);
        return mic;
    }
    internal static void Restore()
    {
        var previous = JsonSerializer.Deserialize<Dictionary<Role, string>>(File.ReadAllText(BackupPath))
            ?? throw new InvalidOperationException("There is no previous microphone saved.");
        // Validate every endpoint before making any changes.
        using var enumerator = new MMDeviceEnumerator();
        foreach (string id in previous.Values.Distinct())
        {
            using var device = enumerator.GetDevice(id);
            if (device.State != DeviceState.Active || device.DataFlow != DataFlow.Capture)
                throw new InvalidOperationException("The previous microphone is disconnected. Choose another in Windows sound settings.");
        }
        SetRoles(previous, ReadDefaults());
    }
    private static void SetRoles(Dictionary<Role, string> desired, Dictionary<Role, string> rollback)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            foreach (var pair in desired) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(pair.Value, pair.Key));
            var actual = ReadDefaults();
            if (desired.Any(pair => !actual.TryGetValue(pair.Key, out string? id) || id != pair.Value))
                throw new InvalidOperationException("Windows did not apply the requested default microphone.");
        }
        catch
        {
            foreach (var pair in rollback) policy.SetDefaultEndpoint(pair.Value, pair.Key);
            throw;
        }
        finally { Marshal.ReleaseComObject(policy); }
    }

    // IPolicyConfig is a Windows COM policy interface, not a documented public API.
    // Preserve the vtable order through SetDefaultEndpoint. No format/property calls are made.
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int defaults, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpoint, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int defaults, out long period, out long minimum);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, ref long period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
