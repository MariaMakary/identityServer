using Amazon;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Duende.IdentityServer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using identityServer.Config;
using identityServer.Data;
using identityServer.Endpoints;
using IdentityModel;
using identityServer.Models;
using identityServer.Services;
using Serilog;

var seqUrl = Environment.GetEnvironmentVariable("Serilog__SeqUrl") ?? "http://localhost:5341";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.Seq(seqUrl)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// 0. Load secrets from AWS Secrets Manager
try
{
    using var secretsClient = new AmazonSecretsManagerClient(RegionEndpoint.EUNorth1);
    var secretResponse = await secretsClient.GetSecretValueAsync(new GetSecretValueRequest
    {
        SecretId = "identityServer/secrets"
    });
    var secrets = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(secretResponse.SecretString);
    if (secrets != null)
    {
        foreach (var kvp in secrets)
        {
            builder.Configuration[kvp.Key] = kvp.Value;
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Could not load secrets from AWS Secrets Manager: {ex.Message}");
}

// 1. Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var dbPassword = builder.Configuration["DbPassword"];
if (!string.IsNullOrEmpty(dbPassword))
    connectionString += $";Password={dbPassword}";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. ASP.NET Core Identity
builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// 3. Duende IdentityServer
builder.Services.AddIdentityServer()
    .AddDeveloperSigningCredential()
    .AddInMemoryIdentityResources(IdentityServerConfig.IdentityResources)
    .AddInMemoryApiScopes(IdentityServerConfig.ApiScopes)
    .AddInMemoryApiResources(IdentityServerConfig.ApiResources)
    .AddInMemoryClients(IdentityServerConfig.Clients)
    .AddAspNetIdentity<User>();

// 4. Authentication — override Identity's cookie default with LocalApi
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityServerConstants.LocalApi.AuthenticationScheme;
    options.DefaultChallengeScheme = IdentityServerConstants.LocalApi.AuthenticationScheme;
})
.AddLocalApi(options =>
{
    options.ExpectedScope = "api";
});

// 5. Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOrOwner", policy =>
        policy.RequireAssertion(context =>
        {
            var userId = context.User.FindFirst(JwtClaimTypes.Subject)?.Value;
            var routeId = context.Resource switch
            {
                HttpContext httpContext => httpContext.Request.RouteValues["id"]?.ToString(),
                _ => null
            };

            return context.User.IsInRole("Admin") || userId == routeId;
        }));

    options.AddPolicy("ProjectOwner", policy =>
        policy.RequireAssertion(async context =>
        {
            if (context.User.IsInRole("Admin")) return true;

            var userId = context.User.FindFirst(JwtClaimTypes.Subject)?.Value;
            if (context.Resource is HttpContext http
                && int.TryParse(http.Request.RouteValues["id"]?.ToString(), out var projectId))
            {
                var db = http.RequestServices.GetRequiredService<AppDbContext>();
                var project = await db.Projects.FindAsync(projectId);
                if (project?.UserId == userId) return true;

                // Also allow project members
                return await db.ProjectMembers
                    .AnyAsync(m => m.ProjectId == projectId && m.UserId == userId);
            }
            return false;
        }));
});

// 6. Email Service
builder.Services.AddTransient<IEmailService, EmailService>();

// 7. AWS S3
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new AmazonS3Client(
        config["AWS:AccessKey"],
        config["AWS:SecretKey"],
        RegionEndpoint.GetBySystemName(config["AWS:Region"]));
});
builder.Services.AddScoped<IPhotoStorageService, S3PhotoStorageService>();

// 8. CORS
var corsOrigins = builder.Configuration.GetSection("CORS:Origins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Apply pending EF Core migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Seed roles
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles = { "Admin", "User" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

app.UseCors("AllowFrontend");
app.UseIdentityServer();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapProjectEndpoints();
app.MapInvitationEndpoints();

app.Run();
