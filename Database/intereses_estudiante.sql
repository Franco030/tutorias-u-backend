-- Catálogo de materias
IF OBJECT_ID('dbo.Materias', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Materias
    (
        Id INT IDENTITY(1,1) NOT NULL,
        Nombre NVARCHAR(100) NOT NULL,
        Categoria NVARCHAR(100) NOT NULL,

        CONSTRAINT PK_Materias
            PRIMARY KEY (Id)
    );
END;


-- Relación entre estudiantes y materias de interés
IF OBJECT_ID('dbo.EstudianteIntereses', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EstudianteIntereses
    (
        EstudianteId INT NOT NULL,
        MateriaId INT NOT NULL,

        CONSTRAINT PK_EstudianteIntereses
            PRIMARY KEY (EstudianteId, MateriaId),

        CONSTRAINT FK_EstudianteIntereses_Usuarios
            FOREIGN KEY (EstudianteId)
            REFERENCES dbo.Usuarios(Id),

        CONSTRAINT FK_EstudianteIntereses_Materias
            FOREIGN KEY (MateriaId)
            REFERENCES dbo.Materias(Id)
    );
END;


-- Datos iniciales
IF NOT EXISTS (SELECT 1 FROM dbo.Materias)
BEGIN
    INSERT INTO dbo.Materias (Nombre, Categoria)
    VALUES
        ('Matemáticas', 'Ciencias Exactas'),
        ('Física', 'Ciencias Exactas'),
        ('Cálculo', 'Matemáticas'),
        ('Álgebra', 'Matemáticas'),
        ('Programación', 'Tecnología'),
        ('Bases de Datos', 'Tecnología'),
        ('Redes', 'Tecnología'),
        ('Inglés', 'Idiomas');
END;
