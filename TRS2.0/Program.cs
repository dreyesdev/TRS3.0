using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Data;
using TRS2._0.Models.DataModels;
using TRS2._0.Services;
using Quartz;
using GSS.Authentication.CAS.AspNetCore;



var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<TRSDBContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<TRSDBContext>();
builder.Services.AddControllersWithViews();

// Añadir tu servicio personalizado aquí
builder.Services.AddScoped<WorkCalendarService>();
builder.Services.AddScoped<LoadDataService>();
builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders(); // Opcional, elimina los proveedores existentes
    loggingBuilder.AddConsole(); // Agrega logging a la consola
});

// Configuración de Quartz
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    // Define un JobKey único para tu trabajo
    var jobKey = new JobKey("LoadDataServiceJob");

    // Registra el trabajo como durable
    q.AddJob<LoadDataService>(opts => opts
        .WithIdentity(jobKey)
        .StoreDurably()); // Marca el trabajo como durable
});


// Configuración de servicios de autenticación CAS
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "Cookies";
    options.DefaultChallengeScheme = "CAS";
})
.AddCookie("Cookies")
.AddCAS(options =>
{
    options.CasServerUrlBase = "https://cas.example.com/cas";  // Ajusta a la URL de tu servidor CAS
    options.SaveTokens = true;
    options.Events.OnCreatingTicket = context =>
    {
        // Configura aquí cómo se manejan los tickets y los atributos de usuario
        return Task.CompletedTask;
    };
});


builder.Services.AddQuartzHostedService(
    q => q.WaitForJobsToComplete = true);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();


app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();
