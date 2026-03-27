using System.Text;
using Api.Configuration;
using Api.Data;
using Api.Security;
using Api.Utilities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<SeedDataOptions>(builder.Configuration.GetSection(SeedDataOptions.SectionName));
builder.Services.Configure<VapidOptions>(builder.Configuration.GetSection("Vapid"));

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

        // Separate exact origins from wildcard patterns (e.g. "https://*.events.biatec.io").
        // Wildcard entries use SetIsOriginAllowed so that every category subdomain
        // passes the CORS preflight check without listing each one individually.
        var exactOrigins = allowedOrigins.Where(o => !o.Contains('*')).ToArray();
        var wildcardSuffixes = allowedOrigins
            .Where(o => o.StartsWith("https://*.", StringComparison.Ordinal)
                     || o.StartsWith("http://*.", StringComparison.Ordinal))
            .Select(o =>
            {
                if (!Uri.TryCreate(o.Replace("*.", ""), UriKind.Absolute, out var uri))
                    return ((string Scheme, string HostSuffix, int Port)?)null;
                return (Scheme: uri.Scheme, HostSuffix: "." + uri.Host, Port: uri.Port);
            })
            .Where(entry => entry is not null)
            .Select(entry => entry!.Value)
            .ToArray();

        if (wildcardSuffixes.Length == 0)
        {
            // No wildcard patterns — keep the simple exact-match path.
            policy.WithOrigins(exactOrigins).AllowAnyHeader().AllowAnyMethod();
            return;
        }

        policy
            .SetIsOriginAllowed(origin =>
            {
                if (exactOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                    return true;

                if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
                    return false;

                foreach (var (scheme, hostSuffix, port) in wildcardSuffixes)
                {
                    if (string.Equals(originUri.Scheme, scheme, StringComparison.OrdinalIgnoreCase)
                        && originUri.Port == port
                        && originUri.Host.EndsWith(hostSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.UseInMemoryDatabase("events-tests");
    }
    else
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("EventsCatalog")
            ?? throw new InvalidOperationException("Connection string 'EventsCatalog' is missing."));
    }
});

builder.Services.AddScoped<AppDbInitializer>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("push");
builder.Services.AddScoped<WebPush.IWebPushClient>(serviceProvider =>
    new WebPush.WebPushClient(
        serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("push")));


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
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<Api.Types.Query>()
    .AddMutationType<Api.Types.Mutation>();

var app = builder.Build();

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    name = "Capitalism V API",
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

app.Run();

public partial class Program;
