namespace MEMORIA_BE.Services;

public interface IEmailSender
{
    Task SendOtpAsync(string toEmail, string toName, string code, string purpose, CancellationToken cancellationToken);
}
