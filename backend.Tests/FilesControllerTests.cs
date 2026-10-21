using System.Text;
using backend.Controllers;
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Services;
using backend.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace backend.Tests
{
    public class FilesControllerTests
    {
        private static IFormFile Archivo(string nombre, byte[] contenido) =>
            new FormFile(new MemoryStream(contenido), 0, contenido.Length, "File", nombre);

        private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n...");
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

        [Fact]
        public async Task UploadFile_ShouldReturnUrl_WhenFileIsValid()
        {
            var mockBlobService = new Mock<IBlobService>();
            mockBlobService.Setup(s => s.SubirComprobanteAsync(It.IsAny<IFormFile>()))
                .ReturnsAsync("https://azurereal.blob.core.windows.net/uploads/kardex.pdf");

            var controller = new FilesController(mockBlobService.Object, NullLogger<FilesController>.Instance);

            var result = await controller.UploadFile(new FileUploadRequestDto { File = Archivo("kardex.pdf", Pdf) });

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var responseDto = Assert.IsType<FileUploadResponseDto>(okResult.Value);
            Assert.Equal("https://azurereal.blob.core.windows.net/uploads/kardex.pdf", responseDto.Url);
        }

        [Fact]
        public async Task UploadFile_ShouldReturnBadRequest_WhenFileIsInvalid()
        {
            var mockBlobService = new Mock<IBlobService>();
            mockBlobService.Setup(s => s.SubirComprobanteAsync(It.IsAny<IFormFile>()))
                .ThrowsAsync(new ArgumentException("Solo se aceptan archivos PDF, PNG, JPG o WEBP."));
            var controller = new FilesController(mockBlobService.Object, NullLogger<FilesController>.Instance);

            var result = await controller.UploadFile(new FileUploadRequestDto { File = Archivo("pagina.html", Pdf) });

            ProblemaAssert.EsProblema(result.Result, 400, "Solo se aceptan archivos PDF, PNG, JPG o WEBP.");
        }

        [Theory]
        [InlineData("kardex.pdf", "application/pdf", ".pdf")]
        [InlineData("foto.PNG", "image/png", ".png")]
        public void Validador_AceptaPdfEImagenesReales(string nombre, string tipo, string extension)
        {
            var contenido = nombre.EndsWith("pdf") ? Pdf : Png;

            var resultado = ValidadorArchivos.Validar(Archivo(nombre, contenido));

            Assert.Equal((extension, tipo), resultado);
        }

        [Theory]
        [InlineData("pagina.html")]   // extensión no permitida
        [InlineData("falso.pdf")]     // HTML disfrazado de PDF
        public void Validador_RechazaTiposNoPermitidosOFalsos(string nombre)
        {
            var html = Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");

            Assert.Throws<ArgumentException>(() => ValidadorArchivos.Validar(Archivo(nombre, html)));
        }

        [Fact]
        public void Validador_RechazaArchivosDeMasDe5Mb()
        {
            var grande = new byte[ValidadorArchivos.TamanoMaximo + 1];
            Pdf.CopyTo(grande, 0);

            Assert.Throws<ArgumentException>(() => ValidadorArchivos.Validar(Archivo("grande.pdf", grande)));
        }
    }
}
