using QuickCommerce.Api.Extensions;
using QuickCommerce.Application.DependencyInjection;
using QuickCommerce.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

if (QuickCommerce.Api.Hosting.CrashReporting.IsConfigured(builder.Configuration))
{
    builder.WebHost.UseSentry(options => QuickCommerce.Api.Hosting.CrashReporting.Configure(options, builder.Configuration, builder.Environment));
}

builder.Services
    .AddApiServices(builder.Configuration, builder.Environment)
    .AddApplicationServices()
    .AddInfrastructureServices(builder.Configuration);

var app = builder.Build();
app.UseApiPipeline();
app.Run();

public partial class Program;
