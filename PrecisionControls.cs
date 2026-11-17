using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace GhostMic;

public sealed class GhostMenu : ContextMenuStrip
{
    public GhostMenu()
    {
        BackColor = Theme.Field; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10);
        Renderer = new ConsoleMenuRenderer(); ShowImageMargin = false; ShowCheckMargin = true;
        Padding = new Padding(6); DropShadowEnabled = false;
    }
    protected override void OnItemAdded(ToolStripItemEventArgs e)
    {
        base.OnItemAdded(e);
        if (e.Item != null) e.Item.Padding = e.Item is ToolStripSeparator ? new Padding(0, 4, 0, 4) : new Padding(6, 7, 14, 7);
    }
    private sealed class ConsoleMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Field);
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        { using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width-1, e.ToolStrip.Height-1); }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            using var brush = new SolidBrush(Theme.Selection);
            e.Graphics.FillRectangle(brush, new Rectangle(0, 0, e.Item.Width, e.Item.Height));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) =>
            Theme.TextAt(e.Graphics, e.Text ?? "", e.TextFont ?? e.Item.Font, e.Item.Enabled ? Theme.Text : Theme.Muted, e.TextRectangle);
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int x = e.ImageRectangle.X+2, y = (e.Item.Height-12)/2;
            using var pen = new Pen(e.Item.Enabled ? Theme.Accent : Theme.Muted, 1.7f);
            e.Graphics.DrawLines(pen, [new Point(x, y+6), new Point(x+4, y+10), new Point(x+12, y+2)]);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 12, e.Item.Height/2, e.Item.Width-12, e.Item.Height/2); }
    }
}

public sealed class DecibelStepper : UserControl
{
    private readonly TextBox editor = new() { BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, BackColor = Theme.Field, ForeColor = Theme.Text };
    private readonly GhostButton decrease = new() { Text = "−", Quiet = true, AccessibleName = "Lower gate threshold" };
    private readonly GhostButton increase = new() { Text = "+", Quiet = true, AccessibleName = "Raise gate threshold" };
    private decimal minimum = -80, maximum = -20, current = -50;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public decimal Minimum { get => minimum; set { minimum = value; Value = current; } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public decimal Maximum { get => maximum; set { maximum = value; Value = current; } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public decimal Value
    {
        get => current;
        set
        {
            decimal next = Math.Clamp(decimal.Truncate(value), Minimum, Maximum);
            bool changed = next != current; current = next;
            string text = current.ToString(CultureInfo.InvariantCulture); if (editor.Text != text) editor.Text = text;
            if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? ValueChanged;
    public DecibelStepper()
    {
        DoubleBuffered = true; Size = new Size(104, 30); BackColor = Theme.Field;
        AccessibleName = "Noise gate threshold in decibels";
        editor.Text = "-50"; decrease.BackColor = increase.BackColor = Theme.Field;
        Controls.AddRange([decrease, editor, increase]);
        decrease.Click += (_, _) => Value--; increase.Click += (_, _) => Value++;
        editor.TextChanged += (_, _) =>
        {
            if (decimal.TryParse(editor.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out decimal parsed) && parsed >= Minimum && parsed <= Maximum) Value = parsed;
        };
        editor.Validating += (_, _) => { Value = decimal.TryParse(editor.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : current; };
        editor.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Up or Keys.Down) { Value += e.KeyCode == Keys.Up ? 1 : -1; e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Enter) { Validate(); e.SuppressKeyPress = true; }
        };
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (editor == null) return;
        decrease.SetBounds(1, 1, 25, Height-2); increase.SetBounds(Width-26, 1, 25, Height-2);
        editor.SetBounds(26, Math.Max(1, (Height-editor.PreferredHeight)/2), Math.Max(1, Width-52), editor.PreferredHeight);
    }
    protected override void OnPaint(PaintEventArgs e)
    { base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Theme.Outline(e.Graphics, Theme.Border, new RectangleF(.5f, .5f, Width-1, Height-1), 3); }
}
