using System.Text.Json;
using NAudio.CoreAudioApi;

namespace GhostMic;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--tray-test")) { MainWindow.RunTrayLifecycleTest(); return; }
        if (args.Contains("--quit")) { InstanceControl.Send(InstanceControl.QuitMessage); return; }
        if (args.Contains("--app-status")) { InstanceControl.Send(InstanceControl.StatusMessage); return; }
        if (args.Contains("--configure-desktop") || args.Contains("--desktop-status") || args.Contains("--disable-startup"))
        {
            try
            {
                if (args.Contains("--configure-desktop")) DesktopConfiguration.Configure();
                if (args.Contains("--disable-startup")) StartupRegistration.SetEnabled(false);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "desktop-integration-result.json"), DesktopConfiguration.Status());
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "desktop-integration-result.json"), error.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--monitor-test"))
        {
            try { SelfTest.MonitorTest(); }
            catch (Exception e) { File.WriteAllText("monitor-test-result.txt", e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--capture-test"))
        {
            try { SelfTest.CaptureTest(); }
            catch (Exception e) { File.WriteAllText("capture-test-result.txt", e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--self-test"))
        {
            try { SelfTest.Run(); Environment.ExitCode = 0; }
            catch (Exception e) { File.WriteAllText("self-test-result.txt", e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--devices"))
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = new List<object>();
            foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
                foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                    using (device) devices.Add(new { Flow = flow.ToString(), device.FriendlyName, device.ID });
            File.WriteAllText("devices.json", JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        if (args.Contains("--smoke-test"))
        {
            void Fail(Exception error)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-test-result.txt"), "FAIL: " + error);
                Environment.ExitCode = 1;
            }
            Application.ThreadException += (_, e) => { Fail(e.Exception); Application.Exit(); };
            try { Application.Run(new MainWindow(true)); }
            catch (Exception error) { Fail(error); }
            return;
        }
        using var mutex = new Mutex(true, "Local\\GhostMic.Desktop", out var first);
        if (!first) { if (!args.Contains("--startup")) InstanceControl.Send(InstanceControl.ShowMessage); return; }
        Application.Run(new MainWindow(false, args.Contains("--startup"), args.Contains("--tray")));
    }
}
