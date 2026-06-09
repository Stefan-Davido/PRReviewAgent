using PRReviewAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "PR Review Agent",
        Version = "v1",
        Description = "AI-powered Azure DevOps Pull Request reviewer using Claude"
    });
});

// Register PR review service as singleton (loads skill file once at startup)
builder.Services.AddSingleton<PRReviewService>();

var app = builder.Build();

// Swagger UI always on (restrict in production if needed)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "PR Review Agent v1");
    c.RoutePrefix = string.Empty; // Swagger at root "/"
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
