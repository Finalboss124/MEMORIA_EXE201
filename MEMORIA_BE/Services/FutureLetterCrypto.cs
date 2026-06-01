using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MEMORIA_BE.Configurations;

namespace MEMORIA_BE.Services;

public sealed class FutureLetterCrypto : IFutureLetterCrypto
{
    private const string Prefix = "v1:";
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly byte[] _key;

    public FutureLetterCrypto(
        IOptions<FutureLetterEncryptionSettings> encryptionSettings,
        IOptions<JwtSettings> jwtSettings)
    {
        var configuredKey = encryptionSettings.Value.Key;
        var keyMaterial = string.IsNullOrWhiteSpace(configuredKey)
            ? jwtSettings.Value.Key
            : configuredKey;

        if (string.IsNullOrWhiteSpace(keyMaterial))
        {
            throw new InvalidOperationException("Future letter encryption key is missing.");
        }

        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
    }

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return null;
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(_key, TagSizeBytes);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var payload = new byte[NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, payload, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(cipherBytes, 0, payload, NonceSizeBytes + TagSizeBytes, cipherBytes.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    public string? Decrypt(string? protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return null;
        }

        if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return protectedText;
        }

        try
        {
            var payload = Convert.FromBase64String(protectedText[Prefix.Length..]);
            if (payload.Length < NonceSizeBytes + TagSizeBytes)
            {
                return "[Unable to decrypt this message. The encryption payload is invalid.]";
            }

            var nonce = payload[..NonceSizeBytes];
            var tag = payload[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
            var cipherBytes = payload[(NonceSizeBytes + TagSizeBytes)..];
            var plainBytes = new byte[cipherBytes.Length];

            using var aes = new AesGcm(_key, TagSizeBytes);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (FormatException)
        {
            return "[Unable to decrypt this message. The encrypted payload is not valid base64.]";
        }
        catch (CryptographicException)
        {
            return "[Unable to decrypt this message. The encryption key may have changed.]";
        }
    }
}
