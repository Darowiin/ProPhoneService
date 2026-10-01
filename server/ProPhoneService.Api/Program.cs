using Microsoft.EntityFrameworkCore;
using ProPhoneService.Infrastructure.EfCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ProPhoneServiceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();

app.MapGet("/health", () => Results.Text("Healthy"));

app.Run();
