namespace GhostMic;

public sealed partial class MainWindow
{
    internal static void RunTrayLifecycleTest()
    {
        string resultPath = Path.Combine(AppContext.BaseDirectory, "tray-test-result.txt");
        using var window = new MainWindow(false, forceTray: true);
        using var check = new System.Windows.Forms.Timer { Interval = 500 };
        check.Tick += (_, _) =>
        {
            check.Stop();
            try
            {
                if (window.Visible || !window.IsHandleCreated || window.engine.Running) throw new Exception("Tray launch showed a window or started capture.");
                SendMessage(window.Handle, InstanceControl.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                if (!window.Visible) throw new Exception("Tray restore did not show the window.");
                window.WindowState = FormWindowState.Minimized;
                if (window.Visible) throw new Exception("Minimize did not hide to the tray.");
                SendMessage(window.Handle, InstanceControl.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                window.Close();
                if (window.IsDisposed || window.Visible) throw new Exception("Closing idle window did not preserve the tray app.");
                SendMessage(window.Handle, InstanceControl.QuitMessage, IntPtr.Zero, IntPtr.Zero);
                if (!window.IsDisposed) throw new Exception("Explicit Quit did not dispose the tray app.");
                File.WriteAllText(resultPath, "PASS: tray launch stayed hidden; restore, minimize, close-to-tray and explicit Quit verified. No capture started.");
            }
            catch (Exception error)
            {
                File.WriteAllText(resultPath, error.ToString()); Environment.ExitCode = 1;
                if (!window.IsDisposed) { window.quitting = true; window.Close(); }
            }
        };
        check.Start(); Application.Run(window);
    }

    // Send mouse messages through the native dropdown, rather than calling an
    // item's Click delegate: the reported failure happens after the delegate returns.
    private void VerifyMenuInteraction()
    {
        Exception? nativeError = null;
        void OnUiError(object sender, ThreadExceptionEventArgs e) => nativeError = e.Exception;
        Application.ThreadException += OnUiError;
        var devices = input.Items.ToArray();
        int selected = input.SelectedIndex;
        try
        {
            input.Items.Clear();
            input.Items.AddRange([new AudioDevice("test-a", "Regression microphone A"), new AudioDevice("test-b", "Regression microphone B")]);
            input.SelectedIndex = 0;
            for (int i = 0; i < 6; i++)
            {
                input.PerformClick();
                ClickMenuItem(input.SelectionMenu, i % 2);
                if (nativeError != null) throw new InvalidOperationException("Microphone menu mouse selection failed.", nativeError);
                if (input.SelectionMenu.IsDisposed || input.SelectedIndex != i % 2 || input.SelectionMenu.Visible)
                    throw new InvalidOperationException("Microphone menu did not select and close cleanly.");
                input.PerformClick();
                input.SelectionMenu.Close(ToolStripDropDownCloseReason.AppClicked);
                input.PerformClick();
                input.SelectionMenu.Close(ToolStripDropDownCloseReason.Keyboard);
            }
        }
        finally
        {
            input.SelectionMenu.Close();
            input.Items.Clear(); input.Items.AddRange(devices); input.SelectedIndex = selected;
            Application.ThreadException -= OnUiError;
        }

        Application.ThreadException += OnUiError;
        try
        {
            for (int i = 0; i < 6; i++)
            {
                ShowDeviceMenu(input);
                ClickMenuItem(settingsMenu, 0); // Refresh devices: no external apps or microphone capture.
                if (nativeError != null) throw new InvalidOperationException("Device settings menu mouse selection failed.", nativeError);
                if (settingsMenu.IsDisposed || settingsMenu.Visible)
                    throw new InvalidOperationException("Device settings menu did not close cleanly.");
                ShowDeviceMenu(input);
                settingsMenu.Close(ToolStripDropDownCloseReason.AppClicked);
                ShowDeviceMenu(input);
                settingsMenu.Close(ToolStripDropDownCloseReason.Keyboard);
            }
        }
        finally { Application.ThreadException -= OnUiError; }
        if (engine.Running) throw new InvalidOperationException("Menu checks unexpectedly started microphone capture.");
    }

    private static void ClickMenuItem(ContextMenuStrip menu, int itemIndex)
    {
        if (!menu.Visible) throw new InvalidOperationException("Menu failed to open.");
        Rectangle bounds = menu.Items[itemIndex].Bounds;
        int x = bounds.X + bounds.Width / 2, y = bounds.Y + bounds.Height / 2;
        var position = (IntPtr)((y << 16) | (x & 0xffff));
        SendMessage(menu.Handle, 0x201, (IntPtr)1, position); // WM_LBUTTONDOWN
        SendMessage(menu.Handle, 0x202, IntPtr.Zero, position); // WM_LBUTTONUP
    }
}
