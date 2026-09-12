using backend.Services;
using backend.DTOs.Requests;
using backend.Data;
using backend.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace backend.Tests
{
    public class AuthServiceTests
    {
        private readonly AppDbContext _dbContext;
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly Mock<IEmailService> _mockEmailService;
        private readonly AuthService _authService;

        public AuthServiceTests()
        {
            // 1. Configurar una Base de Datos "en memoria" (se borra al terminar la prueba y NO toca tu Azure SQL)
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AppDbContext(options);

            // 2. Simular (Mockear) las configuraciones del appsettings (JWT)
            _mockConfig = new Mock<IConfiguration>();
            _mockConfig.Setup(c => c["Jwt:Key"]).Returns("LallaveSuperSecretaDePruebaQueDebeSerLarga12345");
            _mockConfig.Setup(c => c["Jwt:Issuer"]).Returns("TestIssuer");
            _mockConfig.Setup(c => c["Jwt:Audience"]).Returns("TestAudience");
            _mockConfig.Setup(c => c["FrontendUrl"]).Returns("http://localhost:5173");

            // 3. Simular (Mockear) el servicio de correos para que no mande correos reales
            _mockEmailService = new Mock<IEmailService>();

            // 4. Instanciar tu servicio real pasándole los simuladores
            _authService = new AuthService(_dbContext, _mockConfig.Object, _mockEmailService.Object);
        }

        [Fact]
        public async Task RegisterAsync_ShouldCreateUserAndHashPassword_WhenEmailIsNew()
        {
            // Arrange (Preparar datos de prueba)
            var dto = new RegisterDto
            {
                Email = "test@ejemplo.com",
                Nombre = "Usuario Prueba",
                Password = "PasswordSeguro123"
            };

            // Act (Ejecutar la función que queremos probar)
            await _authService.RegisterAsync(dto);

            // Assert (Verificar que los resultados sean los esperados)
            var userInDb = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Email == dto.Email);

            Assert.NotNull(userInDb); // El usuario debió guardarse en la DB
            Assert.Equal("test@ejemplo.com", userInDb.Email); // El correo debe coincidir
            Assert.False(userInDb.IsEmailVerified); // Debe iniciar sin verificar

            // Verificar que la contraseña se encriptó correctamente y NO es texto plano
            Assert.NotEqual("PasswordSeguro123", userInDb.PasswordHash);
            Assert.True(BCrypt.Net.BCrypt.Verify("PasswordSeguro123", userInDb.PasswordHash));

            // Verificar que nuestro sistema intentó mandar el correo de verificación exactamente 1 vez
            _mockEmailService.Verify(e => e.SendEmailAsync(dto.Email, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }
    }
}