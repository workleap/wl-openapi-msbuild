using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "V1 API General", Version = "v1" });
    c.SwaggerDoc("v1-management", new OpenApiInfo { Title = "V1 API management", Version = "v1-management" });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("v1/swagger.json", "v1");
    options.SwaggerEndpoint("v1-management/swagger.json", "v1-management");
});

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
