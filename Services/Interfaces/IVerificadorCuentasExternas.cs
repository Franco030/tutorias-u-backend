namespace backend.Services.Interfaces
{
    // Datos de una cuenta de Google o Microsoft cuyo token ya se comprobó.
    public record CuentaExterna(string Email, string Nombre, string? FotoUrl, string? ProveedorId);

    // Comprueba que el token lo emitió el proveedor para esta app antes de
    // confiar en el correo que trae. Si no es válido lanza UnauthorizedAccessException.
    public interface IVerificadorCuentasExternas
    {
        Task<CuentaExterna> VerificarGoogleAsync(string token, CancellationToken cancellationToken = default);

        Task<CuentaExterna> VerificarMicrosoftAsync(string idToken, CancellationToken cancellationToken = default);
    }
}
