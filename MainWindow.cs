using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace GhostMic;

public sealed partial class MainWindow : Form
{
    private readonly AudioEngine engine = new();
    private readonly DeviceSelector input = new(), output = new();
    private readonly GainSlider gain = new();
    private readonly NumericUpDown gainNumber = new() { Minimum = AudioProcessor.MinimumGainDb, Maximum = AudioProcessor.MaximumGainDb, Value = 6, DecimalPlaces = 0, Width = 82 };
    private readonly DecibelStepper gateNumber = new() { Minimum = -80, Maximum = -20, Value = -50, Width = 104 };
    private readonly GhostToggle highPass = new() { Text = "Remove low rumble (80 Hz)", Checked = true, AutoSize = true };
    private readonly GhostToggle gate = new() { Text = "Noise gate", AutoSize = true };
    private readonly GhostToggle mute = new() { Text = "Mute microphone", AutoSize = true };
    private readonly GhostButton start = new() { Text = "Start virtual mic", AutoSize = true };
    private readonly GhostButton stop = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly GhostButton record = new() { Text = "Record 8s sample", AutoSize = true };
    private readonly GhostButton play = new() { Text = "Play last sample", AutoSize = true, Enabled = false };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(760, 0) };
    private readonly Label details = new() { AutoSize = true, ForeColor = Color.Silver };
    private readonly Label reduction = new() { AutoSize = true };
    private readonly LevelMeter inputMeter = new(), outputMeter = new();
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    private readonly string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
    private DateTime? recordingEnds;
    private bool testing;
    private bool quitting, loading = true;
    private string? recordingPath;
    private readonly bool smokeTest;
    private AppSettings saved = new();
    private readonly bool startupLaunch, forceTray;
    private bool suppressInitialDisplay, startupPending;
    private string? startupInputId, startupOutputId;
    private DateTime nextStartupAttempt;
    private int startupAttempts;

    public MainWindow(bool smokeTest, bool startupLaunch = false, bool forceTray = false)
    {
        this.smokeTest = smokeTest;
        this.startupLaunch = startupLaunch; this.forceTray = forceTray;
        BuildInterface();
        gain.ValueChanged += (_, _) => { gainNumber.Value = gain.Value; ApplySettings(); };
        gainNumber.ValueChanged += (_, _) => { gain.Value = (int)gainNumber.Value; ApplySettings(); };
        foreach (var c in new[] { highPass, gate, mute }) c.CheckedChanged += (_, _) => ApplySettings();
        gateNumber.ValueChanged += (_, _) => ApplySettings();
        input.SelectedIndexChanged += (_, _) => { if (!loading) Save(); };
        output.SelectedIndexChanged += (_, _) => { if (!loading) Save(); };
        start.Click += (_, _) => Start(false);
        stop.Click += (_, _) => Stop();
        record.Click += (_, _) => ShowMicrophoneTest();
        play.Click += (_, _) => { if (recordingPath != null) Open(recordingPath); };
        var menu = new GhostMenu();
        menu.Items.Add("Open Ghost Mic", null, (_, _) => RestoreWindow());
        var startItem = menu.Items.Add("Start microphone", null, (_, _) => Start(false));
        menu.Items.Add("Mute / unmute", null, (_, _) => mute.Checked = !mute.Checked);
        var stopItem = menu.Items.Add("Stop microphone", null, (_, _) => Stop());
        menu.Items.Add("Settings", null, (_, _) => { RestoreWindow(); ShowDeviceMenu(input); });
        menu.Items.Add("Use as Windows default microphone", null, (_, _) => MakeDefaultMicrophone());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => BeginInvoke(() => { quitting = true; Close(); }));
        menu.Opening += (_, _) => { startItem.Enabled = !engine.Running && start.Enabled; stopItem.Enabled = engine.Running; };
        tray = new NotifyIcon { Icon = Icon, Text = "Ghost Mic · stopped", Visible = !smokeTest, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => RestoreWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (_, e) =>
        {
            if (!quitting && (engine.Running || saved.CloseToTray) && !smokeTest)
            { e.Cancel = true; Hide(); tray.ShowBalloonTip(2000, "Ghost Mic is in the tray", "Open from the tray, or use Quit to exit.", ToolTipIcon.Info); return; }
            timer.Stop(); engine.Dispose(); tray.Dispose(); Save(); timer.Dispose(); hints.Dispose();
        };
        engine.Faulted += message =>
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(() => { Stop(); SetStatus("Audio stopped: " + message + " Check microphone permissions in Windows Settings if access was denied.", true); });
        };
        timer.Tick += (_, _) => TickMeters();
        LoadSettings();
        startupInputId = saved.InputId; startupOutputId = saved.OutputId;
        suppressInitialDisplay = !smokeTest && (forceTray || startupLaunch && saved.StartMinimized);
        startupPending = !smokeTest && startupLaunch && saved.AutoStartMic;
        RefreshDevices(); loading = false; ApplySettings(); timer.Start();
        Shown += (_, _) =>
        {
            if (!smokeTest) return;
            // Render the real form without opening a microphone.
            var smokeTimer = new System.Windows.Forms.Timer { Interval = 800 };
            smokeTimer.Tick += (_, _) =>
            {
                smokeTimer.Stop();
                VerifyMenuInteraction();
                int originalGain = gain.Value;
                gain.Value = 17;
                if (gainNumber.Value != 17 || gainReadout.Text != "+17 dB") throw new InvalidOperationException("Gain slider did not update processing controls.");
                gain.Value = GainSlider.Maximum;
                if (gainNumber.Value != 96 || engine.Settings.GainDb != 96 || gainReadout.Text != "+96 dB") throw new InvalidOperationException("Extended gain range is not connected to processing controls.");
                presets[0].Button.PerformClick();
                if (engine.Settings.GainDb != 0) throw new InvalidOperationException("Preset did not update the audio engine.");
                gain.Value = originalGain;
                bool originalGate = gate.Checked, originalMute = mute.Checked;
                gate.Checked = true; mute.Checked = true;
                if (!gateNumber.Enabled || !engine.Settings.Gate || !engine.Settings.Muted) throw new InvalidOperationException("Audio toggles are not connected.");
                decimal oldThreshold = gateNumber.Value;
                gateNumber.Controls.OfType<GhostButton>().First(button => button.Text == "+").PerformClick();
                if (gateNumber.Value != Math.Min(-20, oldThreshold+1) || engine.Settings.GateDb != (float)gateNumber.Value) throw new InvalidOperationException("Gate stepper did not update audio processing.");
                gateNumber.Value = oldThreshold;
                gate.Checked = originalGate; mute.Checked = originalMute;
                using (var testDialog = CreateMicrophoneTestWindow())
                {
                    testDialog.Show(this);
                    using var testPreview = new Bitmap(testDialog.Width, testDialog.Height);
                    testDialog.DrawToBitmap(testPreview, new Rectangle(Point.Empty, testDialog.Size));
                    testPreview.Save(Path.Combine(AppContext.BaseDirectory, "smoke-test-mic.png"));
                    if (engine.Running) throw new InvalidOperationException("Opening the test panel started capture without a click.");
                    testDialog.Close();
                }
                ActiveControl = input;
                using var bitmap = new Bitmap(Width, Height);
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "smoke-test.png"));
                ShowDeviceMenu(Controls.Find("SettingsButton", true)[0]);
                using (var menuPreview = new Bitmap(settingsMenu.Width, settingsMenu.Height))
                {
                    settingsMenu.DrawToBitmap(menuPreview, new Rectangle(Point.Empty, settingsMenu.Size));
                    menuPreview.Save(Path.Combine(AppContext.BaseDirectory, "smoke-test-menu.png"));
                }
                settingsMenu.Close();
                if (input.Items.Count > 0 && string.IsNullOrWhiteSpace(input.Text))
                    throw new InvalidOperationException("Microphone selection is blank.");
                using (var compact = new Bitmap(MinimumSize.Width, MinimumSize.Height))
                {
                    Size = MinimumSize;
                    DrawToBitmap(compact, new Rectangle(Point.Empty, MinimumSize));
                    compact.Save(Path.Combine(AppContext.BaseDirectory, "smoke-test-compact.png"));
                }
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-test-result.txt"), $"PASS: both menus survived 6 mouse selections and 12 dismissals each; default and minimum window rendered; gain slider, preset, gate and mute controls verified; {input.Items.Count} physical inputs, selected: {input.Text}; {output.Items.Count} virtual outputs; no capture started.");
                quitting = true; Close(); smokeTimer.Dispose();
            };
            smokeTimer.Start();
        };
    }

    private void RefreshDevices()
    {
        if (engine.Running) Stop();
        string? oldInput = (input.SelectedItem as AudioDevice)?.Id ?? saved.InputId;
        string? oldOutput = (output.SelectedItem as AudioDevice)?.Id ?? saved.OutputId;
        var wasLoading = loading; loading = true;
        input.Items.Clear(); output.Items.Clear();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (oldInput == null)
            {
                try { using var defaultMic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); oldInput = defaultMic.ID; }
                catch { /* Fall back to the first available physical microphone. */ }
            }
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                using (device) if (!AudioDevice.IsCable(device.FriendlyName)) input.Items.Add(new AudioDevice(device.ID, device.FriendlyName));
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (device) if (AudioDevice.IsCable(device.FriendlyName)) output.Items.Add(new AudioDevice(device.ID, device.FriendlyName));
            Select(input, oldInput); Select(output, oldOutput);
            start.Enabled = input.Items.Count > 0 && output.Items.Count > 0;
            record.Enabled = input.Items.Count > 0;
            SetStatus(input.Items.Count == 0 ? "Connect a physical microphone, then refresh devices." : output.Items.Count == 0 ? "Install VB-CABLE once, then reboot. You can test your mic first." : "Start your mic, then choose CABLE Output in your recording apps.", output.Items.Count == 0 || input.Items.Count == 0);
        }
        catch (Exception e) { SetStatus("Unable to list devices: " + e.Message, true); }
        finally { loading = wasLoading; }
    }
    private static void Select(DeviceSelector box, string? id)
    { if (box.Items.Count == 0) return; box.SelectedIndex = Math.Max(0, box.Items.Cast<AudioDevice>().ToList().FindIndex(x => x.Id == id)); }
    private void ApplySettings()
    {
        engine.Settings = new ProcessingSettings((float)gainNumber.Value, mute.Checked, highPass.Checked, gate.Checked, (float)gateNumber.Value);
        gateNumber.Enabled = gate.Checked;
        UpdateGainPresentation();
        UpdateSessionPresentation();
        Save();
    }
    private void Start(bool sample, string? monitorId = null, bool testOnly = false)
    {
        if (input.SelectedItem is not AudioDevice mic) return;
        if (!sample && !testOnly && output.SelectedItem is not AudioDevice) return;
        try
        {
            Directory.CreateDirectory(dataDirectory);
            if (sample)
            {
                recordingPath = Path.Combine(dataDirectory, "sample-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".wav");
                engine.Start(mic.Id, null, recordingPath, monitorId); recordingEnds = DateTime.UtcNow.AddSeconds(8);
            }
            else engine.Start(mic.Id, testOnly ? null : ((AudioDevice)output.SelectedItem!).Id, monitorOutputId: monitorId);
            testing = sample || testOnly;
            input.Enabled = output.Enabled = start.Enabled = record.Enabled = false; stop.Enabled = true; play.Enabled = false;
            details.Text = engine.FormatDescription;
            SetStatus(testing ? "Testing your microphone locally. Nothing is sent to your apps." : "Choose CABLE Output in Discord, Snipping Tool, or your recording app.");
            tray.Text = testing ? "Ghost Mic · testing" : "Ghost Mic · live";
        }
        catch (Exception e) { Stop(); SetStatus("Could not start: " + e.Message, true); }
    }
    private void Stop()
    {
        bool wasRecording = recordingEnds != null;
        startupPending = false;
        recordingEnds = null; testing = false; engine.Stop();
        input.Enabled = output.Enabled = true;
        start.Enabled = input.Items.Count > 0 && output.Items.Count > 0;
        record.Enabled = input.Items.Count > 0; stop.Enabled = false;
        play.Enabled = recordingPath != null && File.Exists(recordingPath);
        tray.Text = "Ghost Mic · stopped";
        SetStatus(wasRecording ? "Test saved locally. Select Play recording to listen." : "Microphone stopped. The virtual cable is silent.");
    }
    private void TickMeters()
    {
        TryStartupMicrophone();
        inputMeter.Peak = engine.InputPeak; outputMeter.Peak = engine.OutputPeak;
        reduction.Text = engine.InputClipped ? "Input clipping" : $"Limiter  {engine.ReductionDb:0.0} dB";
        reduction.ForeColor = engine.InputClipped ? Theme.Warning : Theme.Muted;
        UpdateSessionPresentation();
        if (recordingEnds is DateTime until)
        {
            if (DateTime.UtcNow >= until) Stop();
            else SetStatus($"Recording your test… {Math.Ceiling((until - DateTime.UtcNow).TotalSeconds)} seconds left. Saved only on this PC.");
        }
    }
    private void SetStatus(string text, bool warning = false)
    {
        status.Text = text;
        status.ForeColor = warning ? Theme.Warning : Theme.Muted;
        UpdateSessionPresentation();
    }
    private void RestoreWindow() { suppressInitialDisplay = false; Show(); WindowState = FormWindowState.Normal; Activate(); }
    protected override void SetVisibleCore(bool value)
    {
        if (value && suppressInitialDisplay) { if (!IsHandleCreated) CreateHandle(); base.SetVisibleCore(false); return; }
        base.SetVisibleCore(value);
    }
    private void TryStartupMicrophone()
    {
        if (!startupPending || DateTime.UtcNow < nextStartupAttempt) return;
        nextStartupAttempt = DateTime.UtcNow.AddSeconds(2);
        RefreshDevices();
        bool inputReady = startupInputId == null || input.Items.Any(d => d.Id == startupInputId);
        bool outputReady = startupOutputId == null || output.Items.Any(d => d.Id == startupOutputId);
        if (inputReady && outputReady && input.Items.Count > 0 && output.Items.Count > 0)
        {
            if (startupInputId != null) Select(input, startupInputId);
            if (startupOutputId != null) Select(output, startupOutputId);
            startupPending = false; Start(false);
            if (!engine.Running) tray.ShowBalloonTip(4000, "Ghost Mic could not start", "Open Ghost Mic from the tray to check the microphone.", ToolTipIcon.Warning);
        }
        else if (++startupAttempts >= 30)
        {
            startupPending = false;
            SetStatus("Your saved microphone or virtual cable is unavailable. Connect it, then start from the tray.", true);
            tray.ShowBalloonTip(4000, "Microphone unavailable", "Connect your saved mic and start Ghost Mic from the tray.", ToolTipIcon.Warning);
        }
    }
    private void MakeDefaultMicrophone()
    {
        if (output.SelectedItem is not AudioDevice destination) { SetStatus("Choose a virtual cable destination first.", true); return; }
        try { var mic = WindowsAudioDefaults.UseCable(destination.Id); SetStatus($"Windows and calls now use {mic.Name}. Keep Ghost Mic running."); }
        catch (Exception error) { SetStatus("Could not set the default mic: " + error.Message, true); }
    }
    private void Open(string path)
    { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception e) { SetStatus(e.Message, true); } }
    private void SetupCable()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "Setup-Cable.ps1");
        if (!File.Exists(script)) { Open("https://vb-audio.com/Cable/"); return; }
        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe")
            { Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"", UseShellExecute = true });
            SetStatus("Setup opens the official signed driver installer. Complete installation, reboot Windows, and refresh devices here.");
        }
        catch (Exception e) { SetStatus(e.Message, true); }
    }
    private void LoadSettings()
    {
        try
        {
            var path = Path.Combine(dataDirectory, "settings.json");
            if (File.Exists(path)) saved = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
            gainNumber.Value = Math.Clamp(saved.GainDb, AudioProcessor.MinimumGainDb, AudioProcessor.MaximumGainDb); highPass.Checked = saved.HighPass;
            gate.Checked = saved.Gate; gateNumber.Value = Math.Clamp(saved.GateDb, -80, -20); mute.Checked = saved.Muted;
        }
        catch { saved = new(); }
    }
    private void Save()
    {
        if (loading || smokeTest) return;
        try
        {
            Directory.CreateDirectory(dataDirectory);
            saved = new AppSettings((input.SelectedItem as AudioDevice)?.Id, (output.SelectedItem as AudioDevice)?.Id,
                (int)gainNumber.Value, highPass.Checked, gate.Checked, (int)gateNumber.Value, mute.Checked, testPlaybackId ?? saved.MonitorId,
                saved.StartMinimized, saved.AutoStartMic, saved.CloseToTray);
            var path = Path.Combine(dataDirectory, "settings.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception e) { SetStatus("Could not save settings beside the app: " + e.Message, true); }
    }
}

public sealed record AppSettings(string? InputId = null, string? OutputId = null, int GainDb = 6,
    bool HighPass = true, bool Gate = false, int GateDb = -50, bool Muted = false, string? MonitorId = null,
    bool StartMinimized = true, bool AutoStartMic = true, bool CloseToTray = true);
