using FileClassification.Application;
using FileClassification.Application.Settings;
using FileClassification.Infrastructure;
using FileClassification.Infrastructure.Data;
using FileClassification.Worker.Settings;
using FileClassification.Worker.Workers;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, cfg) => cfg.ReadFrom.Configuration(builder.Configuration));

builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection("WorkerSettings"));
builder.Services.Configure<LanguageDetectorSettings>(builder.Configuration.GetSection("LanguageDetector"));

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration.GetConnectionString("DefaultConnection")!);

builder.Services.AddHostedService<FileProcessingWorker>();
builder.Services.AddHostedService<StalledFileResetWorker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

host.Run();
