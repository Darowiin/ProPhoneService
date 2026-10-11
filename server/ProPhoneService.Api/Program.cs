using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProPhoneService.Application;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Infrastructure.EfCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRepositories(builder.Configuration.GetConnectionString("Default"));
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddApplicationServices();

builder.Services.AddOptions<WorkshopOptions>()
    .Bind(builder.Configuration.GetSection("Workshop"))
    .Validate(o => o.Validate() is null, "Некорректные настройки Workshop")
    .ValidateOnStart();

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<WorkshopOptions>>().Value);

var app = builder.Build();

app.MapGet("/health", () => Results.Text("Healthy"));

app.MapControllers();

if (app.Environment.IsDevelopment()
    && !string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Default")))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ProPhoneServiceDbContext>();
    db.Database.Migrate();
}
app.Run();
