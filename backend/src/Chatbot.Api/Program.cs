using Chatbot.Application.Chat;
using Chatbot.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var envFilePath = FindEnvFile();
if (envFilePath is not null)
{
    var envValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
        {
            continue;
        }

        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = trimmed[..separatorIndex].Trim();
        var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"');
        envValues[key] = value;

        if (string.Equals(key, "GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", value);
            Environment.SetEnvironmentVariable("Gemini__ApiKey", value);
        }
        else if (string.Equals(key, "GEMINI_MODEL", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("GEMINI_MODEL", value);
            Environment.SetEnvironmentVariable("Gemini__Model", value);
        }
        else if (string.Equals(key, "LLM_PROVIDER", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("LLM_PROVIDER", value);
            Environment.SetEnvironmentVariable("LLMProvider", value);
        }
    }

    builder.Configuration.AddInMemoryCollection(envValues);
}

builder.Configuration.AddEnvironmentVariables();

// Add services to the container.

var allowedOrigins = ResolveAllowedOrigins(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Any(origin => origin == "*"))
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddScoped<ChatService>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "Chatbot.Api",
    status = "ok",
    endpoints = new[] { "/api/chat", "/api/chat/stream", "/healthz" }
}));

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    utc = DateTimeOffset.UtcNow
}));

app.MapControllers();

app.Run();

static string? FindEnvFile()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, ".env");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return null;
}

static string[] ResolveAllowedOrigins(IConfiguration configuration)
{
    var configured = configuration["CORS_ALLOWED_ORIGINS"]
        ?? configuration["Cors:AllowedOrigins"]
        ?? "http://localhost:4200";

    return configured
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .ToArray();
}
