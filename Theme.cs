using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace GhostMic;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(18, 21, 21), Panel = Color.FromArgb(28, 32, 32),
        Field = Color.FromArgb(35, 40, 39), Border = Color.FromArgb(57, 66, 62),
        Text = Color.FromArgb(240, 237, 229), Muted = Color.FromArgb(158, 170, 161),
        Accent = Color.FromArgb(142, 202, 172), Warning = Color.FromArgb(210, 175, 123),
        Primary = Color.FromArgb(40, 85, 66), PrimaryHover = Color.FromArgb(48, 101, 78),
        AccentEdge = Color.FromArgb(85, 132, 109), Selection = Color.FromArgb(35, 57, 46),
        Highlight = Color.FromArgb(72, 80, 77);
    public static GraphicsPath Round(RectangleF rect, float radius = 10)
    {
        var path = new GraphicsPath(); float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right-d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right-d, rect.Bottom-d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom-d, d, d, 90, 90); path.CloseFigure();
        return path;
    }
    public static void Fill(Graphics g, Color color, RectangleF rect, float radius = 10)
    { using var path = Round(rect, radius); using var brush = new SolidBrush(color); g.FillPath(brush, path); }
    public static void Satin(Graphics g, Color color, RectangleF rect, float radius = 4)
    {
        using var path = Round(rect, radius);
        var top = Color.FromArgb(Math.Min(255, color.R+6), Math.Min(255, color.G+6), Math.Min(255, color.B+6));
        using var brush = new LinearGradientBrush(rect, top, color, LinearGradientMode.Vertical);
        g.FillPath(brush, path);
    }
    public static void Outline(Graphics g, Color color, RectangleF rect, float radius = 10)
    { using var path = Round(rect, radius); using var pen = new Pen(color); g.DrawPath(pen, path); }
    public static void TextAt(Graphics g, string text, Font font, Color color, Rectangle rect, TextFormatFlags extra = TextFormatFlags.Left)
        => TextRenderer.DrawText(g, text, font, rect, color, extra | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
}

public class Card : Panel
{
    public Card() { DoubleBuffered = true; BackColor = Theme.Background; Padding = new Padding(22); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Fill(e.Graphics, Theme.Panel, new RectangleF(0, 0, Width-1, Height-1), 6);
        Theme.Outline(e.Graphics, Theme.Border, new RectangleF(.5f, .5f, Width-1, Height-1), 6);
        using var highlight = new Pen(Theme.Highlight);
        e.Graphics.DrawLine(highlight, 7, 1, Width-8, 1);
    }
}

public class GhostButton : Button
{
    private bool hover;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Quiet { get; set; }
    public GhostButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; AutoSize = false; Height = 40; Cursor = Cursors.Hand;
        BackColor = Theme.Panel; ForeColor = Theme.Text; UseVisualStyleBackColor = false;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = Primary && Enabled ? hover ? Theme.PrimaryHover : Theme.Primary : Selected ? Theme.Selection : hover && Enabled ? Color.FromArgb(43, 54, 47) : Theme.Field;
        if (Quiet && !hover) fill = BackColor;
        if (Quiet && !hover) Theme.Fill(g, fill, new RectangleF(.5f, .5f, Width-1, Height-1), 4);
        else Theme.Satin(g, fill, new RectangleF(.5f, .5f, Width-1, Height-1));
        if (!Quiet) Theme.Outline(g, Selected || Primary && Enabled ? Theme.AccentEdge : Theme.Border, new RectangleF(.5f, .5f, Width-1, Height-1), 4);
        Theme.TextAt(g, Text, Font, !Enabled ? Color.FromArgb(110, 122, 113) : Selected ? Theme.Accent : Theme.Text,
            ClientRectangle, TextFormatFlags.HorizontalCenter);
        if (Focused && ShowFocusCues) Theme.Outline(g, Theme.Muted, new RectangleF(3.5f, 3.5f, Width-7, Height-7), 2);
    }
}

