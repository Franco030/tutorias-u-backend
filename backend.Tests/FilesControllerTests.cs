using backend.Controllers;
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace backend.Tests
{
    public class FilesControllerTests
    {
        [Fact]
        public async Task UploadFile_ShouldReturnUrl_WhenFileIsValid()
        {
            var mockBlobService = new Mock<IBlobService>();
            mockBlobService.Setup(s => s.SubirArchivoAsync(It.IsAny<IFormFile>(), It.IsAny<string>()))
                .ReturnsAsync("https://azurereal.blob.core.windows.net/general/foto.png");

            var controller = new FilesController(mockBlobService.Object);

            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(100);

            var request = new FileUploadRequestDto { File = mockFile.Object, Container = "general" };

            var result = await controller.UploadFile(request);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var responseDto = Assert.IsType<FileUploadResponseDto>(okResult.Value);
            Assert.Equal("https://azurereal.blob.core.windows.net/general/foto.png", responseDto.Url);
        }
    }
}
