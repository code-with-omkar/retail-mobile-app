using QuickCommerce.Api.Extensions;
using QuickCommerce.Application.DependencyInjection;
using QuickCommerce.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApiServices(builder.Configuration, builder.Environment)
    .AddApplicationServices()
    .AddInfrastructureServices(builder.Configuration);

var app = builder.Build();
app.UseApiPipeline();
app.Run();
