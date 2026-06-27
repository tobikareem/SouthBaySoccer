using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Infrastructure;
using SouthBaySoccer.Infrastructure.Authentication;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.AddSouthBaySoccerHttpPipeline();
builder.Services.Configure<JwtTokenOptions>(builder.Configuration.GetSection("Authentication:Jwt"));

var connectionString = builder.Configuration.GetConnectionString("SouthBaySoccerDb")
    ?? builder.Configuration["ConnectionStrings:SouthBaySoccerDb"];

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Missing ConnectionStrings:SouthBaySoccerDb. Configure it in user secrets, local.settings.json, or environment variables.");
}

builder.Services.AddInfrastructure(connectionString);

builder.Build().Run();
