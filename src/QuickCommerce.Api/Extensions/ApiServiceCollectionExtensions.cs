using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using QuickCommerce.Api.Hosting;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.Interfaces;
using QuickCommerce.Application.Services;

namespace QuickCommerce.Api.Extensions;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddControllers();
        var delivery = configuration.GetSection(DeliverySettings.SectionName).Get<DeliverySettings>() ?? new DeliverySettings();
        delivery.Validate();
        services.AddSingleton(delivery);
        services.AddSingleton<IDeliveryEstimator, DeliveryEstimator>();
        var pricing = configuration.GetSection(PricingSettings.SectionName).Get<PricingSettings>()
            ?? throw new InvalidOperationException("The Pricing section (DeliveryFee, HandlingFee, FreeDeliveryThreshold) must be configured.");
        pricing.Validate();
        services.AddSingleton(pricing);
        var payments = configuration.GetSection(PaymentSettings.SectionName).Get<PaymentSettings>() ?? new PaymentSettings();
        payments.Validate();
        services.AddSingleton(payments);
        if (payments.Enabled)
        {
            services.AddHttpClient<IPaymentGateway, QuickCommerce.Infrastructure.Payments.RazorpayGateway>();
            services.AddHostedService<QuickCommerce.Api.Hosting.PaymentBackgroundService>();
        }
        else
        {
            services.AddSingleton<IPaymentGateway, QuickCommerce.Infrastructure.Payments.DisabledPaymentGateway>();
        }

        var addresses = configuration.GetSection(AddressSettings.SectionName).Get<AddressSettings>() ?? new AddressSettings();
        addresses.Validate();
        services.AddSingleton(addresses);
        var account = configuration.GetSection(AccountSettings.SectionName).Get<AccountSettings>() ?? new AccountSettings();
        account.Validate();
        services.AddSingleton(account);
        services.AddSingleton(configuration.GetSection(RegistrationSettings.SectionName).Get<RegistrationSettings>() ?? new RegistrationSettings());
        var otp = configuration.GetSection(OtpSettings.SectionName).Get<OtpSettings>() ?? new OtpSettings();
        otp.Validate();
        services.AddSingleton(otp);
        services.AddSingleton(configuration.GetSection(GoogleSettings.SectionName).Get<GoogleSettings>() ?? new GoogleSettings());
        if (string.Equals(otp.Sender, "Msg91", StringComparison.OrdinalIgnoreCase))
        {
            var msg91 = configuration.GetSection(QuickCommerce.Infrastructure.Security.Msg91Settings.SectionName).Get<QuickCommerce.Infrastructure.Security.Msg91Settings>() ?? new QuickCommerce.Infrastructure.Security.Msg91Settings();
            msg91.Validate();
            services.AddSingleton(msg91);
            services.AddHttpClient<IOtpSender, QuickCommerce.Infrastructure.Security.Msg91OtpSender>();
        }
        else if (environment.IsDevelopment())
        {
            services.AddSingleton<IOtpSender, QuickCommerce.Infrastructure.Security.LogOtpSender>();
        }
        else
        {
            throw new InvalidOperationException("Otp:Sender must be Msg91 outside Development: the log sender would write sign-in codes to the log.");
        }

        var email = configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>() ?? new EmailSettings();
        if (!environment.IsDevelopment())
        {
            email.ValidateForNonDevelopment();
        }

        services.AddSingleton(email);
        services.AddAuthRateLimiting(configuration);
        services.AddTrustedProxies(configuration);
        ValidateProductionConfiguration(configuration, environment);
        services.AddHealthChecks();
        services.AddHostedService<QuickCommerce.Api.Hosting.CampaignBackgroundService>();
        services.AddHostedService<ShutdownLoggingHostedService>();
        services.AddOpenApi();
        services.AddHttpContextAccessor();
        services.AddSingleton<QuickCommerce.Infrastructure.Media.LocalImageStore>(provider => new QuickCommerce.Infrastructure.Media.LocalImageStore(configuration["Media:RootPath"] ?? Path.Combine(environment.ContentRootPath, "media")));
        services.AddSingleton<IImageStore>(provider => provider.GetRequiredService<QuickCommerce.Infrastructure.Media.LocalImageStore>());
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => ConfigureJwt(options, configuration, environment));
        services.AddAuthorization(options =>
        {
            options.AddPolicy(SecurityPolicies.Orders, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    context.User.IsInRole("Customer") ||
                    context.User.IsInRole("StoreStaff") ||
                    context.User.IsInRole("Admin") ||
                    context.User.IsInRole("ApplicationAdmin") ||
                    context.User.Claims.Any(claim => claim.Type == "permission" && claim.Value == "orders:read")));
            options.AddPolicy(SecurityPolicies.StoreOrderOperations, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    context.User.IsInRole("StoreStaff") ||
                    context.User.IsInRole("Admin") ||
                    context.User.Claims.Any(claim => claim.Type == "permission" && claim.Value == "orders:operate")));
            options.AddPolicy(SecurityPolicies.AdminManagement, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    context.User.IsInRole("Admin") ||
                    context.User.IsInRole("ApplicationAdmin") ||
                    context.User.Claims.Any(claim => claim.Type == "permission" && claim.Value == "users:manage") ||
                    context.User.Claims.Any(claim => claim.Type == "permission" && claim.Value == "roles:manage") ||
                    context.User.Claims.Any(claim => claim.Type == "permission" && claim.Value == "permissions:manage")));
            options.AddPolicy(SecurityPolicies.OfferManagement, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    context.User.IsInRole("ApplicationAdmin") ||
                    context.User.IsInRole("Admin") ||
                    context.User.IsInRole("StoreStaff")));
            options.AddPolicy(SecurityPolicies.CatalogManagement, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.IsInRole("ApplicationAdmin") || context.User.IsInRole("Admin")));
            options.AddPolicy(SecurityPolicies.StockManagement, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.IsInRole("ApplicationAdmin") || context.User.IsInRole("Admin") || context.User.IsInRole("StoreStaff")));
            options.AddPolicy(SecurityPolicies.DeliveryPartner, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.IsInRole("DeliveryPartner")));
            options.AddPolicy(SecurityPolicies.AdminDataRead, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    context.User.IsInRole("ApplicationAdmin") ||
                    context.User.IsInRole("Admin") ||
                    context.User.IsInRole("StoreStaff") ||
                    context.User.Claims.Any(claim => claim.Type == "permission" && claim.Value == "Store.Read")));
        });
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter a JWT bearer token."
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });
        var corsOrigins = ApiCors.ParseOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>());
        services.AddCors(options => options.AddDefaultPolicy(ApiCors.Build(environment.IsDevelopment(), corsOrigins)));
        services.AddSingleton(new CorsOriginsInfo(corsOrigins.Count));
        return services;
    }

    private static void ConfigureJwt(JwtBearerOptions options, IConfiguration configuration, IHostEnvironment environment)
    {
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];
        var signingKey = configuration["Jwt:SigningKey"];
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey must be configured outside Development.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
            ValidIssuer = issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            ValidAudience = audience,
            ValidateIssuerSigningKey = !string.IsNullOrWhiteSpace(signingKey),
            IssuerSigningKey = string.IsNullOrWhiteSpace(signingKey) ? null : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    private static void ValidateProductionConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var requiredSettings = new[]
        {
            "Jwt:Issuer",
            "Jwt:Audience",
            "Jwt:SigningKey",
            "ConnectionStrings:QuickCommerceDb"
        };
        var missingSettings = requiredSettings.Where(setting => string.IsNullOrWhiteSpace(configuration[setting])).ToArray();
        if (missingSettings.Length > 0)
        {
            throw new InvalidOperationException($"Required production settings are missing: {string.Join(", ", missingSettings)}");
        }
    }
}
