using FileSite.Data;
using FileSite.Data.Interfaces;
using FileSite.Models;
using FileSite.Repositories;
using FileSite.Services;
using FileSite.Services.EmailServicing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;


var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();


builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IFileDataRepository, FileDataRepository>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "FileLifetimes_";
});

// Register IConnectionMultiplexer for direct Redis access (Sorted Sets)
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => 
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")));

builder.Services.AddSingleton<GlobalDataRepository>();
builder.Services.AddSingleton<FileTypeCounter>()
                .AddHostedService<FileTypeCounter>(provider => provider.GetService<FileTypeCounter>());
builder.Services.AddSingleton<EmailService>()
                .AddHostedService<EmailService>(provider => provider.GetService<EmailService>());
builder.Services.AddHostedService<FileCleanup>();

builder.Services.AddIdentity<AppUser, IdentityRole>(op => op.User.RequireUniqueEmail=true)
                .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddMemoryCache();
builder.Services.AddSession();
builder.Services.AddSerilog();//redirects normal logs to serilog
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.UseAuthentication();

app.UseSerilogRequestLogging();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

//await Task.FromResult(() => Seed.SeedData(app)); DEPRICATED//////for task version
Seed.SeedData(app);

Log.Information("helo");
app.Run();
