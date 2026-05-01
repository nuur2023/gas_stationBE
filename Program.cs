// using backend.Data.Context;
// using backend.Data.Interfaces;
// using backend.Data.Repository;
// using backend.Data.Seeds;
// using System.Text;
// using System.Text.Json;
// using System.Text.Json.Serialization;
// using Microsoft.AspNetCore.Authentication.JwtBearer;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.IdentityModel.Tokens;
// using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

// var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddControllers().AddJsonOptions(o =>
// {
//     o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
//     o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
// });
// builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();

// // var connectionString = builder.Configuration.GetConnectionString("ConStr")
// //     ?? builder.Configuration.GetConnectionString("Default")
// //     ?? throw new InvalidOperationException("Connection string 'ConStr' (or 'Default') is not configured.");

// // var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
// // builder.Services.AddDbContext<GasStationDBContext>(opt =>
// //     opt.UseMySql(connectionString, serverVersion));
// // Conect to Database
// builder.Services.AddDbContext<GasStationDBContext>(options => options.UseMySql(
//     builder.Configuration.GetConnectionString("ConStr"), new MySqlServerVersion(new Version(10, 4, 34)))
// );

// var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is missing.");
// var jwtIssuer = builder.Configuration["Jwt:Issuer"];
// var jwtAudience = builder.Configuration["Jwt:Audience"];

// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddJwtBearer(options =>
//     {
//         options.TokenValidationParameters = new TokenValidationParameters
//         {
//             ValidateIssuer = true,
//             ValidateAudience = true,
//             ValidateIssuerSigningKey = true,
//             ValidateLifetime = true,
//             ValidIssuer = jwtIssuer,
//             ValidAudience = jwtAudience,
//             IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
//             ClockSkew = TimeSpan.Zero
//         };
//     });

// builder.Services.AddAuthorization();

// builder.Services.AddCors(options =>
// {
//     options.AddPolicy("Frontend", policy =>
//     {
//         policy.WithOrigins("http://localhost:5174", "http://127.0.0.1:5174")
//             .AllowAnyHeader()
//             .AllowAnyMethod();
//     });

//     if (builder.Environment.IsDevelopment())
//     {
//         options.AddPolicy("MobileDev", policy =>
//         {
//             policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
//         });
//     }
// });

// builder.Services.AddScoped<IBusinessRepository, BusinessRepository>();
// builder.Services.AddScoped<IRoleRepository, RoleRepository>();
// builder.Services.AddScoped<IMenuRepository, MenuRepository>();
// builder.Services.AddScoped<ISubMenuRepository, SubMenuRepository>();
// builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
// builder.Services.AddScoped<IFuelTypeRepository, FuelTypeRepository>();
// builder.Services.AddScoped<ICurrencyRepository, CurrencyRepository>();
// builder.Services.AddScoped<IFuelPriceRepository, FuelPriceRepository>();
// builder.Services.AddScoped<IUserRepository, UserRepository>();
// builder.Services.AddScoped<IBusinessUserRepository, BusinessUserRepository>();
// builder.Services.AddScoped<IRateRepository, RateRepository>();
// builder.Services.AddScoped<IPumpRepository, PumpRepository>();
// builder.Services.AddScoped<INozzleRepository, NozzleRepository>();
// builder.Services.AddScoped<IDippingPumpRepository, DippingPumpRepository>();
// builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
// builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
// builder.Services.AddScoped<IGeneratorUsageRepository, GeneratorUsageRepository>();
// builder.Services.AddScoped<IStationRepository, StationRepository>();
// builder.Services.AddScoped<IDippingRepository, DippingRepository>();
// builder.Services.AddScoped<ILiterReceivedRepository, LiterReceivedRepository>();
// builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
// builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
// builder.Services.AddScoped<ICustomerFuelGivenRepository, CustomerFuelGivenRepository>();
// builder.Services.AddScoped<IAccountRepository, AccountRepository>();
// builder.Services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
// builder.Services.AddScoped<ICustomerPaymentRepository, CustomerPaymentRepository>();
// builder.Services.AddScoped<IAuthRepository, AuthRepository>();

// var app = builder.Build();

// Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads", "inventory-evidence"));

