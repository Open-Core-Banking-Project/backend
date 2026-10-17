using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "Healthy" }));

app.Run();
