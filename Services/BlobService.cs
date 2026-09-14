using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using backend.Services.Interfaces;

namespace backend.Services
{
    public class BlobService : IBlobService
    {
        private readonly string _connectionString;

        public BlobService(IConfiguration configuration)
        {
            _connectionString = configuration["AZURE_STORAGE_CONNECTION_STRING"]
                ?? throw new InvalidOperationException("La conexion al Azure Blob Storage no esta configurada correctamente.");
        }

        public async Task<string> SubirArchivoAsync(IFormFile archivo, string nombreContenedor)
        {
            if (archivo == null || archivo.Length == 0)
                throw new ArgumentException("El archivo está vacío o es nulo.");

            var blobServiceClient = new BlobServiceClient(_connectionString);

            var blobContainerClient = blobServiceClient.GetBlobContainerClient(nombreContenedor.ToLower());
            await blobContainerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var extension = Path.GetExtension(archivo.FileName);
            var nombreArchivoUnico = $"{Guid.NewGuid()}{extension}";

            var blobClient = blobContainerClient.GetBlobClient(nombreArchivoUnico);

            using (var stream = archivo.OpenReadStream())
            {
                await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = archivo.ContentType });
            }

            return blobClient.Uri.ToString();
        }
    }
}
