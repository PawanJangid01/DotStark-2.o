using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;


namespace DotStarkWeb.Services
{
    public interface ISqliteFormService
    {
        void SaveFormData(
            string name,
            string email,
            string company,
            string message
        );

        void SendBrevoTemplateEmail(
           string name, string email
        );
        void SendBrevoTemplateEmailToAdmin(
            string name,
            string email,
            string comapny,
            string message
        );
    }

    public class SqliteFormService : ISqliteFormService
    {
        private readonly string _connectionString;
        private readonly IPublishedContentQuery _contentQuery;

        public SqliteFormService(
        IConfiguration config,
        IPublishedContentQuery contentQuery,
        IUmbracoContextAccessor umbracoContextAccessor)
        {
            _connectionString = config.GetConnectionString("umbracoDbDSN");
            _contentQuery = contentQuery;
        }

        public void SaveFormData(string name, string email, string company, string message)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();

            command.CommandText = @"
                INSERT INTO ContactUsForm 
                (Name, Email, Company, Message, SubmittedAt)
                VALUES 
                ($name, $email, $company, $message, $submittedAt)
            ";

            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$email", email);
            command.Parameters.AddWithValue("$company", company);
            command.Parameters.AddWithValue("$message", message);
            command.Parameters.AddWithValue("$submittedAt", DateTime.UtcNow);

            command.ExecuteNonQuery();
        }

        private IPublishedContent? GetEmailSettings()
        {
            return _contentQuery
        .ContentAtRoot()
        .FirstOrDefault(x => x.ContentType.Alias == "smtpEmailCredentials");
        }


        public void SendBrevoTemplateEmail(
        string name,
        string email
     )
        {
            var settings = GetEmailSettings();
            if (settings == null) return;

            var apiKey = settings.Value<string>("smtpAPIKey");
            var userReciverEmail = email;
            var senderEmail = settings.Value<string>("senderEmail");
            var fromName = settings.Value<string>("fromName");
            var fullName = name;
            var templateId = 1;

            Dictionary<string, object> templateParams = new Dictionary<string, object>
               {
                   { "customer", fullName }
               };



            var payload = new
            {
                to = new[]
                {
            new { email = userReciverEmail, name = fullName }
        },
                sender = new
                {
                    email = senderEmail,
                    name = fromName
                },
                templateId = templateId,
                @params = templateParams,
                replyTo = new
                {
                    email = email,
                    name = fullName
                }
            };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("api-key", apiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = client.PostAsync(
                "https://api.brevo.com/v3/smtp/email",
                content
            ).Result;

            if (!response.IsSuccessStatusCode)
            {
                var error = response.Content.ReadAsStringAsync();

                // Log error (choose one)
                Console.WriteLine("Brevo email failed: " + error);
                // _logger.LogError("Brevo email failed: {Error}", error);

                return;
            }
        }


        public void SendBrevoTemplateEmailToAdmin(
                             string name,
                            string email,
                            string company,
                            string message
                                        )
        {
            var settings = GetEmailSettings();
            if (settings == null) return;

            var apiKey = settings.Value<string>("smtpAPIKey");
            var adminEmail = settings.Value<string>("adminEmail");
            var senderEmail = settings.Value<string>("senderEmail");
            var fromName = settings.Value<string>("fromName");
            var fullName = name;
            var templateId = 2;

            Dictionary<string, object> templateParams = new Dictionary<string, object>
               {
                     
                     { "name", name },
                     { "Email", email },
                     { "Company", company },
                     { "Message", message }
               };


            var payload = new
            {
                to = new[]
                {
            new { email = adminEmail, name = fullName }
        },
                sender = new
                {
                    email = senderEmail,
                    name = fromName
                },
                templateId = templateId,
                @params = templateParams,
                replyTo = new
                {
                    email = email,
                    name = fullName
                }
            };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("api-key", apiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = client.PostAsync(
                "https://api.brevo.com/v3/smtp/email",
                content
            ).Result;

            if (!response.IsSuccessStatusCode)
            {
                var error = response.Content.ReadAsStringAsync();

                // Log error (choose one)
                Console.WriteLine("Brevo email failed: " + error);
                // _logger.LogError("Brevo email failed: {Error}", error);

                return;
            }
        }
    }
}
