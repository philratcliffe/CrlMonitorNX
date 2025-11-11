#if WINDOWS
#pragma warning disable IDE0055 // Windows Forms code with specific formatting for readability
using System.Windows.Forms;

namespace CrlMonitor.Eula;

internal static class WindowsEulaDialog
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    public static bool TryShow(EulaMetadata metadata)
    {
#pragma warning disable CA1031 // Defensive: all exceptions should result in fallback to console EULA
        try
        {
            var accepted = false;
            using var completed = new System.Threading.ManualResetEventSlim();
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    accepted = ShowDialog(metadata.Text);
                }
                finally
                {
                    completed.Set();
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            completed.Wait();
            return accepted;
        }
        catch
        {
            return false;
        }
#pragma warning restore CA1031
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static bool ShowDialog(string eulaText)
    {
        using var form = new Form
        {
            Text = "CrlMonitor End User Licence Agreement",
            Width = 800,
            Height = 600,
            StartPosition = FormStartPosition.CenterScreen
        };

        // Normalize line endings for Windows Forms (LF -> CRLF)
        var normalizedText = eulaText.Replace("\r\n", "\n", System.StringComparison.Ordinal).Replace("\n", "\r\n", System.StringComparison.Ordinal);

        var textBox = new RichTextBox
        {
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = normalizedText,
            WordWrap = false,
            Font = new System.Drawing.Font("Consolas", 9F)
        };

        var acceptButton = new Button
        {
            Text = "Accept",
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            Width = 120
        };

        var declineButton = new Button
        {
            Text = "Decline",
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            Width = 120
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

        form.AcceptButton = acceptButton;
        form.CancelButton = declineButton;
        form.Controls.Add(textBox);
        form.Controls.Add(buttonPanel);

        return form.ShowDialog() == DialogResult.OK;
    }
}
#pragma warning restore IDE0055
#endif
