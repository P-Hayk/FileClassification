using FileClassification.Application;
using FileClassification.Application.Settings;
using FileClassification.Infrastructure;
using FileClassification.Infrastructure.Data;
using Microsoft.AspNetCore.Http.Features;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

var maxBodyBytes = builder.Configuration.GetValue<long>("UploadLimits:MaxRequestBodySizeBytes");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxBodyBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxBodyBytes);

builder.Services.Configure<LanguageDetectorSettings>(builder.Configuration.GetSection("LanguageDetector"));
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration.GetConnectionString("DefaultConnection")!);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseSerilogRequestLogging();
app.MapControllers();
app.Run();
