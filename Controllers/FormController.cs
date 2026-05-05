using DotStarkWeb.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Website.Controllers;
using Umbraco.Extensions;
using static Lucene.Net.Util.OfflineSorter;
namespace DotStarkWeb.Controllers
{
    public class FormController : SurfaceController
    {
        private readonly IPublishedContentQuery _contentQuery;
        private readonly ISqliteFormService _formService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FormController> _logger;
        private readonly IRecaptchaService _recaptchaService;
        private static readonly Dictionary<string, (int Count, DateTime FirstRequest)> requestTracker
    = new Dictionary<string, (int, DateTime)>();

        public FormController(
            ISqliteFormService formService,
            IConfiguration configuration,
            IPublishedContentQuery contentQuery,
              ILogger<FormController> logger,
            IUmbracoContextAccessor umbracoContextAccessor,
            IUmbracoDatabaseFactory databaseFactory,
            ServiceContext serviceContext,
            IRecaptchaService recaptchaService,
            AppCaches appCaches,
            IProfilingLogger profilingLogger,
            IPublishedUrlProvider publishedUrlProvider)
            : base(umbracoContextAccessor, databaseFactory, serviceContext, appCaches, profilingLogger, publishedUrlProvider)
        {
            _formService = formService;
            _contentQuery = contentQuery;
            _configuration = configuration;
            _logger = logger;
            _recaptchaService = recaptchaService;
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SubmitForm(string name, string email, string companyName, string companySize, string subject, string subscription, string productType, string recaptchaToken, string userConfirm)
        {

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(subject) ||
                string.IsNullOrWhiteSpace(companySize) ||
                string.IsNullOrWhiteSpace(companyName))
            {
                TempData["FormError"] = "Fill out all required fields.";
                return CurrentUmbracoPage();
            }
            // ✅ Inline Email Validation (no separate method)
            var emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            if (!Regex.IsMatch(email, emailPattern, RegexOptions.IgnoreCase))
            {
                TempData["FormError"] = "Please enter a valid email address.";
                return CurrentUmbracoPage();
            }

            // -----------------------------
            // Honeypot Check
            // -----------------------------
            if (!string.IsNullOrWhiteSpace(userConfirm))
            {
                TempData["FormError"] = "Spam detected.";
                return CurrentUmbracoPage();
            }

            // =========================
            // GET IP
            // =========================
            var ipAddress = GetClientIpAddress();

            // =========================
            // RATE LIMIT (3 PER MIN)
            // =========================
            if (!string.IsNullOrEmpty(ipAddress))
            {
                lock (requestTracker)
                {
                    if (requestTracker.ContainsKey(ipAddress))
                    {
                        var entry = requestTracker[ipAddress];

                        if ((DateTime.UtcNow - entry.FirstRequest).TotalSeconds < 60)
                        {
                            if (entry.Count >= 3)
                            {
                                _logger.LogWarning($"Rate limit exceeded for IP: {ipAddress}");
                                TempData["FormError"] = "Too many requests. Try again later.";
                                return CurrentUmbracoPage();
                            }

                            requestTracker[ipAddress] = (entry.Count + 1, entry.FirstRequest);
                        }
                        else
                        {
                            requestTracker[ipAddress] = (1, DateTime.UtcNow);
                        }
                    }
                    else
                    {
                        requestTracker[ipAddress] = (1, DateTime.UtcNow);
                    }
                }
            }

            // =========================
            // reCAPTCHA VALIDATION (FIXED)
            // =========================
            //if (string.IsNullOrWhiteSpace(recaptchaToken))
            //{
            //    TempData["FormError"] = "Invalid reCAPTCHA token.";
            //    return CurrentUmbracoPage();
            //}

            //var isValidCaptcha = await _recaptchaService.ValidateTokenAsync(recaptchaToken, ipAddress);

            //if (!isValidCaptcha)
            //{
            //    _logger.LogWarning($"reCAPTCHA failed for IP: {userIpAddress}");
            //    TempData["FormError"] = "reCAPTCHA validation failed.";
            //    return CurrentUmbracoPage();
            //}

            // =========================
            // SPAM / SQL INJECTION FILTER
            // =========================
            string combinedInput = $"{name} {email} {companyName} {companySize} {subject} {subscription} {productType}".ToLower();

            string[] blockedPatterns =
            {
            "select ", "union ", "drop ", "sleep(", "pg_sleep",
            "--", "' or ", "insert ", "delete ", "update ",
            "xp_", "or 1=1", "\" or ", "/*", "*/", "exec("
        };

            if (blockedPatterns.Any(p => combinedInput.Contains(p)))
            {
                _logger.LogWarning($"Malicious input detected from IP: {ipAddress}");
                TempData["FormError"] = "Invalid input detected.";
                return CurrentUmbracoPage();
            }

            // =========================
            // SETTINGS
            // =========================
            var settinges = GetEmailSettings();
            if (settinges == null)
            {
                TempData["FormError"] = "Email settings not configured.";
                return CurrentUmbracoPage();
            }


            foreach (var pattern in blockedPatterns)
            {
                if (combinedInput.ToLower().Contains(pattern))
                {
                    TempData["FormError"] = "Invalid input detected.";
                    return CurrentUmbracoPage();
                }
            }

            var settings = GetEmailSettings();
            if (settings == null)
            {
                TempData["FormError"] = "Email settings are not configured.";
                return CurrentUmbracoPage(); ;
            }

            string url = string.Empty;

            var thankYouLink = settings.Value<IEnumerable<Link>>("thankYouPageUrl");
            url = thankYouLink?.FirstOrDefault()?.Url;


            _formService.SaveFormData(name, email, companyName, companySize, subject, subscription, productType, ipAddress);

            _formService.SendBrevoTemplateEmail(name, email);

            var template = _configuration["Umbraco:CMS:EmailTemplates:ContactInquiry"];

            template = ReplaceOrRemove(template, "FullName", "Full Name", name);
            template = ReplaceOrRemove(template, "Email", "Email", email);
            template = ReplaceOrRemove(template, "CompanyName", "Company Name", companyName);
            template = ReplaceOrRemove(template, "CompanySize", "Company Size", companySize);
            template = ReplaceOrRemove(template, "Subject", "Subject", subject);
            template = ReplaceOrRemove(template, "Subscription", "Subscription", subscription);
            template = ReplaceOrRemove(template, "ProductType", "Product Type", productType);

            // Send email
            _formService.SendEmail(template);


            return Redirect(url);

        }


        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult DemoForm(string firstName, string lastName, string email, string companyName, string companySize, string jobRole)
        {

            if (string.IsNullOrWhiteSpace(firstName) ||
                string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(companyName) ||
                string.IsNullOrWhiteSpace(companySize) ||
                string.IsNullOrWhiteSpace(jobRole))
            {
                return Json(new
                {
                    success = false,
                    message = "Fill out all required fields."
                });
            }
            // ✅ Inline Email Validation
            var emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            if (!Regex.IsMatch(email, emailPattern, RegexOptions.IgnoreCase))
            {
                return Json(new
                {
                    success = false,
                    message = "Please enter a valid email address."
                });
            }

            _formService.SaveDemoFormData(firstName, lastName, email, companyName, companySize, jobRole);

            return Json(new
            {
                success = true
            });


        }

