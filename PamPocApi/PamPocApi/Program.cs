using System.Text.Json;
using System.Text.Json.Serialization;
using PamPocApi.Configuration;
using PamPocApi.Middleware;
using PamPocApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddServiceConfiguration(builder.Configuration);

builder.Services.Configure<JsonSerializerOptions>("Web", options =>
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.PropertyNameCaseInsensitive = true;
});

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultPolicy", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [])
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

builder.Services.AddOpenApi();

builder.Services.AddScoped<ISpeechService, SpeechService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IPromptService, PromptService>();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("DefaultPolicy");

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();