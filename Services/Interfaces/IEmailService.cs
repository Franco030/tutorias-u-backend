namespace backend.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlContent);

        Task SendEmailAsync(string toEmail, string subject, string htmlContent, IReadOnlyList<AdjuntoCorreo> adjuntos);
    }
}
