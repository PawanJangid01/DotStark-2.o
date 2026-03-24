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
namespace DotStarkWeb.Controllers
{
    public class FormController : SurfaceController
    {
        private readonly IPublishedContentQuery _contentQuery;
        private readonly ISqliteFormService _formService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FormController> _logger;
        public FormController(
            ISqliteFormService formService,
            IConfiguration configuration,
            IPublishedContentQuery contentQuery,
              ILogger<FormController> logger,
            IUmbracoContextAccessor umbracoContextAccessor,
            IUmbracoDatabaseFactory databaseFactory,
            ServiceContext serviceContext,
            AppCaches appCaches,
            IProfilingLogger profilingLogger,
            IPublishedUrlProvider publishedUrlProvider)
            : base(umbracoContextAccessor, databaseFactory, serviceContext, appCaches, profilingLogger, publishedUrlProvider)
        {
            _formService = formService;
            _contentQuery = contentQuery;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult SubmitForm(string name, string email, string company, string message)
        {

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(company))
            {
                return Json(new
                {
                    success = false,
                    message = "Fill out all required fields."
                });
            }
            //if (!Regex.IsMatch(contactNumber, @"^\d{10}$"))
            //{
            //    return Json(new { success = false, message = "Invalid phone number" });
            //}


            var settings = GetEmailSettings();
            if (settings == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Email settings are not configured."
                });
            }

            string url = string.Empty;

            var thankYouLink = settings.Value<IEnumerable<Link>>("thankYouPageUrl");
                url = thankYouLink?.FirstOrDefault()?.Url;
            
            

            _formService.SaveFormData(name, email, company, message);

            _formService.SendBrevoTemplateEmail(name, email);

            // Send email
            _formService.SendBrevoTemplateEmailToAdmin(name, email, company, message);


            return Redirect(url);

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

    }
}
