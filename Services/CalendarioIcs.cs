using System.Globalization;
using System.Text;
using backend.Services.Interfaces;

namespace backend.Services
{
    /*
     * Archivo .ics (RFC 5545) de una tutoría para agregarla al calendario
     * (Outlook, Google, Apple) desde el correo. El UID es estable por cita, así
     * que abrir el archivo de otro correo de la misma cita no la duplica.
     */
    public static class CalendarioIcs
    {
        public const string NombreArchivo = "tutoria.ics";
        public const string TipoContenido = "text/calendar";

        public static AdjuntoCorreo Evento(
            int citaId,
            string titulo,
            string descripcion,
            DateTime inicioUtc,
            TimeSpan duracion,
            string? enlace,
            DateTime ahoraUtc)
        {
            var lineas = new List<string>
            {
                "BEGIN:VCALENDAR",
                "VERSION:2.0",
                "PRODID:-//TutoriasU//Tutorias//ES",
                "CALSCALE:GREGORIAN",
                "METHOD:PUBLISH",
                "BEGIN:VEVENT",
                $"UID:cita-{citaId}@tutoriasu",
                $"DTSTAMP:{Utc(ahoraUtc)}",
                $"DTSTART:{Utc(inicioUtc)}",
                $"DTEND:{Utc(inicioUtc + duracion)}",
                $"SUMMARY:{Escapar(titulo)}",
                $"DESCRIPTION:{Escapar(descripcion)}",
                $"LOCATION:{Escapar(enlace ?? "En línea")}",
            };

            if (enlace != null)
            {
                lineas.Add($"URL:{enlace}");
            }

            lineas.AddRange(
            [
                "STATUS:CONFIRMED",
                "BEGIN:VALARM",
                "ACTION:DISPLAY",
                "TRIGGER:-PT15M",
                $"DESCRIPTION:{Escapar(titulo)}",
                "END:VALARM",
                "END:VEVENT",
                "END:VCALENDAR",
            ]);

            var texto = string.Concat(lineas.Select(linea => Plegar(linea) + "\r\n"));
            return new AdjuntoCorreo(NombreArchivo, TipoContenido, Encoding.UTF8.GetBytes(texto));
        }

        private static string Utc(DateTime fecha) =>
            fecha.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        // Texto de una propiedad: \ ; , y saltos de línea van escapados.
        public static string Escapar(string texto) => texto
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .ReplaceLineEndings("\n")
            .Replace("\n", "\\n");

        // Líneas de máximo 75 bytes; las siguientes empiezan con un espacio. No se
        // corta un carácter de varios bytes (acentos) a la mitad.
        public static string Plegar(string linea)
        {
            const int limite = 75;
            if (Encoding.UTF8.GetByteCount(linea) <= limite)
            {
                return linea;
            }

            var resultado = new StringBuilder();
            var bytesEnLinea = 0;
            var maximo = limite;
            var elementos = StringInfo.GetTextElementEnumerator(linea);
            while (elementos.MoveNext())
            {
                var elemento = elementos.GetTextElement();
                var bytes = Encoding.UTF8.GetByteCount(elemento);
                if (bytesEnLinea + bytes > maximo)
                {
                    resultado.Append("\r\n ");
                    bytesEnLinea = 0;
                    // El espacio inicial cuenta dentro de los 75 bytes.
                    maximo = limite - 1;
                }

                resultado.Append(elemento);
                bytesEnLinea += bytes;
            }

            return resultado.ToString();
        }
    }
}
