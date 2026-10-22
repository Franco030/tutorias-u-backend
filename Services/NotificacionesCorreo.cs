using backend.Services.Interfaces;

namespace backend.Services
{
    public sealed class NotificacionesCorreo : INotificacionesCorreo
    {
        private static readonly string[] Dias =
            ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

        private static readonly string[] Meses =
            ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto",
             "septiembre", "octubre", "noviembre", "diciembre"];

        private readonly IColaCorreos _cola;
        private readonly ILogger<NotificacionesCorreo> _logger;
        private readonly string _frontendUrl;

        public NotificacionesCorreo(
            IColaCorreos cola,
            IConfiguration configuration,
            ILogger<NotificacionesCorreo> logger)
        {
            _cola = cola;
            _logger = logger;
            _frontendUrl = (configuration["FrontendUrl"] ?? string.Empty).TrimEnd('/');
        }

        public void CitaSolicitada(DatosCitaCorreo cita) => Enviar(
            cita.TutorEmail,
            $"Nueva solicitud de tutoría de {cita.EstudianteNombre}",
            "CitaAgendada",
            new()
            {
                ["Titulo"] = "Nueva solicitud de tutoría",
                ["Preencabezado"] = $"{cita.EstudianteNombre} quiere una sesión de {cita.Materia} el {FormatearFecha(cita.FechaHoraInicio)}.",
                ["TutorNombre"] = cita.TutorNombre,
                ["EstudianteNombre"] = cita.EstudianteNombre,
                ["Materia"] = cita.Materia,
                ["Fecha"] = FormatearFecha(cita.FechaHoraInicio),
                ["Tema"] = Limpiar(cita.Tema),
                ["UrlBoton"] = Url("/tutor/mis-tutorias")
            });

        public void CitaAceptada(DatosCitaCorreo cita, string? linkReunion, string? notas)
        {
            var link = EsEnlaceSeguro(linkReunion) ? linkReunion!.Trim() : null;
            Enviar(
                cita.EstudianteEmail,
                $"Tutoría confirmada: {cita.Materia} con {cita.TutorNombre}",
                "CitaAceptada",
                new()
                {
                    ["Titulo"] = "Tutoría confirmada",
                    ["Preencabezado"] = $"{cita.TutorNombre} confirmó tu sesión de {cita.Materia} el {FormatearFecha(cita.FechaHoraInicio)}.",
                    ["TutorNombre"] = cita.TutorNombre,
                    ["EstudianteNombre"] = cita.EstudianteNombre,
                    ["Materia"] = cita.Materia,
                    ["Fecha"] = FormatearFecha(cita.FechaHoraInicio),
                    ["Notas"] = notas?.Trim(),
                    ["LinkReunion"] = link,
                    ["SinLink"] = link == null ? "1" : null,
                    ["UrlBoton"] = Url("/mis-tutorias")
                });
        }

        public void CitaRechazada(DatosCitaCorreo cita, string? motivo) => Enviar(
            cita.EstudianteEmail,
            $"Tu solicitud de {cita.Materia} fue rechazada",
            "CitaCancelada",
            new()
            {
                ["Titulo"] = "Solicitud rechazada",
                ["Preencabezado"] = $"{cita.TutorNombre} no podrá darte la sesión de {cita.Materia}.",
                ["Encabezado"] = "Tu solicitud de tutoría fue rechazada",
                ["DestinatarioNombre"] = cita.EstudianteNombre,
                ["Descripcion"] = $"{cita.TutorNombre} no podrá darte esta sesión. Puedes buscar otro horario o a otro tutor.",
                ["RolContraparte"] = "Tutor",
                ["ContraparteNombre"] = cita.TutorNombre,
                ["Materia"] = cita.Materia,
                ["Fecha"] = FormatearFecha(cita.FechaHoraInicio),
                ["Motivo"] = Limpiar(motivo),
                ["SinMotivo"] = Limpiar(motivo) == null ? "1" : null,
                ["TextoBoton"] = "Buscar tutores",
                ["UrlBoton"] = Url("/tutores")
            });

        public void CitaCancelada(DatosCitaCorreo cita, bool canceladaPorTutor, string? motivo)
        {
            var (destinatarioEmail, destinatarioNombre, contraparte, rolContraparte, urlBoton) = canceladaPorTutor
                ? (cita.EstudianteEmail, cita.EstudianteNombre, cita.TutorNombre, "Tutor", Url("/mis-tutorias"))
                : (cita.TutorEmail, cita.TutorNombre, cita.EstudianteNombre, "Estudiante", Url("/tutor/mis-tutorias"));

            Enviar(
                destinatarioEmail,
                $"Tutoría cancelada: {cita.Materia} del {FormatearFecha(cita.FechaHoraInicio)}",
                "CitaCancelada",
                new()
                {
                    ["Titulo"] = "Tutoría cancelada",
                    ["Preencabezado"] = $"{contraparte} canceló la sesión de {cita.Materia}.",
                    ["Encabezado"] = "Una tutoría fue cancelada",
                    ["DestinatarioNombre"] = destinatarioNombre,
                    ["Descripcion"] = $"{contraparte} canceló la sesión. El horario quedó libre.",
                    ["RolContraparte"] = rolContraparte,
                    ["ContraparteNombre"] = contraparte,
                    ["Materia"] = cita.Materia,
                    ["Fecha"] = FormatearFecha(cita.FechaHoraInicio),
                    ["Motivo"] = Limpiar(motivo),
                    ["SinMotivo"] = Limpiar(motivo) == null ? "1" : null,
                    ["TextoBoton"] = "Ver mis tutorías",
                    ["UrlBoton"] = urlBoton
                });
        }

        public void TutorAprobado(string nombre, string email) => Enviar(
            email,
            "Tu postulación como tutor fue aprobada",
            "TutorAprobado",
            new()
            {
                ["Titulo"] = "Postulación aprobada",
                ["Preencabezado"] = "Ya puedes dar tutorías en TutoriasU.",
                ["Nombre"] = nombre,
                ["UrlBoton"] = Url("/")
            });

        public void TutorRechazado(string nombre, string email, string? motivo) => Enviar(
            email,
            "Resultado de tu postulación como tutor",
            "TutorRechazado",
            new()
            {
                ["Titulo"] = "Resultado de tu postulación",
                ["Preencabezado"] = "Revisamos tu postulación como tutor.",
                ["Nombre"] = nombre,
                ["Motivo"] = Limpiar(motivo),
                ["UrlBoton"] = Url("/estado-solicitud")
            });

        // Las horas de las citas se guardan como la hora local del horario del
        // tutor (sin conversión de zona), así que se muestran tal cual.
        public static string FormatearFecha(DateTime fecha) =>
            $"{Dias[(int)fecha.DayOfWeek]} {fecha.Day} de {Meses[fecha.Month - 1]} de {fecha.Year}, {fecha:HH:mm} h";

        private void Enviar(string para, string asunto, string plantilla, Dictionary<string, string?> valores)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(para))
                {
                    _logger.LogWarning("Correo \"{Asunto}\" sin destinatario; no se envía.", asunto);
                    return;
                }

                _cola.Encolar(new CorreoPendiente(para, asunto, PlantillasCorreo.Renderizar(plantilla, valores)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo preparar el correo \"{Asunto}\".", asunto);
            }
        }

        private string Url(string ruta) => $"{_frontendUrl}{ruta}";

        private static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        // Solo enlaces http(s): el tutor escribe el enlace y no debe poder inyectar otro esquema.
        private static bool EsEnlaceSeguro(string? enlace) =>
            Uri.TryCreate(enlace?.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
