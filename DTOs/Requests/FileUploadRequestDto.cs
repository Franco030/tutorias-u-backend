using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Requests
{
    public class FileUploadRequestDto
    {
        [Required(ErrorMessage = "El archivo es obligatorio.")]
        public required IFormFile File { get; set; }

        public string Container { get; set; } = "uploads";
    }
}
