#if WINDOWS
using System;
using System.Threading;
using System.Windows.Forms;

namespace CrlMonitor.Eula;

internal static class WindowsEulaDialog
{
    public static bool TryShow(EulaMetadata metadata)
    {
        try
        {
            var accepted = false;
            var completed = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    accepted = ShowDialog(metadata);
                }
                finally
                {
                    completed.Set();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            completed.Wait();
            return accepted;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShowDialog(EulaMetadata metadata)
    {
        using var form = new Form
        {
            Text = "CrlMonitor End User Licence Agreement",
            Width = 800,
            Height = 600,
            StartPosition = FormStartPosition.CenterScreen
        };

        var textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = metadata.Text
        };

        var acceptButton = new Button
        {
            Text = "Accept",
            DialogResult = DialogResult.OK,
            Width = 120,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };

        var declineButton = new Button
        {
            Text = "Decline",
            DialogResult = DialogResult.Cancel,
            Width = 120,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };

        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50
        };
        acceptButton.Left = buttonPanel.Width - acceptButton.Width - 10;
        acceptButton.Top = (buttonPanel.Height - acceptButton.Height) / 2;
        declineButton.Left = 10;
        declineButton.Top = (buttonPanel.Height - declineButton.Height) / 2;
        buttonPanel.Controls.Add(declineButton);
        buttonPanel.Controls.Add(acceptButton);

        form.Controls.Add(textBox);
        form.Controls.Add(buttonPanel);
        form.AcceptButton = acceptButton;
        form.CancelButton = declineButton;

        return form.ShowDialog() == DialogResult.OK;
    }
}
#endif
