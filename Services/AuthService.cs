using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using backend.Data;
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Models;
using backend.Services.Interfaces;
using backend.Enums;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;

namespace backend.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;

        public AuthService(AppDbContext context, IConfiguration configuration, IEmailService emailService)
        {
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
        }

        public async Task<AuthResponseDto> LoginWithGoogleAsync(ExternalLoginDto dto)
        {
            string email;
            string nombre;
            string? fotoUrl;
            string subject;

            if (dto.IdToken.StartsWith("ya29."))
            {
                using var client = new HttpClient();
                var response = await client.GetAsync($"https://www.googleapis.com/oauth2/v3/userinfo?access_token={dto.IdToken}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new UnauthorizedAccessException("La Sesion de Google no es valida o ha expirado");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                email = root.GetProperty("email").GetString() ?? throw new Exception("Google no devolvio el email");
                nombre = root.GetProperty("name").GetString() ?? email;
                fotoUrl = root.TryGetProperty("picture", out var pic) ? pic.GetString() : null;
                subject = root.GetProperty("sub").GetString() ?? "";
            }
            else
            {
                try
                {
                    var payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken);
                    email = payload.Email;
                    nombre = payload.Name ?? payload.Email;
                    fotoUrl = payload.Picture;
                    subject = payload.Subject;
                }
                catch (Exception)
                {
                    throw new UnauthorizedAccessException("La Sesion de Google no es valida o ha expirado");
                }
            }

            var usuario = await FindOrCreateUserAsync(
                email: email,
                nombre: nombre,
                fotoUrl: fotoUrl,
                authProvider: "Google",
                providerId: subject
            );

            var token = GenerateJwtToken(usuario);

            return new AuthResponseDto
            {
                Token = token,
                Id = usuario.Id,
                Email = usuario.Email,
                Nombre = usuario.Nombre,
                Rol = usuario.Rol,
                FotoUrl = fotoUrl,
                OnboardingCompleto = usuario.OnboardingCompleto,
                EstadoAprobacion = ((EstadoAprobacion)usuario.EstadoAprobacionId).ToString()
            };
        }

        public async Task<AuthResponseDto> LoginWithMicrosoftAsync(ExternalLoginDto dto)
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(dto.IdToken))
            {
                throw new UnauthorizedAccessException("El token de Microsoft no tiene un formato válido.");
            }

            var jwt = handler.ReadJwtToken(dto.IdToken);

            var email = jwt.Claims.FirstOrDefault(c => c.Type == "preferred_username" || c.Type == "email" || c.Type == ClaimTypes.Email)?.Value;
            var nombre = jwt.Claims.FirstOrDefault(c => c.Type == "name" || c.Type == ClaimTypes.Name)?.Value;
            var providerId = jwt.Claims.FirstOrDefault(c => c.Type == "oid" || c.Type == "sub")?.Value;

            if (string.IsNullOrEmpty(email))
            {
                throw new UnauthorizedAccessException("No se pudo obtener el correo de la cuenta de Microsoft.");
            }

            var usuario = await FindOrCreateUserAsync(
                email: email,
                nombre: nombre ?? email,
                fotoUrl: null,
                authProvider: "Microsoft",
                providerId: providerId
            );

            var token = GenerateJwtToken(usuario);

            return new AuthResponseDto
            {
                Token = token,
                Id = usuario.Id,
                Email = usuario.Email,
                Nombre = usuario.Nombre,
                Rol = usuario.Rol,
                FotoUrl = usuario.FotoUrl,
                OnboardingCompleto = usuario.OnboardingCompleto,
                EstadoAprobacion = ((EstadoAprobacion)usuario.EstadoAprobacionId).ToString()
            };
        }

        public async Task RegisterAsync(RegisterDto dto)
        {
            var existe = await _context.Usuarios.AnyAsync(u => u.Email == dto.Email);
            if (existe) throw new Exception("El correo ya esta registrado");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var usuario = new Usuario
            {
                Email = dto.Email,
                Nombre = dto.Nombre,
                PasswordHash = passwordHash,
                AuthProvider = "Local",
                IsEmailVerified = false,
                Rol = "Nuevo",
                EstadoAprobacionId = (int)EstadoAprobacion.Ninguno,
                FechaRegistro = DateTime.UtcNow
            };

            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();

            var tokenVerificacion = GenerarTokenVerificacion(usuario);

            var frontendUrl = _configuration["FrontendUrl"] ?? throw new InvalidOperationException("La variable FrontendUrl no esta configurada");

            string link = $"{frontendUrl}/verificar-correo?token={tokenVerificacion}";

            string html = $"<h2>Bienvenido a TutoriasU, {System.Net.WebUtility.HtmlEncode(usuario.Nombre)}!</h2>" +
                          $"<p>Por favor verifica tu cuenta haciendo clic en el siguiente enlace:</p>" +
                          $"<a href='{link}'>Verificar mi cuenta</a>";


            try
            {
                await _emailService.SendEmailAsync(usuario.Email, "Verifica tu cuenta - Tutoriasu", html);
            }
            catch (Exception)
            {
                // Sin el correo la cuenta no se puede verificar: se deshace el
                // registro para que el usuario pueda intentarlo de nuevo.
                _context.Usuarios.Remove(usuario);
                await _context.SaveChangesAsync();
                throw new InvalidOperationException("No pudimos enviar el correo de verificación. Intenta registrarte de nuevo en unos minutos.");
            }
        }

        public async Task<AuthResponseDto> LoginWithEmailAsync(LoginDto dto)
        {
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email == dto.Email)
                    ?? throw new UnauthorizedAccessException("Credenciales incorrectas.");

            if (string.IsNullOrEmpty(usuario.PasswordHash))
                throw new UnauthorizedAccessException("Esta cuenta fue creada con Google/Microsoft. Inicia sesión con esos botones.");

            if (!BCrypt.Net.BCrypt.Verify(dto.Password, usuario.PasswordHash))
                throw new UnauthorizedAccessException("Credenciales incorrectas.");

            if (!usuario.IsEmailVerified)
                throw new UnauthorizedAccessException("Por favor verifica tu correo electrónico antes de iniciar sesión.");

            var token = GenerateJwtToken(usuario);

            return new AuthResponseDto
            {
                Token = token,
                Id = usuario.Id,
                Email = usuario.Email,
                Nombre = usuario.Nombre,
                Rol = usuario.Rol,
                FotoUrl = usuario.FotoUrl,
                OnboardingCompleto = usuario.OnboardingCompleto,
                EstadoAprobacion = ((EstadoAprobacion)usuario.EstadoAprobacionId).ToString()
            };
        }

        public async Task VerifyEmailAsync(string verificationToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var keyString = _configuration["Jwt:Key"] ?? throw new Exception("Llave JWT no configurada");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));

            try
            {
                // Solo tokens de verificación: un token de sesión no sirve aquí
                // (otra audiencia) y uno de verificación no sirve como sesión.
                handler.ValidateToken(verificationToken, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = true,
                    ValidIssuer = _configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = AudienciaVerificacion,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                if (jwtToken.Claims.FirstOrDefault(x => x.Type == ClaimProposito)?.Value != PropositoVerificacion)
                    throw new SecurityTokenException("El token no es de verificación.");
                var email = jwtToken.Claims.First(x => x.Type == ClaimTypes.Email).Value;

                var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email == email)
                    ?? throw new Exception("Usuario no encontrado.");

                usuario.IsEmailVerified = true;
                await _context.SaveChangesAsync();
            }
            catch
            {
                throw new Exception("El enlace de verificación no es válido o ha expirado.");
            }
        }

        private async Task<Usuario> FindOrCreateUserAsync(string email, string nombre, string? fotoUrl, string authProvider, string? providerId)
        {
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email == email);

            if (usuario == null)
            {
                usuario = new Usuario
                {
                    Email = email,
                    Nombre = nombre,
                    FotoUrl = fotoUrl,
                    AuthProvider = authProvider,
                    ProviderId = providerId,
                    IsEmailVerified = true,
                    Rol = "Nuevo",
                    EstadoAprobacionId = (int)EstadoAprobacion.Ninguno,
                    FechaRegistro = DateTime.UtcNow
                };

                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();
            }
            else
            {
                if (!string.IsNullOrEmpty(fotoUrl) && usuario.FotoUrl != fotoUrl)
                {
                    usuario.FotoUrl = fotoUrl;
                    await _context.SaveChangesAsync();
                }    
            }

            return usuario;
        }

        private const string ClaimProposito = "proposito";
        private const string PropositoVerificacion = "verificar-correo";

        // Audiencia propia: el token del enlace de verificación no es aceptado
        // por el JwtBearer de la API (que espera Jwt:Audience).
        private string AudienciaVerificacion => $"{_configuration["Jwt:Audience"]}.verificacion";

        // Token del enlace de verificación: solo sirve para verificar el
        // correo y vence en 24 h (el de sesión dura días y abre la API).
        private string GenerarTokenVerificacion(Usuario usuario)
        {
            var keyString = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("La clave 'Jwt:Key' no esta configurada.");
            var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString)), SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: AudienciaVerificacion,
                claims: new[]
                {
                    new Claim(ClaimTypes.Email, usuario.Email),
                    new Claim(ClaimProposito, PropositoVerificacion)
                },
                expires: DateTime.UtcNow.AddHours(24),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GenerateJwtToken(Usuario usuario)
        {
            var keyString = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("La clave 'Jwt:Key' no esta configurada.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Email, usuario.Email),
                new Claim(ClaimTypes.Name, usuario.Nombre),
                new Claim(ClaimTypes.Role, usuario.Rol)
            };

            var expiresInDays = int.TryParse(_configuration["Jwt:ExpiresInDays"], out var days) ? days : 7;

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddDays(expiresInDays),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // Token y datos actuales del usuario: el rol y el estado de aprobación
        // pueden cambiar (p. ej. cuando el administrador aprueba a un tutor).
        public async Task<AuthResponseDto> RefrescarSesionAsync(int usuarioId)
        {
            var usuario = await _context.Usuarios.FindAsync(usuarioId)
                ?? throw new UnauthorizedAccessException("Usuario no encontrado");

            return new AuthResponseDto
            {
                Token = GenerateJwtToken(usuario),
                Id = usuario.Id,
                Email = usuario.Email,
                Nombre = usuario.Nombre,
                Rol = usuario.Rol,
                FotoUrl = usuario.FotoUrl,
                OnboardingCompleto = usuario.OnboardingCompleto,
                EstadoAprobacion = ((EstadoAprobacion)usuario.EstadoAprobacionId).ToString()
            };
        }

        public async Task<AuthResponseDto> AsignarRolAsync(int usuarioId, string nuevoRol)
        {
            // Solo en el onboarding y solo roles de usuario: el rol de tutor
            // aprobado lo da el administrador y el de administrador nunca se
            // asigna desde la app.
            if (nuevoRol != "Estudiante" && nuevoRol != "Tutor")
                throw new ArgumentException("No es un rol permitido por el sistema");

            var usuario = await _context.Usuarios.FindAsync(usuarioId)
                ?? throw new UnauthorizedAccessException("Usuario no encontrado");

            if (usuario.Rol != "Nuevo")
                throw new InvalidOperationException("Ya elegiste un rol.");

            usuario.Rol = nuevoRol;
            usuario.EstadoAprobacionId = (int)EstadoAprobacion.Ninguno;

            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(usuario);

            return new AuthResponseDto
            {
                Token = token,
                Id = usuario.Id,
                Email = usuario.Email,
                Nombre = usuario.Nombre,
                Rol = usuario.Rol,
                FotoUrl = usuario.FotoUrl,
                OnboardingCompleto = usuario.OnboardingCompleto,
                EstadoAprobacion = ((EstadoAprobacion)usuario.EstadoAprobacionId).ToString()
            };
        }
    }
}


