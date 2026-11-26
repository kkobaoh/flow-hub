using FlowHub.Api.Composition;

var builder = WebApplication.CreateBuilder(args);
var settings = FlowHubSettings.Read(builder.Configuration, builder.Environment.ContentRootPath);
FlowHubSystem.AddScreenServices(builder.Services, settings.Database);
var app = builder.Build();

using var system = await FlowHubSystem.StartAsync(
    settings, TimeProvider.System, app.Services.GetRequiredService<ILoggerFactory>(), app.Lifetime.ApplicationStopping);
system.MapEndpoints(app);

await app.RunAsync();
