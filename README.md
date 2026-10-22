# TutoriasU · Backend

API REST de TutoriasU, la plataforma de tutorías entre estudiantes universitarios. Gestiona cuentas y roles, el catálogo de tutores y materias, la disponibilidad de los tutores, el ciclo completo de las citas, las reseñas, la aprobación de tutores y las notificaciones por correo.

El cliente web está en el repositorio `tutorias-u-frontend`.

## Funcionalidades

| Rol | Funcionalidades |
| --- | --- |
| Estudiante | Registro e inicio de sesión (correo, Google o Microsoft), selección de intereses, tutores recomendados, catálogo y perfil público de tutores, reserva de citas con tema opcional, cancelación con motivo, historial y reseñas de 1 a 5 estrellas. |
| Tutor | Postulación con comprobante, horario semanal, días bloqueados, antelación mínima para reservar, enlace de videollamada fijo, aceptación y rechazo de solicitudes, gestión de materias. |
| Administrador | Revisión de postulaciones con acceso temporal al comprobante, aprobación y rechazo con motivo. |
| Sistema | Correos transaccionales (solicitud, confirmación, cancelación, rechazo y resultado de la postulación), cálculo del promedio de calificaciones y marcado de sesiones completadas. |

## Tecnologías

| Área | Tecnología |
| --- | --- |
| Plataforma | .NET 10, ASP.NET Core Web API |
| Datos | Entity Framework Core 10 (database-first) sobre SQL Server / Azure SQL Database |
| Autenticación | JWT propios; Google Identity y Microsoft Entra ID como proveedores externos |
| Archivos | Azure Blob Storage (contenedor privado, lectura con URL firmada SAS) |
| Correo | Azure Communication Services Email, con cola en segundo plano |
| Pruebas | xUnit, Moq, EF Core InMemory y `WebApplicationFactory` para pruebas de integración |
| Despliegue | GitHub Actions hacia Azure App Service (Linux) |

## Estructura del proyecto

```text
backend/
├── Authorization/      Políticas de autorización (TutorAprobado)
├── Controllers/        Endpoints de la API
├── Data/               AppDbContext generado por scaffold
├── Database/           Scripts SQL idempotentes para cambios de esquema
├── DTOs/               Contratos de entrada y salida
├── Enums/              Estados de aprobación
├── Middleware/         ProblemDetails en español y límite de intentos
├── Models/             Entidades generadas por scaffold
├── Services/           Autenticación, correo, almacenamiento, hora de la plataforma
│   └── Templates/      Plantillas HTML de los correos (recursos incrustados)
├── backend.Tests/      Pruebas unitarias y de integración
├── Program.cs          Configuración de servicios y del pipeline HTTP
└── appsettings.Example.json
```

## Requisitos

- .NET SDK 10
- SQL Server 2019 o posterior, o una base de datos de Azure SQL
- Opcional para funciones completas: una cuenta de Azure Storage y un recurso de Azure Communication Services con dominio de correo

## Configuración

La configuración local va en `appsettings.Development.json`, que está excluido del control de versiones. Toma como base `appsettings.Example.json`. También se pueden usar `dotnet user-secrets` o variables de entorno (por ejemplo `Jwt__Key`).

| Clave | Descripción |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | Cadena de conexión a SQL Server. No uses la base de datos de producción para desarrollo. |
| `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiresInDays` | Firma y validación de los tokens de sesión. La clave debe tener al menos 32 caracteres. |
| `FrontendUrl` | URL del cliente web; se usa en los enlaces de los correos. |
| `COMMUNICATION_SERVICES_CS`, `EmailSenderAddress` | Envío de correos. Sin ellos la API funciona, pero no envía correos ni permite registrarse con correo y contraseña. |
| `AZURE_STORAGE_CONNECTION_STRING` | Almacenamiento de comprobantes. Debe incluir `AccountKey` para poder generar URLs firmadas. |
| `ZonaHoraria` | Zona horaria de la universidad (IANA). Por defecto `America/Mexico_City`. Todas las fechas de citas se interpretan en esta zona. |
| `LimiteAcceso:PorMinuto` | Intentos por minuto y por IP en inicio de sesión y registro. Por defecto 10. |

## Ejecución local

```bash
dotnet restore
dotnet run
```

