using DotStarkWeb.Services;
using Microsoft.AspNetCore.Http;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<ISqliteFormService, SqliteFormService>();

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true; // require consent
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
});

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseCookiePolicy();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
