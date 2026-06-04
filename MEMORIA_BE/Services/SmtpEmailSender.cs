using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Options;
using MEMORIA_BE.Configurations;

namespace MEMORIA_BE.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;
    private readonly IWebHostEnvironment _environment;

    public SmtpEmailSender(IOptions<SmtpSettings> settings, ILogger<SmtpEmailSender> logger, IWebHostEnvironment environment)
    {
        _settings = settings.Value;
        _logger = logger;
        _environment = environment;
    }

    public async Task SendOtpAsync(string toEmail, string toName, string code, string purpose, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host) ||
            string.IsNullOrWhiteSpace(_settings.FromEmail) ||
            string.IsNullOrWhiteSpace(_settings.Username) ||
            string.IsNullOrWhiteSpace(_settings.Password))
        {
            _logger.LogWarning("SMTP is not configured. OTP for {Email} / {Purpose}: {Code}", toEmail, purpose, code);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.FromEmail, _settings.FromName),
            Subject = "Memoria verification code",
            Body = BuildPlainTextBody(toName, code),
            IsBodyHtml = false
        };
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(BuildHtmlBody(toName, code, purpose), Encoding.UTF8, MediaTypeNames.Text.Html));
        message.To.Add(new MailAddress(toEmail, toName));

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl,
            Credentials = string.IsNullOrWhiteSpace(_settings.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_settings.Username, _settings.Password)
        };

        await client.SendMailAsync(message, cancellationToken);
        if (_environment.IsDevelopment())
        {
            _logger.LogInformation("Sent OTP email to {Email} for {Purpose}", toEmail, purpose);
        }
    }

    public async Task SendFutureLetterAsync(
        string toEmail,
        string toName,
        string senderName,
        string title,
        string? body,
        DateTime deliveryDate,
        IReadOnlyCollection<FutureLetterEmailAttachment> attachments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host) ||
            string.IsNullOrWhiteSpace(_settings.FromEmail) ||
            string.IsNullOrWhiteSpace(_settings.Username) ||
            string.IsNullOrWhiteSpace(_settings.Password))
        {
            _logger.LogWarning("SMTP is not configured. Future letter '{Title}' for {Email} was not sent.", title, toEmail);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.FromEmail, _settings.FromName),
            Subject = $"Memoria future letter: {title}",
            Body = BuildFutureLetterPlainTextBody(toName, senderName, title, body, deliveryDate, attachments),
            IsBodyHtml = false
        };
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            BuildFutureLetterHtmlBody(toName, senderName, title, body, deliveryDate, attachments),
            Encoding.UTF8,
            MediaTypeNames.Text.Html));
        message.To.Add(new MailAddress(toEmail, string.IsNullOrWhiteSpace(toName) ? toEmail : toName));

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl,
            Credentials = string.IsNullOrWhiteSpace(_settings.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_settings.Username, _settings.Password)
        };

        await client.SendMailAsync(message, cancellationToken);
        if (_environment.IsDevelopment())
        {
            _logger.LogInformation("Sent future letter '{Title}' to {Email}", title, toEmail);
        }
    }

    public async Task SendProofOfLifePingAsync(string toEmail, string toName, int attemptNumber, DateTime responseDeadline, string confirmAliveUrl, CancellationToken cancellationToken)
    {
        var subject = $"Memoria proof-of-life check #{attemptNumber}";
        var body = $"""
            Hi {Fallback(toName, "there")},

            Memoria has not detected activity on your account. Please sign in before {responseDeadline:yyyy-MM-dd HH:mm} UTC to confirm you are safe and still controlling your account.

            If you have already signed in, you can ignore this message.
            """;
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#fbf9f1;font-family:'Segoe UI',Arial,sans-serif;color:#1b1c17;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:36px 16px;background:#fbf9f1;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:580px;background:#ffffff;border-radius:28px;overflow:hidden;box-shadow:0 24px 70px -32px rgba(112,88,91,.45);">
                      <tr>
                        <td style="padding:32px;background:#fadadd;">
                          <div style="font-size:28px;line-height:34px;font-weight:800;color:#70585b;">Memoria</div>
                          <div style="margin-top:4px;font-size:13px;font-weight:700;color:#4f4445;">Proof-of-life check</div>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;">
                          <h1 style="margin:0 0 12px;font-size:28px;line-height:36px;color:#1b1c17;">Are you safe?</h1>
                          <p style="margin:0 0 20px;color:#4f4445;font-size:16px;line-height:26px;">Hi {{System.Net.WebUtility.HtmlEncode(Fallback(toName, "there"))}}, Memoria has not detected account activity. Please confirm before <strong>{{responseDeadline:yyyy-MM-dd HH:mm}} UTC</strong>.</p>
                          <a href="{{System.Net.WebUtility.HtmlEncode(confirmAliveUrl)}}" style="display:inline-block;background:#70585b;color:#ffffff;border-radius:999px;padding:15px 28px;font-size:16px;font-weight:800;text-decoration:none;">I'm alive</a>
                          <p style="margin:22px 0 0;color:#807475;font-size:13px;line-height:20px;">If you do not confirm after 3 checks, Memoria may notify your primary beneficiary.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        await SendSimpleEmailAsync(toEmail, toName, subject, body, html, cancellationToken);
    }

    public async Task SendLegacyPrimaryBeneficiaryNoticeAsync(string toEmail, string toName, string ownerName, string claimUrl, CancellationToken cancellationToken)
    {
        var subject = "Memoria legacy transfer notice";
        var body = $"""
            Hi {Fallback(toName, "there")},

            Memoria has detected prolonged inactivity for {Fallback(ownerName, "the account owner")} and marked the account as suspected absent.

            You are listed as a beneficiary. Please use this secure link to submit the required identity and death-certificate documents within 7 days:
            {claimUrl}
            """;
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#fbf9f1;font-family:'Segoe UI',Arial,sans-serif;color:#1b1c17;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:36px 16px;background:#fbf9f1;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:600px;background:#ffffff;border-radius:28px;overflow:hidden;box-shadow:0 24px 70px -32px rgba(112,88,91,.45);">
                    <tr><td style="padding:32px;background:#fadadd;"><div style="font-size:28px;font-weight:800;color:#70585b;">Memoria</div><div style="margin-top:4px;font-size:13px;font-weight:700;color:#4f4445;">Legacy claim verification</div></td></tr>
                    <tr><td style="padding:32px;">
                      <h1 style="margin:0 0 12px;font-size:28px;line-height:36px;color:#1b1c17;">Legacy documents required</h1>
                      <p style="margin:0 0 20px;color:#4f4445;font-size:16px;line-height:26px;">Hi {{System.Net.WebUtility.HtmlEncode(Fallback(toName, "there"))}}, Memoria has detected prolonged inactivity for {{System.Net.WebUtility.HtmlEncode(Fallback(ownerName, "the account owner"))}}. Please verify your identity and upload legal documents within 7 days.</p>
                      <a href="{{System.Net.WebUtility.HtmlEncode(claimUrl)}}" style="display:inline-block;background:#70585b;color:#ffffff;border-radius:999px;padding:15px 28px;font-size:16px;font-weight:800;text-decoration:none;">Claim legacy securely</a>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
        await SendSimpleEmailAsync(toEmail, toName, subject, body, html, cancellationToken);
    }

    public async Task SendLegacyClaimRejectedAsync(string toEmail, string toName, string ownerName, string reason, string claimUrl, CancellationToken cancellationToken)
    {
        var subject = "Memoria legacy claim needs more documents";
        var body = $"""
            Hi {Fallback(toName, "there")},

            Memoria reviewed your legacy claim for {Fallback(ownerName, "the account owner")} and needs you to resubmit documents.

            Reason:
            {reason}

            Please use your secure claim link again within 7 days:
            {claimUrl}
            """;
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#fbf9f1;font-family:'Segoe UI',Arial,sans-serif;color:#1b1c17;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:36px 16px;background:#fbf9f1;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:600px;background:#ffffff;border-radius:28px;overflow:hidden;box-shadow:0 24px 70px -32px rgba(112,88,91,.45);">
                    <tr><td style="padding:32px;background:#ffdad6;"><div style="font-size:28px;font-weight:800;color:#70585b;">Memoria</div><div style="margin-top:4px;font-size:13px;font-weight:700;color:#93000a;">Additional documents required</div></td></tr>
                    <tr><td style="padding:32px;">
                      <h1 style="margin:0 0 12px;font-size:28px;line-height:36px;color:#1b1c17;">Please resubmit your claim documents</h1>
                      <p style="margin:0 0 14px;color:#4f4445;font-size:16px;line-height:26px;">Hi {{System.Net.WebUtility.HtmlEncode(Fallback(toName, "there"))}}, Memoria needs more information before approving the legacy claim for {{System.Net.WebUtility.HtmlEncode(Fallback(ownerName, "the account owner"))}}.</p>
                      <div style="margin:18px 0;padding:18px;background:#fbf9f1;border:1px solid #d2c3c4;border-radius:18px;color:#1b1c17;font-size:15px;line-height:24px;"><strong>Reason:</strong><br>{{System.Net.WebUtility.HtmlEncode(reason)}}</div>
                      <a href="{{System.Net.WebUtility.HtmlEncode(claimUrl)}}" style="display:inline-block;background:#70585b;color:#ffffff;border-radius:999px;padding:15px 28px;font-size:16px;font-weight:800;text-decoration:none;">Resubmit documents</a>
                      <p style="margin:22px 0 0;color:#807475;font-size:13px;line-height:20px;">Please resubmit within 7 days before Memoria moves to the next beneficiary.</p>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
        await SendSimpleEmailAsync(toEmail, toName, subject, body, html, cancellationToken);
    }

    public async Task SendLegacyReleaseAsync(
        string toEmail,
        string toName,
        string ownerName,
        IReadOnlyCollection<LegacyReleaseFile> files,
        CancellationToken cancellationToken)
    {
        var fileLines = files.Count == 0
            ? "No legacy files were attached to this account."
            : string.Join(Environment.NewLine, files.Select(item => $"- {item.FileName}: {item.FileUrl}"));
        var subject = "Memoria legacy archive released";
        var body = $"""
            Hi {Fallback(toName, "there")},

            Memoria has approved your legacy claim for {Fallback(ownerName, "the account owner")}.

            Legacy files:
            {fileLines}
            """;
        var filesHtml = files.Count == 0
            ? "<p style=\"margin:0;color:#4f4445;font-size:15px;line-height:24px;\">No legacy files were attached to this account.</p>"
            : string.Join("", files.Select(item =>
            {
                var safeName = WebUtility.HtmlEncode(item.FileName);
                var safeUrl = WebUtility.HtmlEncode(item.FileUrl);
                var safeBucket = WebUtility.HtmlEncode(item.Bucket);
                return $"""
                    <tr>
                      <td style="padding:12px 0;border-top:1px solid #e4e3db;">
                        <div style="font-size:15px;font-weight:800;color:#1b1c17;word-break:break-word;">{safeName}</div>
                        <div style="margin-top:3px;color:#807475;font-size:12px;line-height:18px;">{safeBucket} - {FormatFileSize(item.FileSizeBytes)}</div>
                      </td>
                      <td align="right" style="padding:12px 0 12px 12px;border-top:1px solid #e4e3db;">
                        <a href="{safeUrl}" style="display:inline-block;background:#70585b;color:#ffffff;border-radius:999px;padding:10px 16px;font-size:13px;font-weight:800;text-decoration:none;white-space:nowrap;">Download</a>
                      </td>
                    </tr>
                    """;
            }));
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#fbf9f1;font-family:'Segoe UI',Arial,sans-serif;color:#1b1c17;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:36px 16px;background:#fbf9f1;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;background:#ffffff;border-radius:28px;overflow:hidden;box-shadow:0 24px 70px -32px rgba(112,88,91,.45);">
                    <tr><td style="padding:32px;background:#caead8;"><div style="font-size:28px;font-weight:800;color:#486457;">Memoria</div><div style="margin-top:4px;font-size:13px;font-weight:700;color:#042016;">Legacy archive released</div></td></tr>
                    <tr><td style="padding:32px;">
                      <h1 style="margin:0 0 12px;font-size:28px;line-height:36px;color:#1b1c17;">Your claim was approved</h1>
                      <p style="margin:0 0 22px;color:#4f4445;font-size:16px;line-height:26px;">Hi {{WebUtility.HtmlEncode(Fallback(toName, "there"))}}, Memoria has approved your claim for {{WebUtility.HtmlEncode(Fallback(ownerName, "the account owner"))}}. You can download the released legacy files below.</p>
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0">{{filesHtml}}</table>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        await SendSimpleEmailAsync(toEmail, toName, subject, body, html, cancellationToken);
    }

    private async Task SendSimpleEmailAsync(string toEmail, string toName, string subject, string body, string? htmlBody, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host) ||
            string.IsNullOrWhiteSpace(_settings.FromEmail) ||
            string.IsNullOrWhiteSpace(_settings.Username) ||
            string.IsNullOrWhiteSpace(_settings.Password))
        {
            _logger.LogWarning("SMTP is not configured. Email '{Subject}' for {Email} was not sent.", subject, toEmail);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.FromEmail, _settings.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
        }
        message.To.Add(new MailAddress(toEmail, string.IsNullOrWhiteSpace(toName) ? toEmail : toName));

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl,
            Credentials = string.IsNullOrWhiteSpace(_settings.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_settings.Username, _settings.Password)
        };

        await client.SendMailAsync(message, cancellationToken);
    }

    private static string BuildPlainTextBody(string toName, string code)
    {
        return $"""
            Hi {toName},

            Your Memoria verification code is: {code}

            This code expires in 10 minutes. If you did not request this code, you can ignore this email.
            """;
    }

    private static string BuildHtmlBody(string toName, string code, string purpose)
    {
        var safeName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(toName) ? "there" : toName);
        var safePurpose = WebUtility.HtmlEncode(GetPurposeLabel(purpose));

        return $$"""
            <!doctype html>
            <html lang="en">
              <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>Memoria Verification Code</title>
              </head>
              <body style="margin:0;padding:0;background:#fbf9f1;font-family:'Plus Jakarta Sans','Segoe UI',Arial,sans-serif;color:#1b1c17;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#fbf9f1;padding:36px 16px;">
                  <tr>
                    <td align="center">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:28px;overflow:hidden;box-shadow:0 24px 70px -32px rgba(112,88,91,.45);">
                        <tr>
                          <td style="padding:34px 34px 20px;background:linear-gradient(135deg,#fadadd 0%,#fbf9f1 54%,#caead8 100%);">
                            <div style="font-size:28px;line-height:34px;font-weight:800;color:#70585b;letter-spacing:0;">Memoria</div>
                            <div style="font-size:13px;line-height:20px;font-weight:700;color:#4f4445;margin-top:4px;">Digital Legacy</div>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:34px;">
                            <div style="display:inline-block;background:#e0e0f4;color:#5c5d6e;border-radius:999px;padding:8px 14px;font-size:12px;line-height:16px;font-weight:800;letter-spacing:.12em;text-transform:uppercase;">{{safePurpose}}</div>
                            <h1 style="margin:22px 0 10px;font-size:30px;line-height:38px;font-weight:800;color:#1b1c17;letter-spacing:0;">Your verification code</h1>
                            <p style="margin:0;color:#4f4445;font-size:16px;line-height:26px;">Hi {{safeName}}, use this code to continue signing in to your Memoria vault.</p>
                            <div style="margin:30px 0;padding:24px 18px;background:#f5f4ec;border:1px solid #d2c3c4;border-radius:24px;text-align:center;">
                              <div style="font-size:42px;line-height:48px;font-weight:800;letter-spacing:.28em;color:#1b1c17;font-family:'Segoe UI',Arial,sans-serif;">{{code}}</div>
                            </div>
                            <p style="margin:0;color:#4f4445;font-size:14px;line-height:22px;">This code expires in <strong style="color:#70585b;">10 minutes</strong>. If you did not request it, you can safely ignore this email.</p>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:22px 34px;background:#f5f4ec;color:#807475;font-size:12px;line-height:18px;">
                            Memoria keeps your memories and legacy access protected.
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;
    }

    private static string BuildFutureLetterPlainTextBody(
        string toName,
        string senderName,
        string title,
        string? body,
        DateTime deliveryDate,
        IReadOnlyCollection<FutureLetterEmailAttachment> attachments)
    {
        var attachmentLines = attachments.Count == 0
            ? "No attachments."
            : string.Join(Environment.NewLine, attachments.Select(item => $"- {item.FileName}: {item.FileUrl}"));

        return $"""
            Hi {Fallback(toName, "there")},

            {Fallback(senderName, "Someone")} scheduled this future letter for you through Memoria.

            Title: {title}
            Delivery date: {deliveryDate:yyyy-MM-dd HH:mm} UTC

            Message:
            {body}

            Attachments:
            {attachmentLines}
            """;
    }

    private static string BuildFutureLetterHtmlBody(
        string toName,
        string senderName,
        string title,
        string? body,
        DateTime deliveryDate,
        IReadOnlyCollection<FutureLetterEmailAttachment> attachments)
    {
        var safeName = WebUtility.HtmlEncode(Fallback(toName, "there"));
        var safeSender = WebUtility.HtmlEncode(Fallback(senderName, "Someone"));
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeBody = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(body) ? "No text message was included." : body)
            .Replace("\r\n", "<br>")
            .Replace("\n", "<br>");
        var filesHtml = BuildFutureLetterAttachmentHtml(attachments); /*
            ? "<p style=\"margin:0;color:#4f4445;font-size:14px;line-height:22px;\">No attachments.</p>"
            : string.Join("", attachments.Select(item =>
            {
                var safeFileName = WebUtility.HtmlEncode(item.FileName);
                var safeFileUrl = WebUtility.HtmlEncode(item.FileUrl);
                var safeMime = WebUtility.HtmlEncode(item.MimeType);
                return $"""
                    <tr>
                      <td style="padding:12px 0;border-top:1px solid #e4e3db;">
                        <a href="{safeFileUrl}" style="color:#70585b;font-weight:700;text-decoration:none;">{safeFileName}</a>
                        <div style="color:#807475;font-size:12px;line-height:18px;margin-top:3px;">{safeMime} · {FormatFileSize(item.FileSizeBytes)}</div>
                      </td>
                    </tr>
                    """;
            }));*/

        return $$"""
            <!doctype html>
            <html lang="en">
              <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>Memoria Future Letter</title>
              </head>
              <body style="margin:0;padding:0;background:#fbf9f1;font-family:'Plus Jakarta Sans','Segoe UI',Arial,sans-serif;color:#1b1c17;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#fbf9f1;padding:36px 16px;">
                  <tr>
                    <td align="center">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;background:#ffffff;border-radius:28px;overflow:hidden;box-shadow:0 24px 70px -32px rgba(112,88,91,.45);">
                        <tr>
                          <td style="padding:34px;background:linear-gradient(135deg,#fadadd 0%,#fbf9f1 54%,#caead8 100%);">
                            <div style="font-size:28px;line-height:34px;font-weight:800;color:#70585b;">Memoria</div>
                            <div style="font-size:13px;line-height:20px;font-weight:700;color:#4f4445;margin-top:4px;">Future Postbox</div>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:34px;">
                            <div style="display:inline-block;background:#e0e0f4;color:#5c5d6e;border-radius:999px;padding:8px 14px;font-size:12px;line-height:16px;font-weight:800;letter-spacing:.12em;text-transform:uppercase;">Scheduled letter</div>
                            <h1 style="margin:22px 0 10px;font-size:30px;line-height:38px;font-weight:800;color:#1b1c17;">{{safeTitle}}</h1>
                            <p style="margin:0;color:#4f4445;font-size:16px;line-height:26px;">Hi {{safeName}}, {{safeSender}} scheduled this message to reach you on {{deliveryDate:yyyy-MM-dd HH:mm}} UTC.</p>
                            <div style="margin:28px 0;padding:24px;background:#f5f4ec;border:1px solid #d2c3c4;border-radius:24px;color:#1b1c17;font-size:16px;line-height:26px;">{{safeBody}}</div>
                            <h2 style="margin:0 0 10px;font-size:18px;line-height:26px;color:#1b1c17;">Attachments</h2>
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0">{{filesHtml}}</table>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:22px 34px;background:#f5f4ec;color:#807475;font-size:12px;line-height:18px;">
                            This message was scheduled through Memoria Digital Legacy.
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;
    }

    private static string BuildFutureLetterAttachmentHtml(IReadOnlyCollection<FutureLetterEmailAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return "<p style=\"margin:0;color:#4f4445;font-size:14px;line-height:22px;\">No attachments.</p>";
        }

        return string.Join("", attachments.Select(item =>
        {
            var safeFileName = WebUtility.HtmlEncode(item.FileName);
            var safeFileUrl = WebUtility.HtmlEncode(item.FileUrl);
            var safeMime = WebUtility.HtmlEncode(item.MimeType);
            var isVideo = item.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
            var isAudio = item.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
            var icon = isVideo ? "&#9658;" : isAudio ? "&#9835;" : "FILE";
            var iconSize = isVideo || isAudio ? "18px" : "10px";
            var action = isVideo ? "Open video" : isAudio ? "Open audio" : "Open file";

            return $"""
                <tr>
                  <td style="padding:10px 0;">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="border:1px solid #e4e3db;border-radius:18px;background:#fbf9f1;">
                      <tr>
                        <td width="54" style="padding:14px 0 14px 16px;vertical-align:top;">
                          <div style="width:42px;height:42px;border-radius:14px;background:#e0e0f4;color:#5c5d6e;text-align:center;line-height:42px;font-size:{iconSize};font-weight:800;">{icon}</div>
                        </td>
                        <td style="padding:14px 12px;vertical-align:top;">
                          <div style="font-size:14px;line-height:20px;font-weight:800;color:#1b1c17;word-break:break-word;">{safeFileName}</div>
                          <div style="color:#807475;font-size:12px;line-height:18px;margin-top:3px;">{safeMime} - {FormatFileSize(item.FileSizeBytes)}</div>
                        </td>
                        <td align="right" style="padding:14px 16px 14px 8px;vertical-align:middle;">
                          <a href="{safeFileUrl}" style="display:inline-block;background:#70585b;color:#ffffff;border-radius:999px;padding:10px 16px;font-size:13px;line-height:18px;font-weight:800;text-decoration:none;white-space:nowrap;">{action}</a>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
                """;
        }));
    }

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:0.#} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:0.#} KB";
        }

        return $"{bytes} B";
    }

    private static string GetPurposeLabel(string purpose)
    {
        return purpose switch
        {
            "Register" => "Account verification",
            "GoogleLogin" => "Google sign-in",
            "LegacyContract" => "Legacy contract signature",
            "LegacyClaimOtp" => "Legacy claim verification",
            _ => "Secure sign-in"
        };
    }
}
