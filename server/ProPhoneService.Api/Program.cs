using Microsoft.EntityFrameworkCore;
using ProPhoneService.Infrastructure.EfCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ProPhoneServiceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddRepositories();

var app = builder.Build();

app.MapGet("/health", () => Results.Text("Healthy"));

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ProPhoneServiceDbContext>();
    db.Database.Migrate();
}
app.Run();
