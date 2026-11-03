using Lis.Core.Data;
using Lis.Core.Services;
using Lis.Web.Components;
using Lis.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register LIS Core Services
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAccessionService, AccessionService>();
builder.Services.AddScoped<IReferenceRangeService, ReferenceRangeService>();
builder.Services.AddScoped<ICalculatedComponentService, CalculatedComponentService>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IResultService, ResultService>();
builder.Services.AddScoped<ITestCatalogService, TestCatalogService>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<ILabSettingsService, LabSettingsService>();
builder.Services.AddScoped<IQcService, QcService>();
builder.Services.AddScoped<DirectPrintService>();
builder.Services.AddScoped<UserPreferencesService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
