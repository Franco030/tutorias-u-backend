-- Flujo de reservas: fechas bloqueadas, antelación mínima, enlace de reunión
-- fijo del tutor y tema de la cita. Idempotente: se puede ejecutar varias veces.

-- Días en que un tutor no da tutorías (vacaciones, exámenes...).
IF OBJECT_ID('dbo.Bloqueos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Bloqueos
    (
        Id INT IDENTITY(1,1) NOT NULL,
        TutorId INT NOT NULL,
        Fecha DATE NOT NULL,
        Motivo NVARCHAR(200) NULL,

        CONSTRAINT PK_Bloqueos PRIMARY KEY (Id),
        CONSTRAINT FK_Bloqueos_Tutor FOREIGN KEY (TutorId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE,
        CONSTRAINT UQ_Bloqueos_Tutor_Fecha UNIQUE (TutorId, Fecha)
    );
END;

-- Horas mínimas de anticipación para reservar con el tutor (0 a 168).
IF COL_LENGTH('dbo.Usuarios', 'AntelacionMinimaHoras') IS NULL
BEGIN
    ALTER TABLE dbo.Usuarios
        ADD AntelacionMinimaHoras INT NOT NULL
            CONSTRAINT DF_Usuarios_AntelacionMinimaHoras DEFAULT 12
            CONSTRAINT CK_Usuarios_AntelacionMinimaHoras CHECK (AntelacionMinimaHoras BETWEEN 0 AND 168);
END;

-- Enlace de videollamada que el tutor usa por defecto al aceptar una cita.
IF COL_LENGTH('dbo.Usuarios', 'LinkReunion') IS NULL
BEGIN
    ALTER TABLE dbo.Usuarios ADD LinkReunion VARCHAR(255) NULL;
END;

-- Lo que el estudiante quiere repasar en la sesión.
IF COL_LENGTH('dbo.Citas', 'Tema') IS NULL
BEGIN
    ALTER TABLE dbo.Citas ADD Tema NVARCHAR(500) NULL;
END;
