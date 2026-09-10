using backend.DTOs.Requests;
using backend.DTOs.Responses;

namespace backend.Services.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponseDto> LoginWithGoogleAsync(ExternalLoginDto dto);
        Task<AuthResponseDto> LoginWithMicrosoftAsync(ExternalLoginDto dto);
        Task RegisterAsync(RegisterDto dto);
        Task<AuthResponseDto> LoginWithEmailAsync(LoginDto dto);
        Task VerifyEmailAsync(string verificationToken);
    }
}
