using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PESystem.Models;
using PESystem.Areas.NPI.Services;
using PESystem.Policies;

var builder = WebApplication.CreateBuilder(args);

// ===== FIX UPLOAD LIMIT (Kestrel) =====
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 209715200; // 200 MB
});

// ===== FIX UPLOAD LIMIT (Form) =====
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 200 * 1024 * 1024;
});

// ---- Config ----
var configuration = new ConfigurationBuilder()
    .AddJsonFile("mvc_appsettings.json", optional: false, reloadOnChange: true)
    .Build();

builder.Services.AddHttpContextAccessor();
builder.Services.AddControllersWithViews();

builder.Services.AddSingleton<NpiDocumentService>();

// ---- HttpClient: gộp làm 1, vừa có BaseAddress vừa bypass proxy ----
builder.Services.AddHttpClient("ApiClient", client =>
{
    client.BaseAddress = new Uri(configuration["ApiBaseUrl"]!); // vd: http://10.220.130.119:9090/
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    Proxy = null,
    UseProxy = false
});

// ---- (Tuỳ nhu cầu) DbContext riêng của MVC ----
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("cnn")));

// ---- Authorization theo Area ----
builder.Services.AddSingleton<IAuthorizationHandler, AreaAccessHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SmartFAAccess", p => p.Requirements.Add(new AreaAccessRequirement("SmartFA")));
    options.AddPolicy("RepositoriesAccess", p => p.Requirements.Add(new AreaAccessRequirement("Repositories")));
    options.AddPolicy("PDRepositoriesAccess", p => p.Requirements.Add(new AreaAccessRequirement("PDRepositories")));
    options.AddPolicy("DataCloudAccess", p => p.Requirements.Add(new AreaAccessRequirement("DataCloud")));
    options.AddPolicy("HassBiAccess", p => p.Requirements.Add(new AreaAccessRequirement("HassBi")));
    options.AddPolicy("DDRAccess", p => p.Requirements.Add(new AreaAccessRequirement("DDR")));
    options.AddPolicy("CheckListAccess", p => p.Requirements.Add(new AreaAccessRequirement("CheckList")));
    options.AddPolicy("ScrapAccess", p => p.Requirements.Add(new AreaAccessRequirement("Scrap")));
    options.AddPolicy("MaterialSystemAccess", p => p.Requirements.Add(new AreaAccessRequirement("MaterialSystem")));
    options.AddPolicy("SFCAccess", p => p.Requirements.Add(new AreaAccessRequirement("SFC")));
    options.AddPolicy("NPIAccess", p => p.Requirements.Add(new AreaAccessRequirement("NPI")));
});

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// ---- Cookie Authentication: đặt hạn & sliding ----
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Auth/Login";
        o.LogoutPath = "/Auth/Logout";
        o.AccessDeniedPath = "/Auth/AccessDenied";
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);   // thoi gian giu session
        // Nếu chạy HTTPS:
        // o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        // o.Cookie.SameSite     = SameSiteMode.Lax;
    });

// ---- Form & CORS (MVC call API server-side nên CORS không cần thiết) ----
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 200 * 1024 * 1024);
// Có thể bỏ CORS ở MVC nếu chỉ server-side call API
builder.Services.AddCors(o => o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors("AllowAll");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ---- Routes ----
// 1) Areas
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

// 2) Default -> về Home/Index (không phải Auth/Login)
//    Home/Index nên có [Authorize] để tự chuyển về Login nếu chưa đăng nhập.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Home}/{id?}");

app.Run();
