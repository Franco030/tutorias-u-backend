using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Requests
{
    public class RegisterDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(3)]
        public string Nombre { get; set; } = string.Empty;

        [Required, MinLength(6, ErrorMessage = "La clave debe tener al menos 6 caracteres.")]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }
}
