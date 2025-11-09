using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrlMonitor.Notifications;

internal sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);

internal sealed record EmailMessage(
    IReadOnlyList<string> Recipients,
    string Subject,
    string Body,
    IReadOnlyList<EmailAttachment> Attachments,
    string? HtmlBody = null);

internal interface IEmailClient
{
    Task SendAsync(EmailMessage message, SmtpOptions options, CancellationToken cancellationToken);
}

internal sealed class SmtpEmailClient : IEmailClient
{
    public async Task SendAsync(EmailMessage message, SmtpOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);

        if (message.Recipients == null || message.Recipients.Count == 0)
        {
            throw new InvalidOperationException("At least one recipient must be specified.");
        }

        using var mail = new MailMessage
        {
            From = ParseAddress(options.From),
            Subject = message.Subject ?? string.Empty,
            Body = string.Empty,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };

        foreach (var recipient in message.Recipients)
        {
            if (string.IsNullOrWhiteSpace(recipient))
            {
                continue;
            }

            mail.To.Add(ParseAddress(recipient));
        }

        if (mail.To.Count == 0)
        {
            throw new InvalidOperationException("At least one valid recipient must be specified.");
        }

        if (!string.IsNullOrWhiteSpace(message.Body))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.Body, Encoding.UTF8, MediaTypeNames.Text.Plain));
        }

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody!, Encoding.UTF8, MediaTypeNames.Text.Html));
        }

        if (message.Attachments != null)
        {
            foreach (var attachment in message.Attachments)
            {
                if (attachment?.Content == null || attachment.Content.Length == 0)
                {
                    continue;
                }

                var stream = new MemoryStream(attachment.Content, writable: false);
                var mailAttachment = new Attachment(stream, attachment.FileName ?? "attachment", attachment.ContentType ?? "application/octet-stream");
                mail.Attachments.Add(mailAttachment);
            }
        }

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableStartTls,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(options.Username, options.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
    }

    private static MailAddress ParseAddress(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        var trimmed = raw.Trim();
        var start = trimmed.IndexOf('<', StringComparison.Ordinal);
        var end = trimmed.IndexOf('>', StringComparison.Ordinal);
        if (start >= 0 && end > start)
        {
            var name = trimmed.Substring(0, start).Trim();
            var address = trimmed.Substring(start + 1, end - start - 1).Trim();
            return string.IsNullOrWhiteSpace(name)
                ? new MailAddress(address)
                : new MailAddress(address, name);
        }

        return new MailAddress(trimmed);
    }
}
