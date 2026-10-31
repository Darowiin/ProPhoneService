using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProPhoneService.Api.Auth;
using ProPhoneService.Domain.Shared.Auditing;
using ProPhoneService.Application;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Infrastructure.EfCore;
using System.ComponentModel.DataAnnotations;
using System.Text;
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
    .Validate(o => string.IsNullOrWhiteSpace(o.MasterEmail) || new EmailAddressAttribute().IsValid(o.MasterEmail),
        "Некорректный Auth:MasterEmail - укажите email мастера или оставьте пустым")
    .ValidateOnStart();

var jwtOptions = new JwtOptions
{
    Key = jwtKey,
    Issuer = builder.Configuration["Jwt:Issuer"] ?? "ProPhoneService",
    Audience = builder.Configuration["Jwt:Audience"] ?? "ProPhoneService",
};
builder.Services.AddSingleton(jwtOptions);

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AuthOptions>>().Value);

// Проверка access-токена: симметричный ключ, issuer/audience, роль из клейма "role"
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key!)),
            NameClaimType = AuthClaims.Subject,
            RoleClaimType = AuthClaims.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// Аккаунт запроса - источник полей аудита "кто изменил"
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, HttpContextCurrentActor>();

var app = builder.Build();

app.MapGet("/health", () => Results.Text("Healthy"));

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (!string.IsNullOrWhiteSpace(connectionString))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ProPhoneServiceDbContext>();

    // Автомиграции в Development; Database__MigrateOnStartup=false отключает (тесты)
    if (app.Environment.IsDevelopment() && builder.Configuration.GetValue("Database:MigrateOnStartup", true))
    {
        db.Database.Migrate();
    }

    // Мастер-аккаунт: апсерт роли Master для Auth:MasterEmail (пусто - пропуск)
    await scope.ServiceProvider.GetRequiredService<MasterAccountSeeder>().EnsureAsync();
}
app.Run();
