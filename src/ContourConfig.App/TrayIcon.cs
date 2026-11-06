using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ContourConfig.App
{
    // Notification-area icon. Left-click opens the window; right-click shows a WPF (Fluent-themed) menu.
    internal sealed class TrayIcon : IDisposable
    {
        private const int MaximumToolTipLength = 127;
        private readonly Forms.NotifyIcon notifyIcon;
        private readonly Drawing.Icon icon;
        private readonly ContextMenu menu = new() { Placement = PlacementMode.MousePoint };
        private readonly MenuItem boostItem = new() { Header = "Boost enabled", IsCheckable = true };

        public TrayIcon()
        {
            using (Stream stream = Application.GetResourceStream(new Uri("pack://application:,,,/ContourConfig.ico")).Stream)
            {
                icon = new(stream, Forms.SystemInformation.SmallIconSize);
            }
            MenuItem open = new() { Header = "Open Contour Config", FontWeight = FontWeights.SemiBold };
            open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
            boostItem.Click += (_, _) => BoostChangeRequested?.Invoke(this, boostItem.IsChecked);
            MenuItem exit = new() { Header = "Exit" };
            exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
            menu.Items.Add(open);
            menu.Items.Add(boostItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(exit);

            // The tooltip text is set by Update on the first status tick.
            notifyIcon = new() { Icon = icon, Visible = true };
            notifyIcon.MouseClick += OnMouseClick;
        }

        public event EventHandler? OpenRequested;
        public event EventHandler<bool>? BoostChangeRequested;
        public event EventHandler? ExitRequested;

        public void Update(string toolTip, bool boostEnabled, bool boostAvailable)
        {
            string text = toolTip.Length > MaximumToolTipLength ? toolTip[..MaximumToolTipLength] : toolTip;
            if (notifyIcon.Text != text)
            {
                notifyIcon.Text = text;
            }
            boostItem.IsChecked = boostEnabled;
            boostItem.IsEnabled = boostAvailable;
        }

        public void ShowNotification(string title, string text) =>
            notifyIcon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.None);

        public void Dispose()
        {
            menu.IsOpen = false;
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            icon.Dispose();
        }

        private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (e.Button == Forms.MouseButtons.Right)
            {
                menu.IsOpen = true;
                // Without focus the menu would not close when the user clicks elsewhere.
                if (PresentationSource.FromVisual(menu) is HwndSource source)
                {
                    _ = NativeMethods.SetForegroundWindow(source.Handle);
                }
            }
        }
    }
}
