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
                catch (Exception ex)
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
                FotoUrl = fotoUrl
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
                FotoUrl = usuario.FotoUrl
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
                Rol = "Estudiante",
                FechaRegistro = DateTime.UtcNow
            };

            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();

            var tokenVerificacion = GenerateJwtToken(usuario);

            var frontendUrl = _configuration["FrontendUrl"] ?? throw new InvalidOperationException("La variable FrontendUrl no esta configurada");

            string link = $"{frontendUrl}/verificar-correo?token={tokenVerificacion}";

            string html = $"<h2>Bienvenido a TutoriasU, {usuario.Nombre}!</h2>" +
                          $"<p>Por favor verifica tu cuenta haciendo clic en el siguiente enlace:</p>" +
                          $"<a href='{link}'>Verificar mi cuenta</a>";


            await _emailService.SendEmailAsync(usuario.Email, "Verifica tu cuenta - Tutoriasu", html);
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
                FotoUrl = usuario.FotoUrl
            };
        }

        public async Task VerifyEmailAsync(string verificationToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var keyString = _configuration["Jwt:Key"] ?? throw new Exception("Llave JWT no configurada");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));

            try
            {
                handler.ValidateToken(verificationToken, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = true,
                    ValidIssuer = _configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = _configuration["Jwt:Audience"],
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
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
                    Rol = "Estudiante",
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
    }
}
