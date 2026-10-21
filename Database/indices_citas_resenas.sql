-- Índices para las consultas de reseñas y citas (idempotente).

-- Reseñas de un tutor, de la más reciente a la más antigua (perfil y promedio).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Resenas_TutorId' AND object_id = OBJECT_ID('dbo.Resenas'))
BEGIN
    CREATE INDEX IX_Resenas_TutorId ON dbo.Resenas (TutorId, FechaCreacion DESC);
END;

-- Citas por tutor (pendientes, próximas, historial, horas ocupadas).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Citas_TutorId' AND object_id = OBJECT_ID('dbo.Citas'))
BEGIN
    CREATE INDEX IX_Citas_TutorId ON dbo.Citas (TutorId, FechaHoraInicio);
END;

-- Citas por estudiante (próximas e historial).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Citas_EstudianteId' AND object_id = OBJECT_ID('dbo.Citas'))
BEGIN
    CREATE INDEX IX_Citas_EstudianteId ON dbo.Citas (EstudianteId, FechaHoraInicio);
END;
