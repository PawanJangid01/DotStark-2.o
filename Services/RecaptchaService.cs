using DotStarkWeb.Services;
using System.Text.Json;

public class RecaptchaService : IRecaptchaService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _httpClient;

    public RecaptchaService(IConfiguration config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
    }

    public async Task<bool> ValidateTokenAsync(string token, string ipAddress)
    {
        var secret = _config["GoogleReCaptcha:SecretKey"];

        var response = await _httpClient.PostAsync(
            "https://www.google.com/recaptcha/api/siteverify",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("secret", secret),
                new KeyValuePair<string,string>("response", token),
                new KeyValuePair<string,string>("remoteip", ipAddress)
            })
        );

        var json = await response.Content.ReadAsStringAsync();

        var result = JsonSerializer.Deserialize<RecaptchaResponse>(json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return result != null &&
               result.Success &&
               result.Score >= 0.5; // adjust if needed
    }
}

public class RecaptchaResponse
{
    public bool Success { get; set; }
    public double Score { get; set; }
    public string Action { get; set; }
    public DateTime Challenge_ts { get; set; }
    public string Hostname { get; set; }
}