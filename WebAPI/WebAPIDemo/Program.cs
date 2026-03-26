using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using WebAPIDemo.Data;

IConfiguration configuration = new ConfigurationBuilder()
                            .AddJsonFile("appsettings.json")
                            .Build();

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("ShirtStoreManagement"));
});

// Add services to the container.
builder.Services.AddControllers();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes.TryAdd("Bearer", new OpenApiSecurityScheme
        {
            Scheme = "Bearer",
            Type = SecuritySchemeType.Http,
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
        });

        document.SecurityRequirements.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }] = new string[] { }
        });

        return Task.CompletedTask;
    });
});

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("x-api-version"),
        new QueryStringApiVersionReader("api-version")
    );
})
.AddApiExplorer();

AppSta.AddServices(builder.Services, configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.Urls.Add("http://0.0.0.0:5250");

var apiVersionDescriptionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

if (app.Environment.IsDevelopment())
{
   Console.WriteLine("Discovered API versions:");
   foreach(var description in apiVersionDescriptionProvider.ApiVersionDescriptions)
   {
       Console.WriteLine($"- {description.GroupName} (v{description.ApiVersion})");
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "WebAPIDemo API v1");
        options.SwaggerEndpoint("/openapi/v2.json", "WebAPIDemo API v2");
    });
}
// 添加自定义中间件（放在 UseRouting 之前）
app.Use(async (context, next) =>
{
    // 在这里定义你允许的路径列表
    var allowedPaths = new[] { "/yangai/ota", "/yangai/ota/", "/api/test", "/health" };

    var currentPath = context.Request.Path.Value?.ToLower();

    if (allowedPaths.Contains(currentPath))
    {
        await next(); // 允许的路径，正常处理
    }
    else
    {
        // 其他所有路径返回空白
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(string.Empty);
    }
});
//app.UseHttpsRedirection();

app.MapControllers();

app.Run();


