using Microsoft.AspNetCore.Http;

namespace backend.Services.Interfaces
{
    public interface IBlobService
    {
        // Valida el archivo (ValidadorArchivos) y lo guarda en el contenedor
        // privado de comprobantes; lanza ArgumentException si no es válido.
        // Devuelve la URL del blob, que sin firma no se puede leer.
        Task<string> SubirComprobanteAsync(IFormFile archivo);

        // ¿La URL apunta a un comprobante de nuestro almacenamiento?
        bool EsComprobantePropio(string? url);

        // URL de solo lectura con vencimiento (SAS) para que el administrador
        // abra un comprobante; null si no se puede firmar.
        string? GenerarUrlLectura(string? url, TimeSpan vigencia);
    }
}
