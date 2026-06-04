namespace MEMORIA_BE.Services;

public interface IEmailSender
{
    Task SendOtpAsync(string toEmail, string toName, string code, string purpose, CancellationToken cancellationToken);

    Task SendProofOfLifePingAsync(string toEmail, string toName, int attemptNumber, DateTime responseDeadline, string confirmAliveUrl, CancellationToken cancellationToken);

    Task SendLegacyPrimaryBeneficiaryNoticeAsync(string toEmail, string toName, string ownerName, string claimUrl, CancellationToken cancellationToken);

    Task SendLegacyClaimRejectedAsync(string toEmail, string toName, string ownerName, string reason, string claimUrl, CancellationToken cancellationToken);

    Task SendLegacyReleaseAsync(
        string toEmail,
        string toName,
        string ownerName,
        IReadOnlyCollection<LegacyReleaseFile> files,
        CancellationToken cancellationToken);

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

public sealed record LegacyReleaseFile(string FileName, string FileUrl, string Bucket, long FileSizeBytes);
