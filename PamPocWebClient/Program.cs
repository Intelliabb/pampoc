using PamPocWebClient.Components;
using PamPocWebClient.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Recorded audio arrives from the browser as a base64 string over the Blazor
// circuit, which is well past SignalR's 32 KB default.
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 20 * 1024 * 1024);

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5269";

builder.Services.AddHttpClient<IVoiceService, VoiceService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    // The local STT -> LLM -> TTS round trip can take a while on first run.
    client.Timeout = TimeSpan.FromMinutes(2);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
