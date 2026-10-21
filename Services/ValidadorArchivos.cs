namespace backend.Services
{
    /*
     * Comprobantes que suben los aspirantes a tutor: PDF o imagen de hasta 5 MB.
     * El tipo se decide por la firma real del archivo (sus primeros bytes), no
     * por la extensión ni por el Content-Type que manda el cliente; así no se
     * puede subir, por ejemplo, un HTML disfrazado de PDF.
     */
    public static class ValidadorArchivos
    {
        public const long TamanoMaximo = 5 * 1024 * 1024;

        private static readonly Dictionary<string, string> TiposPermitidos = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
        };

        // Devuelve la extensión normalizada y el Content-Type con el que se guarda.
        public static (string Extension, string ContentType) Validar(IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
                throw new ArgumentException("El archivo está vacío.");

            if (archivo.Length > TamanoMaximo)
                throw new ArgumentException("El archivo supera los 5 MB.");

            var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();
            if (!TiposPermitidos.TryGetValue(extension, out var contentType))
                throw new ArgumentException("Solo se aceptan archivos PDF, PNG, JPG o WEBP.");

            Span<byte> cabecera = stackalloc byte[12];
            using (var stream = archivo.OpenReadStream())
            {
                var leidos = stream.Read(cabecera);
                cabecera = cabecera[..leidos];
            }

            if (!CoincideFirma(contentType, cabecera))
                throw new ArgumentException("El contenido del archivo no corresponde a su tipo.");

            return (extension == ".jpeg" ? ".jpg" : extension, contentType);
        }

        private static bool CoincideFirma(string contentType, ReadOnlySpan<byte> b) => contentType switch
        {
            "application/pdf" => b.StartsWith("%PDF-"u8),
            "image/png" => b.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/jpeg" => b.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
            "image/webp" => b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8),
            _ => false,
        };
    }
}
