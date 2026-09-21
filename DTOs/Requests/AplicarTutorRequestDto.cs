using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Requests
{
    public class AplicarTutorRequestDto
    {
        [Required(ErrorMessage = "La URL de la credencial es requerida.")]
        [Url(ErrorMessage = "Debe proporcionar una URL válida.")]
        public string UrlCredencial { get; set; } = string.Empty;

        [Required(ErrorMessage = "Debe seleccionar al menos una materia.")]
        [MinLength(1, ErrorMessage = "Debe enviar al menos el ID de una materia.")]
        public List<int> MateriaIds { get; set; } = new List<int>();
    }
}