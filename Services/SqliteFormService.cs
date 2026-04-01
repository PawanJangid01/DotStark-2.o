using MailKit.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using MimeKit;
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
            string companyName,
            string message,
            string subscription,
            string subject,
            string companySize,
            string productType
        );

        void SaveDemoFormData(
            string firstName,
            string lastName,
            string email,
            string companyName,
            string companySize,
            string jobRole
        );

        void SendBrevoTemplateEmail(
           string name, string email
        );
        void SendEmail(
            string mailBody
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

        public void SaveFormData(string name, string email, string companyName, string companySize, string subject, string message, string subscription, string productType)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();

            command.CommandText = @"
                INSERT INTO ContactUsForm 
                (Name, Email, CompanyName, Message, Subject, CompanySize, SubmittedAt, Subscription, ProductType)
                VALUES 
                ($name, $email, $companyName, $message, $subject, $companySize,  $submittedAt, $subscription, $productType)
            ";

            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$email", email);
            command.Parameters.AddWithValue("$companyName", companyName);
            command.Parameters.AddWithValue("$companySize", companySize);
            command.Parameters.AddWithValue("$subject", subject);
            command.Parameters.AddWithValue("$message", message ?? string.Empty);
            command.Parameters.AddWithValue("$submittedAt", DateTime.UtcNow);
            command.Parameters.AddWithValue("$subscription", subscription ?? string.Empty);
            command.Parameters.AddWithValue("$productType", productType ?? string.Empty);

            command.ExecuteNonQuery();
        }


        public void SaveDemoFormData(string firstName, string lastName, string email, string companyName, string companySize, string jobRole)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();

            command.CommandText = @"
                INSERT INTO DemoForm 
                (FirstName, LastName, Email, CompanyName, CompanySize, JobRole)
                VALUES 
                ($firstName, $lastName, $email, $companyName, $companySize,  $jobRole)
            ";

            command.Parameters.AddWithValue("$firstName", firstName);
            command.Parameters.AddWithValue("$lastName", lastName);
            command.Parameters.AddWithValue("$email", email);
            command.Parameters.AddWithValue("$companyName", companyName);
            command.Parameters.AddWithValue("$companySize", companySize);
            command.Parameters.AddWithValue("$jobRole", jobRole);

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
            var templateId = 7;

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


        public void SendEmail(string mailBody)
        {
            var settings = GetEmailSettings();
            if (settings == null) return;

            // SMTP settings from Umbraco
            var smtpHost = settings.Value<string>("smtpHost");      // smtpout.secureserver.net
            var smtpPort = settings.Value<int>("smtpPort");        // 587
            var smtpUser = settings.Value<string>("smtpUsername"); // info@datamatrixiq.com
            var smtpkey = settings.Value<string>("smtpKey"); // EMAIL PASSWORD

            var senderEmail = settings.Value<string>("senderEmail");
            var adminEmail = settings.Value<string>("adminEmail");
            var fromName = settings.Value<string>("fromName");
            var subject = settings.Value<string>("emailSubject");


            // Build email
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, senderEmail));
            //message.To.Add(MailboxAddress.Parse(adminEmail)); // Admin email
            message.Subject = subject;

            // ✅ Add multiple admin emails
            if (!string.IsNullOrWhiteSpace(adminEmail))
            {
                var emailList = adminEmail
                    .Split(',', StringSplitOptions.RemoveEmptyEntries);

                foreach (var email in emailList)
                {
                    message.To.Add(MailboxAddress.Parse(email.Trim()));
                }
            }

            message.Body = new TextPart("html")
            {
                Text = mailBody
            };

            using var client = new MailKit.Net.Smtp.SmtpClient();
            client.AuthenticationMechanisms.Remove("XOAUTH2");
            client.Connect(
                smtpHost,
                smtpPort,
                SecureSocketOptions.StartTls
            );

            client.Authenticate(smtpUser, smtpkey);
            client.Send(message);
            client.Disconnect(true);
        }
    }
}
