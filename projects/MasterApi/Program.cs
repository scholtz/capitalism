namespace MasterApi;

using System.Text;
using MasterApi.Configuration;
using MasterApi.Data;
using MasterApi.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<MasterServerOptions>(
            builder.Configuration.GetSection(MasterServerOptions.SectionName));

        builder.Services.Configure<JwtOptions>(
            builder.Configuration.GetSection(JwtOptions.SectionName));

        var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();

        // Fail fast: prevent using the default JWT signing key in non-Development environments.
        // If the key has not been changed from the shipped default, tokens could be forged by anyone
        // who has read this source. Deployment must supply a strong unique key via configuration or
        // environment variables (Jwt__SigningKey).
        if (!builder.Environment.IsDevelopment()
            && !builder.Environment.IsEnvironment("Testing")
            && string.Equals(jwtOptions.SigningKey, JwtOptions.DefaultSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The JWT SigningKey has not been changed from its default value. " +
                "Set a strong unique secret in the 'Jwt:SigningKey' configuration entry " +
                "(or environment variable 'Jwt__SigningKey') before running outside Development.");
        }

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

        builder.Services.AddDbContext<MasterDbContext>(options =>
        {
            if (builder.Environment.IsEnvironment("Testing"))
            {
                options.UseSqlite(builder.Configuration.GetConnectionString("MasterCatalog")
                    ?? throw new InvalidOperationException("Connection string 'MasterCatalog' is missing."));
            }
            else
            {
                options.UseNpgsql(builder.Configuration.GetConnectionString("MasterCatalog")
                    ?? throw new InvalidOperationException("Connection string 'MasterCatalog' is missing."));
            }
        });

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
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
        builder.Services.AddHttpClient("master-server");

        builder.Services
            .AddGraphQLServer()
            .AddAuthorization()
            .AddQueryType<MasterApi.Types.Query>()
            .AddMutationType<MasterApi.Types.Mutation>();

        builder.Services.AddScoped<MasterDbInitializer>();

        var app = builder.Build();

        app.UseCors("frontend");
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/", () => Results.Ok(new
        {
            name = "Capitalism Master API",
            graphql = "/graphql",
            health = "/healthz"
        }));

        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGraphQL();

        using (var scope = app.Services.CreateScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<MasterDbInitializer>();
            await initializer.InitializeAsync();
        }

        await app.RunAsync();
    }
}