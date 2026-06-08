using ITInventorySystem.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddScoped<DbHelper>();

builder.Services.AddScoped<PrinterRepository>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();

    string connString =
        configuration.GetConnectionString("DefaultConnection")!;

    return new PrinterRepository(connString);
});

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// app.UseHttpsRedirection(); // Keep commented out for development
app.UseStaticFiles();
app.UseRouting();

// ✅ ADD THIS LINE — must be between UseRouting and UseAuthorization
app.UseSession();

app.UseAuthorization();

// ✅ UPDATE DEFAULT ROUTE — Login page is now the default
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

// Test DB connection on startup
using (var scope = app.Services.CreateScope())
{
    var dbHelper = scope.ServiceProvider.GetRequiredService<DbHelper>();
    if (dbHelper.TestConnection(out string error))
        Console.WriteLine("✅ Database connection successful!");
    else
        Console.WriteLine($"❌ {error}");
}

app.Run();