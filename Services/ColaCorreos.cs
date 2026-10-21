using System.Threading.Channels;
using backend.Services.Interfaces;

namespace backend.Services
{
    /*
     * Cola de correos en memoria. Los controladores encolan después de guardar en
     * la base de datos y responden sin esperar a Azure; este servicio envía cada
     * correo en segundo plano y solo registra las fallas, así un problema con el
     * servicio de correo nunca revierte ni retrasa la operación del usuario.
     */
    public sealed class ColaCorreos : BackgroundService, IColaCorreos
    {
        private readonly Channel<CorreoPendiente> _canal = Channel.CreateUnbounded<CorreoPendiente>(
            new UnboundedChannelOptions { SingleReader = true });
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ColaCorreos> _logger;

        public ColaCorreos(IServiceScopeFactory scopeFactory, ILogger<ColaCorreos> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void Encolar(CorreoPendiente correo)
        {
            if (!_canal.Writer.TryWrite(correo))
            {
                _logger.LogWarning("No se pudo encolar el correo \"{Asunto}\".", correo.Asunto);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var correo in _canal.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    await emailService.SendEmailAsync(correo.Para, correo.Asunto, correo.Html);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falló el envío del correo \"{Asunto}\".", correo.Asunto);
                }
            }
        }
    }
}
