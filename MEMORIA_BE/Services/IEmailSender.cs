namespace MEMORIA_BE.Services;

public interface IEmailSender
{
    Task SendOtpAsync(string toEmail, string toName, string code, string purpose, CancellationToken cancellationToken);

    Task SendFutureLetterAsync(
        string toEmail,
        string toName,
        string senderName,
        string title,
        string? body,
        DateTime deliveryDate,
        IReadOnlyCollection<FutureLetterEmailAttachment> attachments,
        CancellationToken cancellationToken);
}

public sealed record FutureLetterEmailAttachment(string FileName, string FileUrl, string MimeType, long FileSizeBytes);
