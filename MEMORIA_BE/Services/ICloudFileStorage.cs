namespace MEMORIA_BE.Services;

public interface ICloudFileStorage
{
    Task<CloudUploadResult> UploadAsync(IFormFile file, Guid ownerUserId, CancellationToken cancellationToken);
}

public sealed record CloudUploadResult(
    string OriginalFileName,
    string StoredFileName,
    string FileUrl,
    string MimeType,
    long FileSizeBytes,
    string Sha256Hash);
