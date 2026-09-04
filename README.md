# Ik Wil Een Huis Zoeken (IWEHZ)

Real-time Dutch rental market scraper and Telegram notification bot. Monitors Pararius, Kamernet, 123wonen, DirectWonen, Nederwoon, and Vesteda and alerts users when new listings match their preferences.

---

## Architecture

```
IWEHZ/
├── Domain/Models/          # User, City, UserCity, RentalListing, NotificationLog
├── Infrastructure/
│   ├── Persistence/        # AppDbContext, EF Core migrations
│   └── Http/               # ScraperFetcher / ScraperHttpClientFactory (ScraperAPI proxy, anti-bot headers)
├── Scrapers/               # IPropertyScraper + one implementation per source
├── Services/               # UserService, CityService, NotificationDispatcher, AdminNotifier
├── Workers/                # ScraperWorker, TelegramBotWorker, CleanupWorker
└── Bot/
    ├── Conversations/      # In-memory ConversationStateCache (step machine)
    └── Handlers/           # MessageHandler (all Telegram UX flows)
```

### Background workers

| Worker | Behaviour |
|---|---|
| `ScraperWorker` | Runs each scraper on its own interval. Persists new listings, detects price drops, fires `NotificationDispatcher`. Retries on HTTP 5xx, network errors, and timeouts. |
| `TelegramBotWorker` | Long-polls the Telegram Bot API via `StartReceiving`. Routes messages and callback queries to `MessageHandler`. |
| `CleanupWorker` | Periodically deletes listings older than `Cleanup:RetentionDays` (default 30). |

### Scrapers

All scrapers route through ScraperAPI's proxy endpoint (`Scraper:ScraperApiKey`). Geo-target
is `eu` by default (`Scraper:ScraperApiCountry`) — the free/Hobby plan does not allow
country-level targeting like `nl`. When the key is unset (local dev) `ScraperFetcher` falls
back to a direct client.

| Source | URL strategy | Default interval | Credits/run |
|---|---|---|---|
| Kamernet | Single national page | 4 h | 1 |
| DirectWonen | Single national page | 6 h | 1 |
| Huurstunt | Single national page (JSON-LD) | 8 h | 1 |
| Nederwoon | One request per user-selected city | 18 h | ~1 per city |
| 123wonen | One request per user-selected city | 24 h | ~1 per city |
| vb&t | Paginated national list, capped at `Scraper:VbtMaxPages` (3) | 24 h | ~3 |
| Pararius | Single national page, `render=true` (Cloudflare) | 72 h | 10 |

Intervals live in `Scraper:SourceIntervalSeconds:{source}`. The defaults budget to
roughly 800 credits/month so the bot stays inside the 1,000-credit free plan.
City-loop scrapers (Nederwoon, 123wonen) only request cities at least one active user
has selected, so their cost scales with sign-ups.

**Credit safety net:** `ScraperWorker` checks ScraperAPI's `/account` endpoint (free,
cached 1 h) at the top of each cycle and pauses **all** scraping once usage reaches
`Scraper:ScraperApiCreditStopRatio` (0.95) of the plan limit, alerting once
(`scraperapi:budget`). An account-level ScraperAPI failure (401/403/429) likewise raises
one `scraperapi:account` alert and skips the rest of the run instead of one alert per source.

### Bot UX

Users interact via a persistent reply keyboard (bottom of chat) with a main menu and a "More" submenu. Onboarding is a guided wizard: min budget → max budget → property type → cities.

| Button / Command | Description |
|---|---|
| 📊 Status | Current settings + inline pause/resume toggle |
| ⚙️ Settings | Update budget, min budget, cities, or property type |
| ⏸ Pause / ▶️ Resume | Toggle notifications |
| 📍 My Cities | List saved cities |
| ➕ More | Secondary menu |
| 💶 Min Budget | Update minimum budget |
| 🏠 Property Type | Filter by apartment / house / room / any |
| 📋 Help | List all commands |
| `/stats` | Admin only — user counts and per-source listing stats |

### Notification logic

- New listings are matched against each user's max budget, min budget, property type filter, and city list.
- Cross-site deduplication via content fingerprint (city + price bucket + street) prevents the same property appearing twice within 48 h.
- Price drops on already-seen listings trigger a separate alert.
- Telegram sends are retried up to 3× with exponential back-off; rate-limit (429) respects `RetryAfter`.

---

## Required configuration

### Production (environment variables / systemd)

| Variable | Config key | Description |
|---|---|---|
| `CONNECTIONSTRINGS__POSTGRES` | `ConnectionStrings:Postgres` | Npgsql connection string |
| `TELEGRAM__BOTTOKEN` | `Telegram:BotToken` | Token from [@BotFather](https://t.me/BotFather) |
| `TELEGRAM__ADMINCHATID` | `Telegram:AdminChatId` | Your numeric Telegram chat ID — get it from [@userinfobot](https://t.me/userinfobot) |
| `SCRAPER__SCRAPERAPIKEY` | `Scraper:ScraperApiKey` | ScraperAPI key. Unset = direct fetch (no proxy). |

### Local development

Create `appsettings.Development.json` (gitignored):

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=iwehz_dev;Username=postgres;Password=YOUR_PW"
  },
  "Telegram": {
    "BotToken": "YOUR_BOT_TOKEN",
    "AdminChatId": 0
  },
  "Scraper": {
    "IntervalMinSeconds": 60,
    "IntervalMaxSeconds": 120,
    "ScraperApiKey": ""
  }
}
```

---

## Database

EF Core migrations run automatically on startup. A fresh database is fully provisioned including 25 seeded Dutch cities.

### Adding cities

Insert directly into the `cities` table and add the new `City` seed entry in `AppDbContext.SeedCities`:

```sql
INSERT INTO cities (name_nl, name_en, is_active) VALUES ('Wageningen', 'Wageningen', true);
```

---

## Running locally

```bash
dotnet run
```

Health check: `GET http://localhost:5000/health`

---

## Deploying to Hetzner (systemd)

```bash
# 1. Publish self-contained binary
dotnet publish -c Release -r linux-x64 --self-contained true -o /opt/iwehz/publish

# 2. /etc/systemd/system/iwehz.service
[Unit]
Description=IWEHZ Rental Bot
After=network.target postgresql.service

[Service]
WorkingDirectory=/opt/iwehz/publish
ExecStart=/opt/iwehz/publish/IWEHZ
Restart=always
RestartSec=10
User=iwehz
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=CONNECTIONSTRINGS__POSTGRES=Host=localhost;...
Environment=TELEGRAM__BOTTOKEN=...
Environment=TELEGRAM__ADMINCHATID=...
Environment=SCRAPER__PROXYURL=

[Install]
WantedBy=multi-user.target

# 3. Enable and start
systemctl daemon-reload
systemctl enable iwehz
systemctl start iwehz
```

---

## Memory profile

Configured for Workstation GC (`ServerGarbageCollection=false`, `GCConserveMemory=7`) to minimise heap footprint on the shared Hetzner CX23 (4 GB RAM). EF Core uses `QueryTrackingBehavior.NoTracking` globally; all writes use `ExecuteUpdateAsync` / `ExecuteDeleteAsync`. `HttpClient` instances are created per scrape cycle and disposed immediately.
