using Atmos.Core;
using Atmos.Web.Data;
using Atmos.Web.Endpoints;
using Atmos.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddDbContext<AtmosDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AtmosDb")));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AtmosDbContext>();

builder.Services.AddAtmosCoreServices(builder.Configuration);
builder.Services.AddAtmosWebServices(builder.Configuration);

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler("/Error");
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseMiddleware<SessionCookieMiddleware>();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapHealthChecks("/healthz");

app.MapWeatherEndpoints();
app.MapGeocodeEndpoints();
app.MapRecentEndpoints();
app.MapAirQualityEndpoints();
app.MapElevationEndpoints();
app.MapNearbyPlaceEndpoints();
app.MapRadarEndpoints();

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
