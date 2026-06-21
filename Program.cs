using IWEHZ.Bot.Conversations;
using IWEHZ.Bot.Handlers;
using IWEHZ.Infrastructure.Persistence;
using IWEHZ.Scrapers;
using IWEHZ.Services;
using IWEHZ.Workers;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

var botToken = builder.Configuration["Telegram:BotToken"]
    ?? throw new InvalidOperationException("Telegram:BotToken is required");

builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));

builder.Services.AddSingleton<ConversationStateCache>();
builder.Services.AddSingleton<IPropertyScraper, ParariusScraper>();
builder.Services.AddSingleton<IPropertyScraper, VestedaScraper>();
builder.Services.AddSingleton<IPropertyScraper, HuurwoningenScraper>();
builder.Services.AddSingleton<IPropertyScraper, KamernetScraper>();
builder.Services.AddSingleton<IPropertyScraper, DirectWonenScraper>();
builder.Services.AddSingleton<IPropertyScraper, NederwoonScraper>();

builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<CityService>();
builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddSingleton<AdminNotifier>();
builder.Services.AddSingleton<MessageHandler>();

builder.Services.AddHostedService<ScraperWorker>();
builder.Services.AddHostedService<TelegramBotWorker>();

var app = builder.Build();

var dbFactory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
await using (var db = await dbFactory.CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy", utc = DateTime.UtcNow }));

await app.RunAsync();
