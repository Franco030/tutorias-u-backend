using backend.Data;
using backend.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace backend.Services
{
    // El correo y la contraseña coinciden, pero falta verificar el correo.
    public sealed class CorreoSinVerificarException()
        : UnauthorizedAccessException("Verifica tu correo antes de iniciar sesión. Si no te llegó el correo, puedes pedir otro.");

    /*
     * Recuperación de la cuenta: restablecer la contraseña y reenviar el correo
     * de verificación. Las solicitudes responden igual exista o no el correo (no
     * revelan quién tiene cuenta) y los correos salen por la cola, así que
     * tampoco hay diferencia de tiempo en la respuesta.
     */
    public sealed class RecuperacionCuenta
    {
        public const string AsuntoVerificacion = "Verifica tu correo en TutoriasU";
        public static readonly TimeSpan VigenciaVerificacion = TimeSpan.FromHours(24);
        public static readonly TimeSpan VigenciaRestablecimiento = TimeSpan.FromHours(1);

        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IColaCorreos _cola;
        private readonly ILogger<RecuperacionCuenta> _logger;

        public RecuperacionCuenta(
            AppDbContext context,
            IConfiguration configuration,
            IColaCorreos cola,
            ILogger<RecuperacionCuenta> logger)
        {
            _context = context;
            _configuration = configuration;
            _cola = cola;
            _logger = logger;
        }

        private static string Normalizar(string email) => email.Trim();

        private static string UrlFrontend(IConfiguration configuration) =>
            (configuration["FrontendUrl"] ?? throw new InvalidOperationException("La variable FrontendUrl no esta configurada")).TrimEnd('/');

        // HTML del correo de verificación (lo usa también el registro, que lo envía en el momento).
        public static string CorreoDeVerificacion(IConfiguration configuration, string nombre, string email)
        {
            var token = TokensCuenta.Crear(configuration, TokensCuenta.Verificacion, email, VigenciaVerificacion);
            return PlantillasCorreo.Renderizar("VerificarCorreo", new Dictionary<string, string?>
            {
                ["Titulo"] = "Verifica tu correo",
                ["Preencabezado"] = "Confirma tu correo para empezar a usar TutoriasU.",
                ["Nombre"] = nombre,
                ["UrlBoton"] = $"{UrlFrontend(configuration)}/verificar-correo?token={Uri.EscapeDataString(token)}"
            });
        }

        public async Task SolicitarRestablecimientoAsync(string email, CancellationToken cancellationToken = default)
        {
            var correo = Normalizar(email);
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == correo, cancellationToken);
            if (usuario == null)
            {
                return;
            }

            // Cuenta de Google o Microsoft: no tiene contraseña que restablecer.
            if (string.IsNullOrEmpty(usuario.PasswordHash))
            {
                Encolar(usuario.Email, "Tu cuenta de TutoriasU no usa contraseña", "CuentaExterna", new()
                {
                    ["Titulo"] = "Tu cuenta no usa contraseña",
                    ["Preencabezado"] = "Entra a TutoriasU con el botón de tu cuenta de Google o Microsoft.",
                    ["Nombre"] = usuario.Nombre,
                    ["Proveedor"] = usuario.AuthProvider,
                    ["UrlBoton"] = UrlFrontend(_configuration)
                });
                return;
            }

            var token = TokensCuenta.Crear(
                _configuration,
                TokensCuenta.Restablecimiento,
                usuario.Email,
                VigenciaRestablecimiento,
                TokensCuenta.SelloDeContrasena(usuario.PasswordHash));

            Encolar(usuario.Email, "Restablece tu contraseña de TutoriasU", "RestablecerContrasena", new()
            {
                ["Titulo"] = "Restablece tu contraseña",
                ["Preencabezado"] = "Usa este enlace en la próxima hora para elegir una contraseña nueva.",
                ["Nombre"] = usuario.Nombre,
                ["UrlBoton"] = $"{UrlFrontend(_configuration)}/restablecer-contrasena?token={Uri.EscapeDataString(token)}"
            });
        }

        // Cambia la contraseña con el enlace del correo. El enlace sirve una sola
        // vez: su sello deja de coincidir en cuanto la contraseña cambia.
        public async Task RestablecerAsync(string token, string nuevaContrasena, CancellationToken cancellationToken = default)
        {
            const string invalido = "El enlace no es válido o ya venció. Pide uno nuevo desde «¿Olvidaste tu contraseña?».";

            TokensCuenta.Contenido contenido;
            try
            {
                contenido = TokensCuenta.Validar(_configuration, TokensCuenta.Restablecimiento, token);
            }
            catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
            {
                throw new InvalidOperationException(invalido);
            }

            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email == contenido.Email, cancellationToken);
            if (usuario == null ||
                string.IsNullOrEmpty(usuario.PasswordHash) ||
                contenido.Sello != TokensCuenta.SelloDeContrasena(usuario.PasswordHash))
            {
                throw new InvalidOperationException(invalido);
            }

            usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(nuevaContrasena);
            // El enlace llegó a su correo: queda verificado.
            usuario.IsEmailVerified = true;
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task ReenviarVerificacionAsync(string email, CancellationToken cancellationToken = default)
        {
            var correo = Normalizar(email);
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == correo, cancellationToken);
            if (usuario == null || usuario.IsEmailVerified || string.IsNullOrEmpty(usuario.PasswordHash))
            {
                return;
            }

            try
            {
                _cola.Encolar(new CorreoPendiente(
                    usuario.Email,
                    AsuntoVerificacion,
                    CorreoDeVerificacion(_configuration, usuario.Nombre, usuario.Email)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo preparar el correo de verificación.");
            }
        }

        private void Encolar(string para, string asunto, string plantilla, Dictionary<string, string?> valores)
        {
            try
            {
                _cola.Encolar(new CorreoPendiente(para, asunto, PlantillasCorreo.Renderizar(plantilla, valores)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo preparar el correo \"{Asunto}\".", asunto);
            }
        }
    }
}
