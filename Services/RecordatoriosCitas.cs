using backend.Data;
using backend.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace backend.Services
{
    /*
     * Recordatorios por correo de las citas aceptadas, al estudiante y al tutor:
     *  - el del día: entre 24 h y 3 h antes (si la cita se aceptó con menos de
     *    3 h, el correo de confirmación ya hace de aviso);
     *  - el de una hora: dentro de la hora previa.
     * Cada envío queda marcado en la cita (RecordatorioDiaEnviado /
     * RecordatorioHoraEnviado) antes de encolarse: un recordatorio nunca sale
     * dos veces, aunque el servidor se reinicie.
     */
    public sealed class RecordatoriosCitas
    {
        public static readonly TimeSpan VentanaDia = TimeSpan.FromHours(24);
        public static readonly TimeSpan MinimoDia = TimeSpan.FromHours(3);
        public static readonly TimeSpan VentanaHora = TimeSpan.FromHours(1);

        private readonly AppDbContext _context;
        private readonly INotificacionesCorreo _notificaciones;
        private readonly IHoraPlataforma _hora;

        public RecordatoriosCitas(AppDbContext context, INotificacionesCorreo notificaciones, IHoraPlataforma hora)
        {
            _context = context;
            _notificaciones = notificaciones;
            _hora = hora;
        }

        // Envía los recordatorios que tocan ahora y regresa cuántos encoló.
        public async Task<int> EnviarPendientesAsync(CancellationToken cancellationToken = default)
        {
            var ahora = _hora.Ahora;
            var limite = ahora + VentanaDia;

            var citas = await _context.Citas
                .Include(cita => cita.Estudiante)
                .Include(cita => cita.Tutor)
                .Include(cita => cita.Materia)
                .Where(cita =>
                    cita.Estado == "Aceptada" &&
                    cita.FechaHoraInicio > ahora &&
                    cita.FechaHoraInicio <= limite &&
                    (cita.RecordatorioDiaEnviado == null || cita.RecordatorioHoraEnviado == null))
                .ToListAsync(cancellationToken);

            var porEnviar = new List<(DatosCitaCorreo Datos, string? Link, bool FaltaUnaHora)>();
            foreach (var cita in citas)
            {
                var falta = cita.FechaHoraInicio - ahora;
                var faltaUnaHora = falta <= VentanaHora;

                if (faltaUnaHora && cita.RecordatorioHoraEnviado == null)
                {
                    cita.RecordatorioHoraEnviado = ahora;
                }
                else if (!faltaUnaHora && falta > MinimoDia && cita.RecordatorioDiaEnviado == null)
                {
                    cita.RecordatorioDiaEnviado = ahora;
                }
                else
                {
                    continue;
                }

                porEnviar.Add((
                    new DatosCitaCorreo(
                        cita.Materia.Nombre,
                        cita.FechaHoraInicio,
                        cita.Estudiante.Nombre,
                        cita.Estudiante.Email,
                        cita.Tutor.Nombre,
                        cita.Tutor.Email,
                        cita.Tema,
                        cita.Id),
                    cita.LinkReunion,
                    faltaUnaHora));
            }

            if (porEnviar.Count == 0)
            {
                return 0;
            }

            await _context.SaveChangesAsync(cancellationToken);

            foreach (var (datos, link, faltaUnaHora) in porEnviar)
            {
                _notificaciones.RecordatorioCita(datos, link, faltaUnaHora);
            }

            return porEnviar.Count;
        }
    }

    /*
     * Revisa los recordatorios cada pocos minutos ("Recordatorios:IntervaloMinutos",
     * 5 por defecto). Se desactiva con "Recordatorios:Activos" = false (pruebas).
     */
    public sealed class ServicioRecordatorios : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ServicioRecordatorios> _logger;
        private readonly bool _activo;
        private readonly TimeSpan _intervalo;

        public ServicioRecordatorios(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ServicioRecordatorios> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _activo = configuration.GetValue("Recordatorios:Activos", true);
            _intervalo = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Recordatorios:IntervaloMinutos", 5)));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_activo)
            {
                return;
            }

            using var temporizador = new PeriodicTimer(_intervalo);
            do
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var recordatorios = scope.ServiceProvider.GetRequiredService<RecordatoriosCitas>();
                    var enviados = await recordatorios.EnviarPendientesAsync(stoppingToken);
                    if (enviados > 0)
                    {
                        _logger.LogInformation("Recordatorios de citas encolados: {Cantidad}.", enviados);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falló la revisión de recordatorios de citas.");
                }
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
    }
}
