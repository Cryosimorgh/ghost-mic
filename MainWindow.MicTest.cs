using NAudio.CoreAudioApi;

namespace GhostMic;

public sealed partial class MainWindow
{
    private string? testPlaybackId;

    private void ShowMicrophoneTest()
    {
        if (engine.Running) return;
        using var dialog = CreateMicrophoneTestWindow();
        dialog.ShowDialog(this);
    }

    private Form CreateMicrophoneTestWindow()
    {
        var dialog = new Form
        {
            Text = "Test your microphone", ClientSize = new Size(600, 554), BackColor = Theme.Background,
            ForeColor = Theme.Text, Font = Font, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false
        };
        var title = LabelAt("Microphone test", 20, Theme.Text, true); title.SetBounds(24, 18, 500, 42);
        title.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(dialog.Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
        var subtitle = LabelAt("Monitor the processed signal through your headphones.", 10, Theme.Muted); subtitle.SetBounds(26, 64, 540, 24);
        var close = new GhostButton { Text = "×", Quiet = true, BackColor = Theme.Background, Font = new Font("Segoe UI", 16) }; close.SetBounds(548, 16, 32, 32); close.Click += (_, _) => dialog.Close();
        var playbackLabel = LabelAt("PLAYBACK DEVICE", 8, Theme.Muted, true); playbackLabel.SetBounds(26, 104, 540, 20);
        var speaker = new DeviceSelector { BackColor = Theme.Background, AccessibleName = "Mic test playback device", Placeholder = "No headphones or speakers connected" }; speaker.SetBounds(24, 132, 552, 44);
        var testGainLabel = LabelAt("MICROPHONE GAIN", 8, Theme.Muted, true); testGainLabel.SetBounds(26, 196, 360, 20);
        var testGainValue = LabelAt(gainReadout.Text, 14, Theme.Accent, true); testGainValue.TextAlign = ContentAlignment.MiddleRight; testGainValue.SetBounds(440, 190, 136, 32);
        var testGain = new GainSlider { Value = gain.Value, BackColor = Theme.Background }; testGain.SetBounds(24, 226, 552, 54);
        testGain.ValueChanged += (_, _) => { gain.Value = testGain.Value; testGainValue.Text = gainReadout.Text; };
        var meterLabel = LabelAt("PROCESSED SIGNAL", 8, Theme.Muted, true); meterLabel.SetBounds(26, 296, 540, 20);
        var meter = new LevelMeter { BackColor = Theme.Background }; meter.SetBounds(26, 322, 548, 48);
        var hear = new GhostToggle { Text = "Hear myself", Checked = true, BackColor = Theme.Background }; hear.SetBounds(26, 388, 240, 34);
        var testMute = new GhostToggle { Text = "Mute mic", Checked = mute.Checked, BackColor = Theme.Background }; testMute.SetBounds(360, 388, 200, 34);
        testMute.CheckedChanged += (_, _) => mute.Checked = testMute.Checked;
        hear.CheckedChanged += (_, _) => engine.MonitorEnabled = hear.Checked;
        var note = LabelAt("Use headphones to avoid feedback. Some playback delay is normal.", 9, Theme.Warning); note.SetBounds(26, 430, 548, 22);
        var message = LabelAt("Ready. Testing stays local and does not feed your recording apps.", 9, Theme.Muted); message.SetBounds(26, 460, 548, 26);
        var begin = new GhostButton { Text = "Start test", Primary = true, BackColor = Theme.Background }; begin.SetBounds(24, 502, 170, 36);
        var sample = new GhostButton { Text = "Record 8s", BackColor = Theme.Background }; sample.SetBounds(206, 502, 170, 36);
        var end = new GhostButton { Text = "Stop test", Enabled = false, BackColor = Theme.Background }; end.SetBounds(388, 502, 188, 36);
        dialog.Controls.AddRange([title, subtitle, close, playbackLabel, speaker, testGainLabel, testGainValue, testGain, meterLabel, meter, hear, testMute, note, message, begin, sample, end]);
        dialog.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, dialog.Width-1, dialog.Height-1); };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (device) if (!AudioDevice.IsCable(device.FriendlyName)) speaker.Items.Add(new AudioDevice(device.ID, device.FriendlyName));
            string? preferred = testPlaybackId ?? saved.MonitorId;
            if (preferred == null)
            {
                try { using var normalOutput = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); preferred = normalOutput.ID; }
                catch { }
            }
            Select(speaker, preferred);
            if (speaker.Items.Count == 0) { hear.Checked = false; hear.Enabled = false; }
        }
        catch (Exception error) { message.Text = "Could not list playback devices: " + error.Message; hear.Checked = false; hear.Enabled = false; }
        speaker.SelectedIndexChanged += (_, _) => { testPlaybackId = speaker.SelectedItem?.Id; Save(); };
        void BeginTest(bool saveRecording)
        {
            engine.MonitorEnabled = hear.Checked;
            Start(saveRecording, speaker.SelectedItem?.Id, testOnly: true);
            if (!engine.Running) message.Text = status.Text;
        }
        begin.Click += (_, _) => BeginTest(false); sample.Click += (_, _) => BeginTest(true);
        end.Click += (_, _) => { Stop(); message.Text = "Test stopped. You can adjust settings and try again."; };
        bool wasActive = false;
        var uiTimer = new System.Windows.Forms.Timer { Interval = 50 };
        uiTimer.Tick += (_, _) =>
        {
            bool active = testing && engine.Running;
            meter.Peak = engine.OutputPeak;
            speaker.Enabled = !active; begin.Enabled = sample.Enabled = !active; end.Enabled = active;
            if (active)
                message.Text = mute.Checked ? "Microphone is muted. Switch off Mute mic to hear your voice." : recordingEnds is DateTime ends ?
                    $"Recording… {Math.Max(0, Math.Ceiling((ends - DateTime.UtcNow).TotalSeconds))}s left. Playback {(hear.Checked ? "on" : "off")}." :
                    hear.Checked ? "Listening live. Change the gain while you speak." : "Test running. Switch on Hear myself for live playback.";
            else if (wasActive) message.Text = status.Text;
            wasActive = active;
        };
        dialog.FormClosing += (_, _) => { uiTimer.Stop(); uiTimer.Dispose(); if (testing) Stop(); engine.MonitorEnabled = false; };
        dialog.Shown += (_, _) => { uiTimer.Start(); dialog.ActiveControl = begin; };
        dialog.KeyPreview = true; dialog.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) dialog.Close(); };
        return dialog;
    }
}
