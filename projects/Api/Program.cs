namespace Api;

using System.Text;
using Api.Configuration;
using Api.Data;
using Api.Engine;
using Api.Engine.Phases;
using Api.Security;
using Api.Utilities;
using Api.Types;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
        builder.Services.Configure<SeedDataOptions>(builder.Configuration.GetSection(SeedDataOptions.SectionName));
        builder.Services.Configure<VapidOptions>(builder.Configuration.GetSection("Vapid"));
        builder.Services.Configure<GameEngineOptions>(builder.Configuration.GetSection(GameEngineOptions.SectionName));
        builder.Services.Configure<MasterServerRegistrationOptions>(builder.Configuration.GetSection(MasterServerRegistrationOptions.SectionName));

        var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration is missing.");

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("frontend", policy =>
            {
                var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

                if (allowedOrigins.Length == 0)
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                    return;
                }

                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
            });
        });

        builder.Services.AddDbContext<AppDbContext>(options =>
        {
            if (builder.Environment.IsEnvironment("Testing"))
            {
                options.UseInMemoryDatabase("TestDb");
            }
            else
            {
                options.UseNpgsql(builder.Configuration.GetConnectionString("GameCatalog")
                    ?? throw new InvalidOperationException("Connection string 'GameCatalog' is missing."));
            }
        });
        builder.Services.AddScoped<AppDbInitializer>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHttpClient("push");
        builder.Services.AddHttpClient("master-server");
        builder.Services.AddScoped<WebPush.IWebPushClient>(serviceProvider =>
            new WebPush.WebPushClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("push")));

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                    ?? new JwtOptions();

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddScoped<TickProcessor>();
        builder.Services.AddHostedService<GameTickHostedService>();
        builder.Services.AddHostedService<MasterServerRegistrationHostedService>();

        builder.Services
            .AddGraphQLServer()
            .AddAuthorization()
            .AddQueryType<Query>()
            .AddMutationType<Mutation>();

        builder.Services.AddScoped<AppDbInitializer>();
        // ── Game tick engine ──
        builder.Services.AddScoped<TickProcessor>();
        builder.Services.AddScoped<ITickPhase, PowerDistributionPhase>();
        builder.Services.AddScoped<ITickPhase, ConstructionPhase>();
        builder.Services.AddScoped<ITickPhase, BuildingUpgradePhase>();
        builder.Services.AddScoped<ITickPhase, LandMarketPhase>();
        builder.Services.AddScoped<ITickPhase, PublicSalesPhase>();
        builder.Services.AddScoped<ITickPhase, ResourceMovementPhase>();
        builder.Services.AddScoped<ITickPhase, ManufacturingPhase>();
        builder.Services.AddScoped<ITickPhase, OperatingCostPhase>();
        builder.Services.AddScoped<ITickPhase, MiningPhase>();
        builder.Services.AddScoped<ITickPhase, PurchasingPhase>();
        builder.Services.AddScoped<ITickPhase, MarketingPhase>();
        builder.Services.AddScoped<ITickPhase, ResearchPhase>();
        builder.Services.AddScoped<ITickPhase, RentPhase>();
        builder.Services.AddScoped<ITickPhase, LoanRepaymentPhase>();
        builder.Services.AddScoped<ITickPhase, TaxPhase>();
        builder.Services.AddScoped<ITickPhase, DividendPhase>();
        builder.Services.AddHostedService<GameTickHostedService>();
        builder.Services.AddHostedService<MasterServerRegistrationHostedService>();

        var app = builder.Build();

        app.UseCors("frontend");
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/", () => Results.Ok(new
        {
            name = "Capitalism V Game API",
            graphql = "/graphql",
            health = "/healthz"
        }));

        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGraphQL();

        using (var scope = app.Services.CreateScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<AppDbInitializer>();
            await initializer.InitializeAsync();
        }

        await app.RunAsync();
    }
}
