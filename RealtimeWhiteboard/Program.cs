using RealtimeWhiteboard.Components;
using RealtimeWhiteboard.Hubs;
using RealtimeWhiteboard.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR();

// Configure DbContext
var dbHost = Environment.GetEnvironmentVariable("DATABASE_HOST") ?? "localhost"; // From docker-compose or default
var dbName = Environment.GetEnvironmentVariable("DATABASE_NAME") ?? "whiteboard_db";
var dbUser = Environment.GetEnvironmentVariable("DATABASE_USER") ?? "whiteboard_user";
var dbPassword = Environment.GetEnvironmentVariable("DATABASE_PASSWORD") ?? "whiteboard_password";

// Note: For local dev without Docker Compose running, localhost might be used.
// When running with Docker Compose, DATABASE_HOST should be the service name (e.g., "db").
// The docker-compose.yml example doesn't yet set these for the app service, we'll assume "db" or rely on direct localhost for now if app isn't containerized yet.
// For simplicity, let's assume direct connection if app is run locally, and expect env vars if containerized.
string connectionString = $"Host={dbHost};Port=5432;Database={dbName};Username={dbUser};Password={dbPassword};";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Add ASP.NET Core Identity services
builder.Services.AddDefaultIdentity<IdentityUser>(options => {
    options.SignIn.RequireConfirmedAccount = false; // Simplification for now
    // Add other options like password requirements if needed
    // options.Password.RequireDigit = true;
    // options.Password.RequiredLength = 8;
})
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Add CascadingAuthenticationState for Blazor
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

// Add Authentication and Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

// Map Hubs
app.MapHub<WhiteboardHub>("/whiteboardhub");

// Map Razor Pages (needed for Identity UI if default UI is used/scaffolded)
app.MapRazorPages();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
