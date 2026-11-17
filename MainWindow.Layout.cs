using System.Runtime.InteropServices;

namespace GhostMic;

public sealed partial class MainWindow
{
    private readonly Label gainReadout = new(), sessionBadge = new(), noticeTitle = new();
    private readonly GhostButton noticeAction = new();
    private readonly List<(GhostButton Button, int Value)> presets = [];
    private readonly ToolTip hints = new() { InitialDelay = 450, AutoPopDelay = 15000 };
    private readonly GhostMenu settingsMenu = new();

    private void BuildInterface()
    {
        SuspendLayout();
        Text = "Ghost Mic"; BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10); FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(980, 692); MinimumSize = new Size(940, 680);
        StartPosition = FormStartPosition.CenterScreen; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Padding = new Padding(1); DoubleBuffered = true;
        var chrome = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(18, 0, 8, 0) };
        var appName = LabelAt("", 8, Theme.Muted); appName.Dock = DockStyle.Left; appName.Width = 180;
        chrome.Controls.Add(appName);
        var close = new GhostButton { Text = "×", Quiet = true, Dock = DockStyle.Right, Width = 40, BackColor = Theme.Background, Font = new Font("Segoe UI", 16), AccessibleName = "Close window" };
        var minimize = new GhostButton { Text = "−", Quiet = true, Dock = DockStyle.Right, Width = 40, BackColor = Theme.Background, AccessibleName = "Minimize to tray" };
        close.Click += (_, _) => Close(); minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        chrome.Controls.Add(minimize); chrome.Controls.Add(close);
        void Drag(object? sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } }
        chrome.MouseDown += Drag; appName.MouseDown += Drag;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 12, 26, 22), ColumnCount = 1, RowCount = 10 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float size in new float[] { 58, 100, 12, -1, 12, 76, 12, 62, 12, 42 })
            body.RowStyles.Add(size < 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, size));
        Controls.Add(body); Controls.Add(chrome);
        void Row(Control c, int row) { c.Dock = DockStyle.Fill; c.Margin = Padding.Empty; body.Controls.Add(c, 0, row); }

        var header = new Panel();
        var name = LabelAt("Ghost Mic", 20, Theme.Text, true); name.SetBounds(0, -4, 245, 36);
        var subtitle = LabelAt("MICROPHONE CONTROL", 8, Theme.Muted); subtitle.SetBounds(1, 31, 320, 20);
        sessionBadge.TextAlign = ContentAlignment.MiddleRight; sessionBadge.Font = new Font("Segoe UI", 9); sessionBadge.BackColor = Theme.Background;
        sessionBadge.Size = new Size(108, 30); sessionBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var deviceMenu = new GhostButton { Name = "SettingsButton", Text = "Settings", Quiet = true, Width = 142, Height = 34, BackColor = Theme.Background, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        deviceMenu.Click += (_, _) => ShowDeviceMenu(deviceMenu);
        header.Controls.AddRange([name, subtitle, sessionBadge, deviceMenu]);
        header.Resize += (_, _) => { sessionBadge.Location = new Point(header.Width-108, 6); deviceMenu.Location = new Point(header.Width-266, 4); };
        Row(header, 0);

        var route = new Card();
        var sourceLabel = LabelAt("INPUT", 8, Theme.Muted, true);
        var targetLabel = LabelAt("OUTPUT TO APPS", 8, Theme.Muted, true);
        var arrow = LabelAt("→", 16, Theme.Muted); arrow.TextAlign = ContentAlignment.MiddleCenter;
        output.Placeholder = "Virtual device not installed";
        input.AccessibleName = "Physical microphone"; output.AccessibleName = "Virtual cable destination";
        route.Controls.AddRange([sourceLabel, targetLabel, input, output, arrow]);
        route.Resize += (_, _) =>
        {
            int half = (route.Width-84)/2;
            sourceLabel.SetBounds(20, 12, half, 20); input.SetBounds(20, 40, half, 40);
            arrow.SetBounds(half+22, 38, 40, 44);
            targetLabel.SetBounds(half+64, 12, half, 20); output.SetBounds(half+64, 40, half, 40);
        };
        Row(route, 1);

        var main = new TableLayoutPanel { ColumnCount = 3, RowCount = 1 };
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57)); main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12)); main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        var gainCard = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var gainCaption = LabelAt("GAIN", 8, Theme.Muted, true); gainCaption.SetBounds(22, 16, 240, 20);
        gainReadout.Font = new Font("Segoe UI Light", 44); gainReadout.ForeColor = Theme.Text; gainReadout.BackColor = Theme.Panel;
        gainReadout.SetBounds(16, 35, 340, 76);
        gain.BackColor = Theme.Panel;
        gainCard.Controls.AddRange([gainCaption, gainReadout, gain]);
        foreach (var item in new[] { ("", 0), ("", 6), ("", 12) })
        {
            int value = item.Item2;
            var button = new GhostButton { Text = $"{(value > 0 ? "+" : "")}{value} dB", Height = 30, Font = new Font("Segoe UI", 9) };
            button.Click += (_, _) => gainNumber.Value = value; presets.Add((button, value)); gainCard.Controls.Add(button);
        }
        gainCard.Resize += (_, _) =>
        {
            gain.SetBounds(15, 109, gainCard.Width-30, 54);
            int width = Math.Min(102, (gainCard.Width-60)/3);
            for (int i = 0; i < presets.Count; i++) presets[i].Button.SetBounds(22+i*(width+8), gainCard.Height-45, width, 30);
        };
        var signal = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var signalTitle = LabelAt("LEVELS", 8, Theme.Muted, true); signalTitle.SetBounds(20, 16, 100, 20);
        var raw = LabelAt("Input", 9, Theme.Muted); raw.SetBounds(20, 43, 150, 20);
        var processed = LabelAt("Post-gain", 9, Theme.Muted); processed.SetBounds(20, 110, 150, 20);
        reduction.Font = new Font("Segoe UI", 8); reduction.AutoSize = false; reduction.TextAlign = ContentAlignment.MiddleRight; reduction.BackColor = Theme.Panel;
        record.Text = "Test microphone"; play.Text = "Play recording";
        record.Font = play.Font = new Font("Segoe UI", 9);
        record.AutoSize = play.AutoSize = false;
        signal.Controls.AddRange([signalTitle, raw, processed, reduction, inputMeter, outputMeter, record, play]);
        signal.Resize += (_, _) =>
        {
            reduction.SetBounds(118, 16, signal.Width-138, 20);
            inputMeter.SetBounds(20, 65, signal.Width-40, 45); outputMeter.SetBounds(20, 132, signal.Width-40, 45);
            int width = (signal.Width-48)/2;
            record.SetBounds(20, signal.Height-45, width, 30); play.SetBounds(28+width, signal.Height-45, width, 30);
        };
        main.Controls.Add(gainCard, 0, 0); main.Controls.Add(signal, 2, 0); Row(main, 3);

        var filters = new Card();
        highPass.Text = "High-pass"; gate.Text = "Noise gate";
        highPass.AutoSize = gate.AutoSize = mute.AutoSize = false;
        var hpNote = LabelAt("80 Hz", 8, Theme.Muted);
        var gateNote = LabelAt("Threshold", 8, Theme.Muted);
        var gateUnit = LabelAt("dB", 8, Theme.Muted);
        var limiterName = LabelAt("Output limiter", 10, Theme.Text);
        var limiterNote = LabelAt("−1 dBFS ceiling", 8, Theme.Muted);
        gateNumber.BackColor = Theme.Field; gateNumber.ForeColor = Theme.Text;
        gateNumber.Font = new Font("Segoe UI", 9);
        filters.Controls.AddRange([highPass, hpNote, gate, gateNote, gateNumber, gateUnit, limiterName, limiterNote]);
        filters.Resize += (_, _) =>
        {
            int third = (filters.Width-40)/3;
            highPass.SetBounds(20, 10, third-10, 30); hpNote.SetBounds(69, 42, third-48, 20);
            gate.SetBounds(20+third, 10, third-12, 30); gateNote.SetBounds(66+third, 42, 72, 20);
            gateNumber.SetBounds(third+142, 38, 104, 30); gateUnit.SetBounds(third+252, 41, 25, 23);
            limiterName.SetBounds(30+2*third, 13, third-24, 25); limiterNote.SetBounds(30+2*third, 41, third-24, 22);
        };
        Row(filters, 5);

        var notice = new Card();
        noticeTitle.Font = new Font("Segoe UI Semibold", 10); noticeTitle.ForeColor = Theme.Text;
        noticeTitle.BackColor = status.BackColor = Theme.Panel;
        status.AutoSize = false; status.Font = new Font("Segoe UI", 9); status.MaximumSize = Size.Empty;
        noticeAction.AutoSize = false; noticeAction.Font = new Font("Segoe UI", 9);
        noticeAction.Click += (_, _) => { if (output.Items.Count == 0) SetupCable(); else ShowGuide(); };
        notice.Controls.AddRange([noticeTitle, status, noticeAction]);
        notice.Resize += (_, _) =>
        {
            noticeTitle.SetBounds(20, 10, notice.Width-218, 22); status.SetBounds(20, 34, notice.Width-218, 24);
            noticeAction.SetBounds(notice.Width-184, 16, 164, 34);
        };
        Row(notice, 7);

        var footer = new Panel();
        start.Text = "Start microphone"; start.Primary = true; start.AutoSize = stop.AutoSize = false;
        start.BackColor = stop.BackColor = Theme.Background; start.Font = new Font("Segoe UI Semibold", 10);
        start.SetBounds(0, 0, 190, 44); stop.SetBounds(200, 0, 90, 44);
        mute.Text = "Mute mic"; mute.BackColor = Theme.Background; mute.Size = new Size(140, 36); mute.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        details.Text = "Microphone stopped"; details.AutoSize = false; details.Font = new Font("Segoe UI", 8); details.ForeColor = Theme.Muted;
        footer.Controls.AddRange([start, stop, details, mute]);
        footer.Resize += (_, _) => { mute.Location = new Point(footer.Width-140, 4); details.SetBounds(314, 3, Math.Max(0, footer.Width-474), 38); };
        Row(footer, 9);
        hints.SetToolTip(gain, "Drag to adjust. Arrow keys change gain by 1 dB. Home / End select the minimum / maximum.");
        hints.SetToolTip(record, "Listen to your processed mic through headphones, adjust gain, or save an eight-second recording. No virtual device needed.");
        hints.SetToolTip(play, "Listen to the last saved test in your default audio player.");
        hints.SetToolTip(highPass, "80 Hz high-pass filter to reduce rumble and handling noise.");
        hints.SetToolTip(gate, "Reduces background sound between phrases. Adjust the threshold below your quietest speech.");
        hints.SetToolTip(input, "Choose one physical microphone. Refresh devices from Device settings after connecting a new mic.");
        hints.SetToolTip(output, "Ghost Mic sends audio here. Choose CABLE Output as the input in recording apps.");
        ResumeLayout(true);
    }

    private static Label LabelAt(string text, float size, Color color, bool bold = false) => new()
    { Text = text, Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size), ForeColor = color, BackColor = Color.Transparent, AutoSize = false };

    private void UpdateGainPresentation()
    {
        gainReadout.Text = $"{(gainNumber.Value > 0 ? "+" : "")}{gainNumber.Value:0} dB";
        foreach (var preset in presets) { preset.Button.Selected = preset.Value == (int)gainNumber.Value; preset.Button.Invalidate(); }
    }
    private void UpdateSessionPresentation()
    {
        bool active = engine.Running;
        string label = !active ? "●  Standby" : mute.Checked ? "●  Muted" : testing ? "●  Testing" : "●  Live";
        if (sessionBadge.Text != label) sessionBadge.Text = label;
        sessionBadge.ForeColor = active && !mute.Checked ? Theme.Accent : Theme.Muted;
        noticeTitle.Text = active ? testing ? "Microphone test" : mute.Checked ? "Microphone muted" : "Signal path active" : output.Items.Count == 0 ? "Virtual device required" : "Signal path ready";
        noticeAction.Text = output.Items.Count == 0 ? "Install virtual device" : "Setup guide";
        hints.SetToolTip(reduction, engine.InputClipped ? "The physical mic is clipping. Lower its Windows input level." : $"Limiter gain reduction: {engine.ReductionDb:0.0} dB. Output buffer recoveries: {engine.BufferResets}. {engine.FormatDescription}");
    }
    private void ShowDeviceMenu(Control anchor)
    {
        var menu = settingsMenu;
        if (menu.Visible) { menu.Close(); return; }
        if (menu.Items.Count == 0)
        {
            menu.BackColor = Theme.Field; menu.ForeColor = Theme.Text; menu.Font = Font;
            menu.Items.Add("Refresh devices", null, (_, _) => RefreshDevices());
            menu.Items.Add("Install virtual device…", null, (_, _) => SetupCable());
            menu.Items.Add("Windows sound settings", null, (_, _) => Open("ms-settings:sound"));
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Discord & Snipping Tool setup", null, (_, _) => ShowGuide());
            menu.Items.Add("Use as Windows default microphone", null, (_, _) => MakeDefaultMicrophone());
            menu.Items.Add("Restore previous default microphone", null, (_, _) =>
            {
                try { WindowsAudioDefaults.Restore(); SetStatus("Previous Windows microphone restored."); }
                catch (Exception error) { SetStatus(error.Message, true); }
            });
            menu.Items.Add(new ToolStripSeparator());
            var launch = new ToolStripMenuItem("Launch at Windows sign-in") { CheckOnClick = true };
            launch.Click += (_, _) => { try { StartupRegistration.SetEnabled(launch.Checked); } catch (Exception error) { launch.Checked = StartupRegistration.Enabled; SetStatus(error.Message, true); } };
            var minimized = new ToolStripMenuItem("Start in the system tray") { CheckOnClick = true };
            minimized.Click += (_, _) => { saved = saved with { StartMinimized = minimized.Checked }; Save(); };
            var autoMic = new ToolStripMenuItem("Start microphone at sign-in") { CheckOnClick = true };
            autoMic.Click += (_, _) => { saved = saved with { AutoStartMic = autoMic.Checked }; Save(); };
            var closeToTray = new ToolStripMenuItem("Close window to system tray") { CheckOnClick = true };
            closeToTray.Click += (_, _) => { saved = saved with { CloseToTray = closeToTray.Checked }; Save(); };
            menu.Items.AddRange([launch, minimized, autoMic, closeToTray]);
            menu.Opening += (_, _) =>
            {
                launch.Checked = StartupRegistration.Enabled; minimized.Checked = saved.StartMinimized;
                autoMic.Checked = saved.AutoStartMic; closeToTray.Checked = saved.CloseToTray;
            };
        }
        menu.Show(anchor, new Point(0, anchor.Height));
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) settingsMenu.Dispose();
        base.Dispose(disposing);
    }
    private void ShowGuide()
    {
        using var guide = new Form { Text = "Connect your apps", BackColor = Theme.Background, ForeColor = Theme.Text, Font = Font, ClientSize = new Size(540, 370), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var title = LabelAt("One microphone. All your apps.", 20, Theme.Text, true); title.SetBounds(24, 20, 500, 46);
        var text = LabelAt("1   Start Ghost Mic with your physical microphone and CABLE Input.\n\n2   Discord → Voice & Video → Input Device → CABLE Output. Disable automatic gain control to let Ghost Mic set the level.\n\n3   Snipping Tool → recording → microphone menu → CABLE Output. Enable the microphone for your recording.\n\nKeep Ghost Mic running. Closing the live window sends it to the tray; Stop or Quit ends capture.", 10, Theme.Muted);
        text.SetBounds(26, 82, 485, 228);
        var done = new GhostButton { Text = "Got it", Primary = true }; done.SetBounds(390, 316, 125, 36); done.Click += (_, _) => guide.Close();
        guide.Controls.AddRange([title, text, done]); guide.AcceptButton = done; guide.CancelButton = done; guide.ShowDialog(this);
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, Width-1, Height-1); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == InstanceControl.ShowMessage) { RestoreWindow(); return; }
        if (m.Msg == InstanceControl.QuitMessage) { quitting = true; Close(); return; }
        if (m.Msg == InstanceControl.StatusMessage)
        {
            try
            {
                Directory.CreateDirectory(dataDirectory);
                string path = Path.Combine(dataDirectory, "runtime-status.json");
                File.WriteAllText(path + ".tmp", System.Text.Json.JsonSerializer.Serialize(new
                {
                    CapturedAtUtc = DateTime.UtcNow, engine.Running, Testing = testing, WindowVisible = Visible, StartupPending = startupPending,
                    PhysicalMicrophone = input.SelectedItem, VirtualDestination = output.SelectedItem, GainDb = gain.Value
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                File.Move(path + ".tmp", path, true);
            }
            catch (Exception error) { SetStatus("Could not write app diagnostics: " + error.Message, true); }
            return;
        }
        base.WndProc(ref m);
        if (m.Msg != 0x84 || WindowState != FormWindowState.Normal) return;
        var point = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
        bool left = point.X < 7, right = point.X >= Width-7, top = point.Y < 7, bottom = point.Y >= Height-7;
        m.Result = (IntPtr)(top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : (int)m.Result);
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
}
