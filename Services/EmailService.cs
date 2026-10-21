using Azure;
using Azure.Communication.Email;
using backend.Services.Interfaces;

namespace backend.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailClient? _emailClient;
        private readonly string? _senderAddress;

        // Sin configuración de correo el servicio se construye igual (si no,
        // todo lo que dependa de él, como el inicio de sesión, dejaría de
        // funcionar); solo falla al intentar enviar.
        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            var connectionString = configuration["COMMUNICATION_SERVICES_CS"];
            _senderAddress = configuration["EmailSenderAddress"];

            if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(_senderAddress))
            {
                logger.LogWarning("El correo no está configurado (COMMUNICATION_SERVICES_CS / EmailSenderAddress): no se enviarán correos.");
                return;
            }

            _emailClient = new EmailClient(connectionString);
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            if (_emailClient == null || _senderAddress == null)
            {
                throw new InvalidOperationException("El servicio de correo no está configurado.");
            }

            try
            {
                var emailMessage = new EmailMessage(
                    senderAddress: _senderAddress,
                    recipientAddress: toEmail,
                    content: new EmailContent(subject)
                    {
                        Html = htmlContent
                    }
                );

                await _emailClient.SendAsync(WaitUntil.Completed, emailMessage);
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Error de Azure al enviar correo: {ex.Message}");
            }
        }
    }
}
