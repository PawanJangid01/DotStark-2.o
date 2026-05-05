namespace DotStarkWeb.Services
{
    public interface IRecaptchaService
    {
        Task<bool> ValidateTokenAsync(string token, string ipAddress);
    }
}