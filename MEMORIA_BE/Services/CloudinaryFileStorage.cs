using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MEMORIA_BE.Configurations;

namespace MEMORIA_BE.Services;

public sealed class CloudinaryFileStorage : ICloudFileStorage
{
    private readonly CloudinarySettings _settings;
    private readonly HttpClient _httpClient;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CloudinaryFileStorage(
        IOptions<CloudinarySettings> settings,
        HttpClient httpClient,
        IWebHostEnvironment environment,
        IHttpContextAccessor httpContextAccessor)
    {
        _settings = settings.Value;
        _httpClient = httpClient;
        _environment = environment;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<CloudUploadResult> UploadAsync(IFormFile file, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.CloudName))
        {
            throw new InvalidOperationException("Cloudinary CloudName is missing.");
        }

        await using var source = file.OpenReadStream();
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var sha256Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var publicId = $"{ownerUserId:N}/{Guid.NewGuid():N}";
        var folder = string.IsNullOrWhiteSpace(_settings.Folder) ? "memoria/future-letters" : _settings.Folder.Trim('/');
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        if (_settings.BypassUploadForLocalTesting)
        {
            var extension = Path.GetExtension(file.FileName);
            var storedFileName = $"{Guid.NewGuid():N}{extension}";
            var relativeDirectory = Path.Combine("local-uploads", "future-letters", ownerUserId.ToString("N"));
            var absoluteDirectory = Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), relativeDirectory);
            Directory.CreateDirectory(absoluteDirectory);
            var absolutePath = Path.Combine(absoluteDirectory, storedFileName);
            await File.WriteAllBytesAsync(absolutePath, bytes, cancellationToken);

            var request = _httpContextAccessor.HttpContext?.Request;
            var baseUrl = request is null
                ? "http://localhost:5284"
                : $"{request.Scheme}://{request.Host}";
            var fileUrl = $"{baseUrl}/{relativeDirectory.Replace('\\', '/')}/{Uri.EscapeDataString(storedFileName)}";

            return new CloudUploadResult(
                file.FileName,
                $"{relativeDirectory.Replace('\\', '/')}/{storedFileName}",
                fileUrl,
                mimeType,
                file.Length,
                sha256Hash);
        }

        var formFields = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(_settings.UploadPreset))
        {
            formFields["upload_preset"] = _settings.UploadPreset;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.ApiSecret))
            {
                throw new InvalidOperationException("Cloudinary ApiKey and ApiSecret are required for signed uploads.");
            }

            var signatureParameters = new Dictionary<string, string>
            {
                ["folder"] = folder,
                ["public_id"] = publicId,
                ["timestamp"] = timestamp
            };

            var signature = CreateSignature(signatureParameters);
            formFields["folder"] = folder;
            formFields["public_id"] = publicId;
            formFields["api_key"] = _settings.ApiKey;
            formFields["timestamp"] = timestamp;
            formFields["signature"] = signature;
        }

        var resourceType = GetResourceType(mimeType);
        var endpoint = $"https://api.cloudinary.com/v1_1/{_settings.CloudName}/{resourceType}/upload";
        using var content = BuildMultipartContent(formFields, bytes, file.FileName, mimeType);
        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Cloudinary upload failed: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var secureUrl = root.GetProperty("secure_url").GetString();
        var storedName = root.TryGetProperty("public_id", out var publicIdElement)
            ? publicIdElement.GetString()
            : $"{folder}/{publicId}";

        if (string.IsNullOrWhiteSpace(secureUrl) || string.IsNullOrWhiteSpace(storedName))
        {
            throw new InvalidOperationException("Cloudinary upload response did not include a file URL.");
        }

        return new CloudUploadResult(
            file.FileName,
            storedName,
            secureUrl,
            mimeType,
            file.Length,
            sha256Hash);
    }

    private string CreateSignature(IReadOnlyDictionary<string, string> parameters)
    {
        var payload = string.Join("&", parameters
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));

        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(payload + _settings.ApiSecret));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ByteArrayContent BuildMultipartContent(
        IReadOnlyDictionary<string, string> fields,
        byte[] fileBytes,
        string fileName,
        string mimeType)
    {
        var boundary = $"----MemoriaBoundary{Guid.NewGuid():N}";
        using var stream = new MemoryStream();

        foreach (var field in fields)
        {
            WriteAscii(stream, $"--{boundary}\r\n");
            WriteAscii(stream, $"Content-Disposition: form-data; name=\"{field.Key}\"\r\n\r\n");
            WriteUtf8(stream, field.Value);
            WriteAscii(stream, "\r\n");
        }

        WriteAscii(stream, $"--{boundary}\r\n");
        WriteAscii(stream, $"Content-Disposition: form-data; name=\"file\"; filename=\"{EscapeHeaderValue(fileName)}\"\r\n");
        WriteAscii(stream, $"Content-Type: {mimeType}\r\n\r\n");
        stream.Write(fileBytes, 0, fileBytes.Length);
        WriteAscii(stream, "\r\n");
        WriteAscii(stream, $"--{boundary}--\r\n");

        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        return content;
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteUtf8(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string EscapeHeaderValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string GetResourceType(string mimeType)
    {
        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
            mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return "video";
        }

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return "image";
        }

        return "raw";
    }
}
