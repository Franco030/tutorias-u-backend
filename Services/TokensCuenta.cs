using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace backend.Services
{
    /*
     * Tokens de los enlaces que llegan por correo (verificar el correo,
     * restablecer la contraseña). Cada propósito tiene su propia audiencia: el
     * JwtBearer de la API (que espera Jwt:Audience) no los acepta como sesión,
     * y uno de verificación no sirve para restablecer ni al revés.
     */
    public static class TokensCuenta
    {
        public const string Verificacion = "verificar-correo";
        public const string Restablecimiento = "restablecer-contrasena";

        private const string ClaimProposito = "proposito";
        private const string ClaimSello = "sello";

        public sealed record Contenido(string Email, string? Sello);

        // La audiencia de verificación conserva su nombre: los enlaces ya
        // enviados siguen funcionando.
        private static string Audiencia(IConfiguration configuration, string proposito) =>
            $"{configuration["Jwt:Audience"]}.{(proposito == Verificacion ? "verificacion" : proposito)}";

        private static SymmetricSecurityKey Llave(IConfiguration configuration) =>
            new(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("La clave 'Jwt:Key' no esta configurada.")));

        public static string Crear(IConfiguration configuration, string proposito, string email, TimeSpan vigencia, string? sello = null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Email, email),
                new(ClaimProposito, proposito)
            };
            if (sello != null)
            {
                claims.Add(new Claim(ClaimSello, sello));
            }

            var token = new JwtSecurityToken(
                issuer: configuration["Jwt:Issuer"],
                audience: Audiencia(configuration, proposito),
                claims: claims,
                expires: DateTime.UtcNow.Add(vigencia),
                signingCredentials: new SigningCredentials(Llave(configuration), SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // Lanza SecurityTokenException si el token no es válido, venció o es de otro propósito.
        public static Contenido Validar(IConfiguration configuration, string proposito, string token)
        {
            new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = Llave(configuration),
                ValidateIssuer = true,
                ValidIssuer = configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = Audiencia(configuration, proposito),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out var validado);

            var jwt = (JwtSecurityToken)validado;
            if (jwt.Claims.FirstOrDefault(c => c.Type == ClaimProposito)?.Value != proposito)
            {
                throw new SecurityTokenException("El token es de otro propósito.");
            }

            var email = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
                ?? throw new SecurityTokenException("El token no trae correo.");
            return new Contenido(email, jwt.Claims.FirstOrDefault(c => c.Type == ClaimSello)?.Value);
        }

        // Sello de la contraseña actual: el enlace de restablecimiento lo lleva y
        // deja de servir en cuanto la contraseña cambia (un solo uso, sin guardar
        // nada en la base). No expone el hash: es un resumen SHA-256 recortado.
        public static string SelloDeContrasena(string? passwordHash) =>
            Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash ?? string.Empty)))[..22];
    }
}
