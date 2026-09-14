using Microsoft.AspNetCore.Http;

namespace backend.Services.Interfaces
{
    public interface IBlobService
    {
        Task<String> SubirArchivoAsync(IFormFile archivo, string nombreContenedor);
    }
}