// if (app.Environment.IsDevelopment())
// {
//     app.UseSwagger();
//     app.UseSwaggerUI();
// }

// if (!app.Environment.IsDevelopment())
// {
//     app.UseHttpsRedirection();
// }
// if (app.Environment.IsDevelopment())
// {
//     app.UseCors("MobileDev");
// }
// else
// {
//     app.UseCors("Frontend");
// }
// app.UseAuthentication();
// app.UseAuthorization();
// app.MapControllers();

// using (var scope = app.Services.CreateScope())
// {
//     var context = scope.ServiceProvider.GetRequiredService<GasStationDBContext>();
//     await SeedData.InitializeAsync(context);
// }

// app.Run();


using gas_station.Data.Context;
using gas_station.Data.Interfaces;
using gas_station.Data.Repository;
using gas_station.Data.Seeds;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// -------------------- JSON --------------------
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// -------------------- DATABASE --------------------
builder.Services.AddDbContext<GasStationDBContext>(options => options.UseMySql(
    builder.Configuration.GetConnectionString("ConStr"), new MySqlServerVersion(new Version(8, 0, 36)))
);

// -------------------- JWT --------------------
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT Key missing.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// -------------------- CORS --------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dev", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });

    options.AddPolicy("Prod", policy =>
    {
        // 🔥 CHANGE THIS to your real frontend domain later
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// -------------------- REPOSITORIES --------------------
builder.Services.AddScoped<IBusinessRepository, BusinessRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IMenuRepository, MenuRepository>();
builder.Services.AddScoped<ISubMenuRepository, SubMenuRepository>();
builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
builder.Services.AddScoped<IFuelTypeRepository, FuelTypeRepository>();
builder.Services.AddScoped<ICurrencyRepository, CurrencyRepository>();
builder.Services.AddScoped<IFuelPriceRepository, FuelPriceRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IBusinessUserRepository, BusinessUserRepository>();
builder.Services.AddScoped<IRateRepository, RateRepository>();
builder.Services.AddScoped<IPumpRepository, PumpRepository>();
builder.Services.AddScoped<INozzleRepository, NozzleRepository>();
builder.Services.AddScoped<IDippingPumpRepository, DippingPumpRepository>();
builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IGeneratorUsageRepository, GeneratorUsageRepository>();
builder.Services.AddScoped<IStationRepository, StationRepository>();
builder.Services.AddScoped<IDippingRepository, DippingRepository>();
builder.Services.AddScoped<ILiterReceivedRepository, LiterReceivedRepository>();
builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
builder.Services.AddScoped<ICustomerFuelGivenRepository, CustomerFuelGivenRepository>();
builder.Services.AddScoped<IAccountRepository, AccountRepository>();
builder.Services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
builder.Services.AddScoped<ICustomerPaymentRepository, CustomerPaymentRepository>();
builder.Services.AddScoped<IAuthRepository, AuthRepository>();
builder.Services.AddScoped<IBusinessFuelInventoryLedgerRepository, BusinessFuelInventoryLedgerRepository>();

// -------------------- FORWARDED HEADERS --------------------
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

// -------------------- MIDDLEWARE --------------------
app.UseForwardedHeaders();

// Create upload directory
Directory.CreateDirectory(Path.Combine(
    app.Environment.ContentRootPath,
    "wwwroot", "uploads", "inventory-evidence"
));

// Swagger (Dev only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ❌ DO NOT USE HTTPS REDIRECTION (handled by platform)

// CORS
if (app.Environment.IsDevelopment())
{
    app.UseCors("Dev");
}
else
{
    app.UseCors("Prod");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// -------------------- MIGRATE DATABASE --------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GasStationDBContext>();
    db.Database.Migrate();
}

// -------------------- SEED DATA --------------------
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<GasStationDBContext>();
    await SeedData.InitializeAsync(context);
}

// -------------------- PORT BINDING (CRITICAL) --------------------
//------------------Shortcut for port binding------------------
// var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
// app.Run($"http://0.0.0.0:{port}");

//------------------Dynamic port binding (App Platform / containers)------------------
var portValue = Environment.GetEnvironmentVariable("PORT");
var port = int.TryParse(portValue, out var parsedPort) ? parsedPort : 8080;
app.Run($"http://0.0.0.0:{port}");