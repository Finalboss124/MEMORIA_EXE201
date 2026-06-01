namespace MEMORIA_BE.Services;

public interface IFutureLetterCrypto
{
    string? Encrypt(string? plainText);

    string? Decrypt(string? protectedText);
}
