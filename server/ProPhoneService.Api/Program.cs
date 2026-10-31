using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProPhoneService.Application;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Infrastructure.EfCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Строка подключения 'Default' не задана; задайте ConnectionStrings__Default в окружении");
}

builder.Services.AddRepositories(connectionString);
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddApplicationServices();

builder.Services.AddNotifier();

builder.Services.AddOptions<WorkshopOptions>()
    .Bind(builder.Configuration.GetSection("Workshop"))
    .Validate(o => o.Validate() is null, "Некорректные настройки Workshop")
    .ValidateOnStart();

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<WorkshopOptions>>().Value);

// Ключ подписи JWT - только в окружении; в Development - fallback-ключ
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Jwt:Key не задана; задайте Jwt__Key в окружении");
}
jwtKey ??= "dev-only-signing-key-not-for-production";

builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection("Auth"))
    .ValidateOnStart();

builder.Services.AddSingleton(new JwtOptions
{
    Key = jwtKey,
    Issuer = builder.Configuration["Jwt:Issuer"] ?? "ProPhoneService",
    Audience = builder.Configuration["Jwt:Audience"] ?? "ProPhoneService",
});

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AuthOptions>>().Value);

var app = builder.Build();

app.MapGet("/health", () => Results.Text("Healthy"));

app.MapControllers();

if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(connectionString))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ProPhoneServiceDbContext>();
    db.Database.Migrate();
}
app.Run();
