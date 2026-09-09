using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using backend.Data;
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Models;
using backend.Services.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace backend.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<AuthResponseDto> LoginWithGoogleAsync(ExternalLoginDto dto)
        {
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException("El token de Google no es válido o ha expirado.", ex);
            }

            var usuario = await FindOrCreateUserAsync(
                email: payload.Email,
                nombre: payload.Name ?? payload.Email,
                fotoUrl: payload.Picture,
                authProvider: "Google",
                providerId: payload.Subject
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
