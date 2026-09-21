using System.ComponentModel.DataAnnotations;

namespace backend.DTOs.Requests
{
    public class AplicarTutorRequestDto
    {
        [Required(ErrorMessage  = "La URL de la credencial es obligatoria.")]
        [Url(ErrorMessage = "El formato de la URL no es valido.")]
        public string UrlCredencial { get; set; } = string.Empty;

        [Required(ErrorMessage = "La lista de materias es obligatoria.")]
        [MinLength(1, ErrorMessage = "Debe seleccionar al menos una materia para postularse.")]
        public List<int> MateriaIds { get; set; } = new List<int>();
    }
}
