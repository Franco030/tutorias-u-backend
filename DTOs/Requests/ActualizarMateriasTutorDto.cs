using System.Collections.Generic;

namespace backend.DTOs.Requests
{
    public class ActualizarMateriasTutorDto
    {
        public List<int> MateriaIds { get; set; } = new List<int>();
    }
}