public class DeviceSelector : Button
{
    private int selectedIndex = -1;
    private readonly GhostMenu selectionMenu = new();
    internal ContextMenuStrip SelectionMenu => selectionMenu;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<AudioDevice> Items { get; } = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder { get; set; } = "Choose a microphone";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AudioDevice? SelectedItem => selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selectedIndex;
        set { selectedIndex = value; Text = SelectedItem?.Name ?? ""; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? SelectedIndexChanged;
    public DeviceSelector()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false; Height = 48; Cursor = Cursors.Hand; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        BackColor = Theme.Panel; ForeColor = Theme.Text; AccessibleRole = AccessibleRole.ComboBox;
    }
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e); if (Items.Count == 0) return;
        var menu = selectionMenu;
        if (menu.Visible) { menu.Close(); return; }
        // The control owns this menu. Closed runs inside ToolStrip's mouse-up path,
        // so disposing there destroys a handle that WinForms still needs.
        foreach (ToolStripItem oldItem in menu.Items.Cast<ToolStripItem>().ToArray()) oldItem.Dispose();
        menu.Items.Clear();
        menu.BackColor = Theme.Field; menu.ForeColor = Theme.Text; menu.Font = Font;
        for (int i = 0; i < Items.Count; i++)
        {
            int index = i; var item = new ToolStripMenuItem(Items[i].Name) { Checked = i == selectedIndex, Padding = new Padding(10, 8, 10, 8) };
            item.Click += (_, _) => SelectedIndex = index; menu.Items.Add(item);
        }
        menu.Show(this, new Point(0, Height));
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) selectionMenu.Dispose();
        base.Dispose(disposing);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Fill(g, Theme.Field, new RectangleF(.5f, .5f, Width-1, Height-1), 4);
        Theme.Outline(g, Focused ? Theme.AccentEdge : Theme.Border, new RectangleF(.5f, .5f, Width-1, Height-1), 4);
        Theme.TextAt(g, SelectedItem?.Name ?? Placeholder, Font, !Enabled || SelectedItem == null ? Theme.Muted : Theme.Text, new Rectangle(14, 0, Width-48, Height));
        using var pen = new Pen(Theme.Muted, 1.6f);
        g.DrawLines(pen, [new Point(Width-26, Height/2-2), new Point(Width-21, Height/2+3), new Point(Width-16, Height/2-2)]);
    }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Items.Count > 0 && e.KeyCode is Keys.Up or Keys.Down)
        { SelectedIndex = Math.Clamp(selectedIndex + (e.KeyCode == Keys.Up ? -1 : 1), 0, Items.Count-1); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

public class GhostToggle : CheckBox
{
    public GhostToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AutoSize = false; Size = new Size(180, 34); Cursor = Cursors.Hand; BackColor = Theme.Panel;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        int y = (Height-22)/2;
        Theme.Fill(g, Checked ? Theme.Primary : Color.FromArgb(60, 69, 63), new RectangleF(0, y, 34, 18), 9);
        Theme.Outline(g, Checked ? Theme.AccentEdge : Theme.Border, new RectangleF(.5f, y+.5f, 33, 17), 8.5f);
        using var brush = new SolidBrush(Checked ? Theme.Accent : Theme.Text);
        g.FillEllipse(brush, Checked ? 19 : 3, y+3, 12, 12);
        Theme.TextAt(g, Text, Font, Enabled ? Theme.Text : Theme.Muted, new Rectangle(46, 0, Width-46, Height));
        if (Focused && ShowFocusCues) Theme.Outline(g, Theme.Muted, new RectangleF(.5f, .5f, Width-1, Height-1), 3);
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
}

public class GainSlider : Control
{
    private int current = 6;
    private bool dragging;
    public const int Minimum = AudioProcessor.MinimumGainDb, Maximum = AudioProcessor.MaximumGainDb;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => current; set { int next = Math.Clamp(value, Minimum, Maximum); if (next == current) return; current = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? ValueChanged;
    public GainSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = 62; TabStop = true; Cursor = Cursors.Hand; AccessibleName = "Microphone gain, in decibels"; AccessibleRole = AccessibleRole.Slider;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        int left = 12, width = Width-24; float x = left + width * (Value-Minimum)/(float)(Maximum-Minimum);
        Theme.Fill(g, Color.FromArgb(57, 68, 61), new RectangleF(left, 17, width, 3), 1.5f);
        if (x > left) Theme.Fill(g, Theme.Accent, new RectangleF(left, 17, Math.Max(3, x-left), 3), 1.5f);
        using var accent = new SolidBrush(Theme.Text); g.FillEllipse(accent, x-9, 9, 18, 18);
        using var center = new SolidBrush(Theme.Primary); g.FillEllipse(center, x-3, 15, 6, 6);
        using var labelFont = new Font("Segoe UI", 8);
        foreach (int tick in new[] { -24, 0, 24, 48, 72, 96 })
        {
            int tx = left + width*(tick-Minimum)/(Maximum-Minimum);
            Theme.TextAt(g, tick > 0 ? $"+{tick}" : tick.ToString(), labelFont, Theme.Muted, new Rectangle(tx-20, 35, 40, 20), TextFormatFlags.HorizontalCenter);
        }
        if (Focused && ShowFocusCues) Theme.Outline(g, Theme.Accent, new RectangleF(.5f, .5f, Width-1, Height-1), 5);
    }
    private void Pick(int x) => Value = Minimum + (int)Math.Round(Math.Clamp((x-12)/(float)Math.Max(1, Width-24), 0, 1)*(Maximum-Minimum));
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus(); Capture = dragging = true; Pick(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) Pick(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value += Math.Sign(e.Delta); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode) { case Keys.Left: case Keys.Down: Value--; break; case Keys.Right: case Keys.Up: Value++; break; case Keys.Home: Value = Minimum; break; case Keys.End: Value = Maximum; break; default: return; }
        e.Handled = true;
    }
}

public sealed class LevelMeter : Control
{
    private float peak;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Peak { get => peak; set { peak = Math.Max(value, peak * .86f); Invalidate(); } }
    public LevelMeter() { Height = 54; DoubleBuffered = true; BackColor = Theme.Panel; ForeColor = Theme.Muted; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.Clear(BackColor);
        float db = 20*MathF.Log10(Math.Max(peak, .000001f));
        int width = Math.Max(12, Width-82), segments = 32; float segment = width/(float)segments;
        for (int i = 0; i < segments; i++)
        {
            bool lit = i/(float)segments < Math.Clamp((db+60)/60, 0, 1);
            Color color = !lit ? Color.FromArgb(50, 62, 54) : i > 29 ? Color.FromArgb(198, 115, 105) : i > 25 ? Theme.Warning : Theme.Accent;
            using var brush = new SolidBrush(color); g.FillRectangle(brush, i*segment, 6, Math.Max(1, segment-3), 17);
        }
        using var small = new Font("Segoe UI", 8);
        for (int i = 0; i < 5; i++) Theme.TextAt(g, new[] { "-60", "-45", "-30", "-15", "0" }[i], small, Theme.Muted, new Rectangle((int)((width-24)*i/4f), 28, 28, 16));
        Theme.TextAt(g, db < -80 ? "—" : $"{db:0.0}", Font, Theme.Text, new Rectangle(width+8, 0, 72, 28), TextFormatFlags.Right);
        Theme.TextAt(g, "dBFS", small, Theme.Muted, new Rectangle(width+8, 28, 72, 16), TextFormatFlags.Right);
    }
}
