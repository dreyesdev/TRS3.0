using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TRS2._0.Models.DataModels;
using TRS2._0.Services;
using Quartz;
using Quartz.Impl;
using TRS2._0.Models.DataModels.TRS2._0.Models.DataModels;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Configuración de Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()    
    //.WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();



// Configuración de Kestrel para usar HTTPS
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(5000); // HTTP
    serverOptions.ListenAnyIP(5001, listenOptions =>
    {
        //listenOptions.UseHttps("C:\\Users\\premo\\source\\repos\\trs3.0nuevo\\TRS2.0\\opstrs03.bsc.es.pfx", "seidor");// Colegio
        //listenOptions.UseHttps("C:\\Users\\dreyes\\Desktop\\Desarrollo\\TRS2.0\\TRS2.0\\Resources\\opstrs03.bsc.es.pfx", "seidor");//Casa 
        listenOptions.UseHttps("C:\\Users\\dreyes\\Source\\Repos\\trs3.0\\TRS2.0\\Resources\\opstrs03.bsc.es.pfx", "seidor");//Casa

    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<TRSDBContext>(options =>
    options.UseSqlServer(connectionString)
           .EnableSensitiveDataLogging()); // Activa Sensitive Data Logging

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 6;
    options.Password.RequiredUniqueChars = 1;
})
    .AddEntityFrameworkStores<TRSDBContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<IdentityOptions>(options =>
{
    options.SignIn.RequireConfirmedEmail = false;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60); // Expira la sesión tras 60 minutos de inactividad
    options.SlidingExpiration = true; // No renueva la sesión automáticamente
    options.Cookie.HttpOnly = true; // Protege la cookie contra accesos de JavaScript
    options.Cookie.IsEssential = true; // Se mantiene en todas las políticas de privacidad
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Requiere HTTPS
    options.Cookie.SameSite = SameSiteMode.Strict; // Evita el acceso de otras aplicaciones
    options.LoginPath = "/Account/Login"; // Redirige al login tras expiración
    options.AccessDeniedPath = "/Error/AccessDenied"; // Página de error si no tiene permisos
});


builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ProjectManagerOrAdminPolicy", policy =>
        policy.RequireRole("ProjectManager", "Admin"));
    options.AddPolicy("AdminPolicy", policy =>
        policy.RequireRole("Admin"));
    options.AddPolicy("AllowLogsPolicy", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole("Admin") ||
            context.Resource.ToString() == "/Admin/GetLogFiles" ||
            context.Resource.ToString().StartsWith("/Admin/GetLogFileContent")));
});

builder.Services.AddScoped<WorkCalendarService>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddScoped<LoadDataService>();

// Envio de Correos
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddTransient<IEmailSender, EmailSender>();

// Configuración de Quartz para ejecutar `LoadDataService` diariamente a la 1:00 AM
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    var jobKey = new JobKey("LoadDataServiceJob");
    q.AddJob<LoadDataService>(opts => opts
        .WithIdentity(jobKey)
        .StoreDurably());

    q.AddTrigger(opts => opts
       .ForJob(jobKey)
       .WithIdentity("DailyLoadTrigger")
       .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(1, 00)
           .WithMisfireHandlingInstructionFireAndProceed()) // 1:00 AM con manejo de misfire
       .StartNow());

    var reminderJobKey = new JobKey("TimesheetReminderJob");
    q.AddJob<TimesheetReminderJob>(opts => opts
        .WithIdentity(reminderJobKey)
        .StoreDurably());

    q.AddTrigger(opts => opts
        .ForJob(reminderJobKey)
        .WithIdentity("WeeklyTimesheetReminder_Mon0900")
        .WithSchedule(
            CronScheduleBuilder
                .CronSchedule("0 0 9 ? * MON") // Lunes 09:00
                .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid"))
                .WithMisfireHandlingInstructionFireAndProceed()
        )
        .StartNow());

});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

var app = builder.Build();

try
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<TRSDBContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        context.Database.Migrate();
        SeedData.Initialize(context, userManager, roleManager).Wait();
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    // Asegúrate de que todos los usuarios deben estar autenticados
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        if (!context.User.Identity.IsAuthenticated && !path.StartsWithSegments("/Account") && !path.StartsWithSegments("/Error"))
        {
            context.Response.Redirect("/Account/Login");
            return;
        }
        await next();
    });

    app.UseStatusCodePagesWithReExecute("/Error/AccessDenied", "?statusCode={0}");

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public static class SeedData
{
    public static async Task Initialize(TRSDBContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        string[] roles = new string[] { "Admin", "ProjectManager", "Researcher", "User", "Leader" };

        foreach (string role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var adminUser = new ApplicationUser
        {
            UserName = "iss",
            Email = "iss@bsc.es",
            PersonnelId = 1,
            EmailConfirmed = true
        };

        if (userManager.Users.All(u => u.Id != adminUser.Id))
        {
            var user = await userManager.FindByEmailAsync(adminUser.Email);
            if (user == null)
            {
                await userManager.CreateAsync(adminUser, "Admin123!");
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }

        // ?? NUEVO BLOQUE: asignar rol Leader automáticamente
        var leaderIds = await context.Leaders.Select(l => l.LeaderId).ToListAsync();

        foreach (var leaderId in leaderIds)
        {
            var person = await context.Personnel.FirstOrDefaultAsync(p => p.Id == leaderId);
            if (person == null || string.IsNullOrEmpty(person.Email))
                continue;

            var user = await userManager.FindByEmailAsync(person.Email);
            if (user != null && !await userManager.IsInRoleAsync(user, "Leader"))
            {
                await userManager.AddToRoleAsync(user, "Leader");
            }
        }
    }

}
