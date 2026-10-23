using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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
        private readonly IVerificadorCuentasExternas _verificador;

        public AuthService(AppDbContext context, IConfiguration configuration, IEmailService emailService, IVerificadorCuentasExternas verificador)
        {
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
            _verificador = verificador;
        }

        public async Task<AuthResponseDto> LoginWithGoogleAsync(ExternalLoginDto dto)
        {
            var cuenta = await _verificador.VerificarGoogleAsync(dto.IdToken);
            var fotoUrl = cuenta.FotoUrl;

            var usuario = await FindOrCreateUserAsync(
                email: cuenta.Email,
                nombre: cuenta.Nombre,
                fotoUrl: fotoUrl,
                authProvider: "Google",
                providerId: cuenta.ProveedorId
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
            var cuenta = await _verificador.VerificarMicrosoftAsync(dto.IdToken);

            var usuario = await FindOrCreateUserAsync(
                email: cuenta.Email,
                nombre: cuenta.Nombre,
                fotoUrl: null,
                authProvider: "Microsoft",
                providerId: cuenta.ProveedorId
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

            var html = RecuperacionCuenta.CorreoDeVerificacion(_configuration, usuario.Nombre, usuario.Email);


            try
            {
                await _emailService.SendEmailAsync(usuario.Email, RecuperacionCuenta.AsuntoVerificacion, html);
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
                throw new CorreoSinVerificarException();

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
            try
            {
                var contenido = TokensCuenta.Validar(_configuration, TokensCuenta.Verificacion, verificationToken);
                var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email == contenido.Email)
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


