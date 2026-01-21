#nullable disable
using API_WEB.ModelsDB;
using API_WEB.ModelsOracle;
using API_WEB.Services.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using API_WEB.Controllers.Scrap;
using API_WEB.Controllers.SmartFA;
using API_WEB.Services.Bonepile;
using API_WEB.Services.SmartFA;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using API_WEB.Services.Authentication;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);

// Đảm bảo đọc api_appsettings.json
builder.Configuration
    .AddJsonFile("api_appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"api_appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

// Thêm CORS
var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(MyAllowSpecificOrigins, policy =>
    {
        policy.WithOrigins("http://localhost:5050", "http://10.220.130.119:8000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Thêm các dịch vụ
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });
    c.OperationFilter<MultipartFormDataOperationFilter>();

    //======== JWT in Swagger========
    var jwtScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập: Bearer {token}"
    };
    c.AddSecurityDefinition("Bearer", jwtScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [jwtScheme] = Array.Empty<string>()
    });
    //==================
});

// Cấu hình DbContext
builder.Services.AddDbContext<CSDL_NE>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("SQLServerConnection"), sqlOptions =>
    {
        sqlOptions.CommandTimeout(300);
    }));

builder.Services.AddDbContext<OracleDbContext>(options =>
options.UseOracle(
    builder.Configuration.GetConnectionString("OracleConnection"),
    oracleOptions =>
    {
        oracleOptions.UseOracleSQLCompatibility("11");
    }).EnableSensitiveDataLogging().LogTo(Console.WriteLine));

builder.Services.AddSingleton<ICellUpdateTracker, CellUpdateTracker>();
builder.Services.AddScoped<IBonepileAfterRepository, BonepileAfterRepository>();

//========================JWT========================
var jwtSection = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSection["Key"]!);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // true khi deploy https
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var jti = ctx.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                if (!string.IsNullOrEmpty(jti))
                {
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<CSDL_NE>();
                    var blocked = await db.Set<BlacklistedToken>().AnyAsync(x => x.Jti == jti);
                    if (blocked) ctx.Fail("Token is blacklisted");
                }
            }
        };
    });
builder.Services.AddAuthorization();



// Thêm các hosted services và HttpClient
builder.Services.AddHostedService<DailyTaskService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("RepairStatusClient", client =>
{
    client.BaseAddress = new Uri("https://sfc-portal.cns.myfiinet.com:443/SfcSmartRepair/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    return new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };
});

builder.Services.AddHostedService<AutoExportBackgroundService>();
builder.Services.AddSingleton<IWebHostEnvironment>(builder.Environment);
builder.Services.AddHostedService<ScrapTaskNumberAlertService>();
builder.Services.AddHostedService<TaskWaitMoveScrapStock>();
//builder.Services.AddHostedService<Alert>();
builder.Services.AddHostedService<ScrapToProduct>();
builder.Services.AddHostedService<LinkMoCheckingService>();
builder.Services.AddHostedService<BonepileWaitingSyncService>();
builder.Services.AddHostedService<BonepileAfterSnapshotService>();
builder.Services.AddHostedService<OtpCleanupService>();

// Đăng ký TokenService
builder.Services.AddSingleton<TokenService>();
var app = builder.Build();

app.UseRouting();
app.UseCors(MyAllowSpecificOrigins);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();




app.Run();

// Operation Filter để hỗ trợ multipart/form-data
public class MultipartFormDataOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var parameters = context.MethodInfo.GetParameters();
        if (parameters.Any(p => p.ParameterType == typeof(IFormFile) || p.ParameterType == typeof(IFormFileCollection)))
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["file"] = new OpenApiSchema { Type = "string", Format = "binary", Description = "Excel file to import" }
                            },
                            Required = new HashSet<string> { "file" }
                        }
                    }
                }
            };
        }
    }
}