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

    private static string GetPurposeLabel(string purpose)
    {
        return purpose switch
        {
            "Register" => "Account verification",
            "GoogleLogin" => "Google sign-in",
            _ => "Secure sign-in"
        };
    }
}
