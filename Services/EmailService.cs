using Azure;
using Azure.Communication.Email;
using backend.Services.Interfaces;

namespace backend.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly EmailClient _emailClient;
        private readonly string _senderAddress;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;

            string connectionString = _configuration["COMMUNICATION_SERVICES_CS"] ?? throw new InvalidOperationException("Falta Clave de comunicacion en appsettings");

            _senderAddress = _configuration["EmailSenderAddress"] ?? throw new InvalidOperationException("Falta EmailSender");

            _emailClient = new EmailClient(connectionString);
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
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
