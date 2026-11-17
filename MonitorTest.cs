using NAudio.CoreAudioApi;

namespace GhostMic;

internal static class MonitorChecks
{
    internal static List<AudioDevice> PhysicalDevices(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        var devices = new List<AudioDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            using (device) if (!AudioDevice.IsCable(device.FriendlyName)) devices.Add(new AudioDevice(device.ID, device.FriendlyName));
        return devices;
    }
}
