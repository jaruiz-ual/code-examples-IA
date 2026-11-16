using BlazorOllamaStudio.Components;
using BlazorOllamaStudio.Data;
using BlazorOllamaStudio.Services;
using Microsoft.EntityFrameworkCore;
using OllamaSharp;

var builder = WebApplication.CreateBuilder(args);

// 1. SQL Server LocalDB con DbContextFactory para evitar conflictos de concurrencia en SignalR
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Cliente oficial OllamaSharp
var ollamaUri = builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
builder.Services.AddSingleton<IOllamaApiClient>(_ =>
{
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(ollamaUri),
        Timeout = TimeSpan.FromMinutes(10)
    };
    return new OllamaApiClient(httpClient);
});

// 3. Servicios de aplicación
builder.Services.AddScoped<DocumentIngestionService>();
builder.Services.AddScoped<RagSql2019Service>();
builder.Services.AddScoped<ModeloGgufService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Inicialización automática de la base de datos y tabla en SQL Server LocalDB
using (var scope = app.Services.CreateScope())
{
    try
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "No se pudo verificar/crear la base de datos LocalDB al iniciar. Verifica que (localdb)\\mssqllocaldb esté activo.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