La API queda disponible en la URL que indique la consola. `GET /` responde con un mensaje de estado.

## Pruebas

```bash
dotnet test backend.Tests/backend.Tests.csproj -c Release
```

Las pruebas no necesitan SQL Server ni servicios de Azure: usan una base de datos en memoria y dobles de prueba. Se ejecutan en Release para no chocar con un servidor local en ejecución, que bloquea los binarios de Debug.

## Base de datos

El modelo es database-first: el esquema se modifica en SQL Server y las clases de `Models/` y `Data/AppDbContext.cs` se regeneran con scaffold.

1. Ejecuta los scripts de `Database/` que falten. Son idempotentes y se pueden volver a ejecutar sin efectos secundarios:
   - `intereses_estudiante.sql`: catálogo de materias e intereses de los estudiantes.
   - `indices_citas_resenas.sql`: índices de citas y reseñas, e índice único contra la doble reserva.
   - `flujo_reservas.sql`: días bloqueados, antelación mínima, enlace fijo del tutor y tema de la cita.
2. Regenera el modelo:

```bash
dotnet ef dbcontext scaffold Name=ConnectionStrings:DefaultConnection Microsoft.EntityFrameworkCore.SqlServer -o Models -c AppDbContext --context-dir Data --force --no-onconfiguring
```

`--no-onconfiguring` es necesario: la conexión la configura `Program.cs`, y las pruebas sustituyen el proveedor por uno en memoria.

## Referencia de la API

Todas las rutas están bajo `/api`. Salvo las de autenticación y el catálogo público, requieren un token `Bearer`.

| Recurso | Endpoints principales |
| --- | --- |
| `auth` | `POST google`, `POST microsoft`, `POST register`, `POST login`, `GET verify-email`, `GET sesion`, `POST asignar-rol` |
| `citas` | `POST agendar`, `PUT {id}/aceptar`, `PUT {id}/rechazar`, `PUT {id}/cancelar`, `PUT {id}/completar`, `GET pendientes`, `GET proximas`, `GET historial`, `GET ocupadas` |
| `tutores` | `GET`, `GET recomendados`, `GET {id}`, `POST aplicar`, `PUT mis-materias`, `POST disponibilidad`, `GET {id}/disponibilidad`, `GET/PUT preferencias`, `GET/PUT bloqueos`, `GET {id}/bloqueos`, `GET {id}/resenas` |
| `resenas` | `POST` |
| `admin` | `GET solicitudes`, `PUT tutores/{id}/aprobar`, `PUT tutores/{id}/rechazar` |
| `estudiantes` | `GET/POST intereses` |
| `materias` | `GET` |
| `files` | `POST upload` (comprobantes PDF o imagen, hasta 5 MB) |

## Convenciones

- **Errores:** todas las respuestas de error usan ProblemDetails (`application/problem+json`) con un `title` en español pensado para mostrarse al usuario. Los errores 500 no exponen detalles internos.
- **Fechas y horas:** las citas y la disponibilidad se guardan en la hora local de la plataforma, sin zona. Toda comparación con "ahora" se hace con `IHoraPlataforma`, nunca con `DateTime.UtcNow`.
- **Autorización:** los endpoints de tutor usan la política `TutorAprobado`, que exige el rol `Tutor` y una postulación aprobada en la base de datos, no solo el rol del token.
- **Correos:** los controladores encolan los avisos después de guardar en la base de datos. Una falla del servicio de correo se registra y nunca revierte la operación.
- **Archivos:** el tipo de archivo se valida por su firma (los primeros bytes) y el contenedor lo decide el servidor.

## Despliegue

- Cada pull request hacia `main` ejecuta el flujo **Backend CI** (compilación y pruebas).
- Cada fusión en `main` ejecuta el flujo de despliegue: compila, publica para `linux-x64` y despliega en Azure App Service con ZIP Deploy.
- El App Service usa la pila .NET 10 en Linux con el comando de inicio `dotnet backend.dll`.
- Para comprobar un despliegue, `GET /api/auth/sesion` sin token debe responder `401` con `application/problem+json`.

Infraestructura en Azure (grupo de recursos `rg-tutoriasu`): App Service, Azure SQL Database, Storage Account y Communication Services con dominio de correo propio.