        private IPublishedContent? GetEmailSettings()
        {
            if (!UmbracoContextAccessor.TryGetUmbracoContext(out var context))
                return null;

            return _contentQuery
          .ContentAtRoot()
          .FirstOrDefault(x => x.ContentType.Alias == "smtpEmailCredentials");
        }

        private string ReplaceOrRemove(string template, string placeholder, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                // Remove the entire <p> block if value is empty
                return Regex.Replace(
                    template,
                    $"<p><strong>{label}:</strong>\\s*{{{{{placeholder}}}}}</p>",
                    string.Empty,
                    RegexOptions.IgnoreCase
                );
            }

            return template.Replace($"{{{{{placeholder}}}}}", value);
        }


        private string GetClientIpAddress()
        {
            var context = HttpContext;

            // ✅ 1. Check X-Forwarded-For (Most common)
            var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            if (!string.IsNullOrEmpty(ip))
            {
                // If multiple IPs, take the first one
                return ip.Split(',').First().Trim();
            }

            // ✅ 2. Check X-Real-IP (Nginx / Cloudflare)
            ip = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(ip))
            {
                return ip;
            }

            // ✅ 3. Fallback (Local / Direct connection)
            return context.Connection.RemoteIpAddress?.ToString();
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SendResourcePdf(string email, string resourceId)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(resourceId))
            {
                return Json(new { success = false, message = "Invalid request" });
            }

            try
            {
                await _formService.SendResourcePdfToEmail(email, resourceId);

                return Json(new
                {
                    success = true,
                    message = "PDF sent successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending resource PDF");

                return Json(new
                {
                    success = false,
                    message = "Something went wrong"
                });
            }
        }
    }
}
