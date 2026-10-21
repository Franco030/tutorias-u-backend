using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using backend.Services.Interfaces;

namespace backend.Services
{
    /*
     * Comprobantes de los aspirantes (kárdex, constancias): datos personales.
     * Se guardan en un contenedor privado y solo se leen con una URL firmada
     * (SAS) de corta duración que genera el backend para el administrador.
     */
    public class BlobService : IBlobService
    {
        // El contenedor lo decide el servidor, no el cliente.
        public const string ContenedorComprobantes = "comprobantes";

        // Contenedor anterior (público) de las solicitudes ya enviadas: sus
        // comprobantes se siguen aceptando y también se abren con SAS.
        public const string ContenedorAnterior = "uploads";

        private readonly BlobServiceClient? _cliente;
        private readonly ILogger<BlobService> _logger;

        // Sin configuración el servicio se construye igual y solo falla al usarse.
        public BlobService(IConfiguration configuration, ILogger<BlobService> logger)
        {
            _logger = logger;
            var connectionString = configuration["AZURE_STORAGE_CONNECTION_STRING"];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                _cliente = new BlobServiceClient(connectionString);
            }
        }

        private BlobServiceClient Cliente => _cliente
            ?? throw new InvalidOperationException("El almacenamiento de archivos no está configurado.");

        public async Task<string> SubirComprobanteAsync(IFormFile archivo)
        {
            var (extension, contentType) = ValidadorArchivos.Validar(archivo);

            var contenedor = Cliente.GetBlobContainerClient(ContenedorComprobantes);
            await contenedor.CreateIfNotExistsAsync(PublicAccessType.None);

            var blobClient = contenedor.GetBlobClient($"{Guid.NewGuid()}{extension}");

            using (var stream = archivo.OpenReadStream())
            {
                // Content-Type según la firma validada, nunca el que manda el cliente.
                await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType });
            }

            return blobClient.Uri.ToString();
        }

        public bool EsComprobantePropio(string? url) => UbicarBlob(url) != null;

        public string? GenerarUrlLectura(string? url, TimeSpan vigencia)
        {
            var ubicacion = UbicarBlob(url);
            if (ubicacion == null)
            {
                return null;
            }

            var blobClient = Cliente.GetBlobContainerClient(ubicacion.Value.Contenedor).GetBlobClient(ubicacion.Value.Blob);
            if (!blobClient.CanGenerateSasUri)
            {
                _logger.LogWarning("La cadena de conexión del almacenamiento no permite firmar URLs (SAS).");
                return null;
            }

            return blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(vigencia)).ToString();
        }

        // Contenedor y nombre del blob si la URL es de nuestra cuenta y de un
        // contenedor de comprobantes; null en cualquier otro caso.
        private (string Contenedor, string Blob)? UbicarBlob(string? url)
        {
            if (_cliente == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var cuenta = _cliente.Uri;
            if (uri.Scheme != cuenta.Scheme || !string.Equals(uri.Authority, cuenta.Authority, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Ruta relativa a la de la cuenta (en Azurite incluye el nombre de la cuenta).
            var ruta = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            var prefijo = cuenta.AbsolutePath.Trim('/');
            if (prefijo.Length > 0)
            {
                if (!ruta.StartsWith(prefijo + "/", StringComparison.Ordinal))
                {
                    return null;
                }
                ruta = ruta[(prefijo.Length + 1)..];
            }

            var partes = ruta.Split('/', 2);
            if (partes.Length != 2 || string.IsNullOrEmpty(partes[1]) || partes[1].Contains('/'))
            {
                return null;
            }

            return partes[0] is ContenedorComprobantes or ContenedorAnterior
                ? (partes[0], partes[1])
                : null;
        }
    }
}
