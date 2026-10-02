namespace backend.DTOs.Responses;

public class DisponibilidadResponseDto
{
    public int Id { get; set; }

    public byte DiaSemana { get; set; }

    public TimeOnly HoraInicio { get; set; }

    public TimeOnly HoraFin { get; set; }
}
