using System.Text.Json;

namespace DotStarkWeb.Services
{
    public class RecaptchaService : IRecaptchaService
    {
        private readonly HttpClient _httpClient;
        private readonly string _recaptchaApiUrl;
        private readonly string _secretKey;

        public RecaptchaService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _secretKey = configuration["Recaptcha:SecretKey"];

            // ✅ Fallback so URL is never null
            _recaptchaApiUrl = configuration["Recaptcha:RecaptchaApiUrl"]
                               ?? "https://www.google.com/recaptcha/api/siteverify";

            if (string.IsNullOrWhiteSpace(_secretKey))
                throw new InvalidOperationException("Recaptcha:SecretKey is missing in appsettings.json");
        }

        public async Task<bool> ValidateTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var url = $"{_recaptchaApiUrl}?secret={_secretKey}&response={token}";

            var response = await _httpClient.PostAsync(url, null);

            if (!response.IsSuccessStatusCode)
                return false;

            var json = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<RecaptchaResponse>(json);

            return result != null && result.success && result.score >= 0.5;
        }
    }

    public class RecaptchaResponse
    {
        public bool success { get; set; }
        public string challenge_Ts { get; set; }
        public string hostname { get; set; }
        public float score { get; set; }
        public string action { get; set; }
    }
}