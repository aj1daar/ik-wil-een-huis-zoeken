# Ik Wil Een Huis Zoeken (IWEHZ)

Real-time Dutch rental market scraper and Telegram notification bot. Monitors Pararius, Vesteda, and Huurwoningen and alerts registered users when new listings match their preferences.

---

## Architecture

```
IWEHZ/
├── Domain/Models/          # User, City, UserCity, RentalListing, NotificationLog
├── Infrastructure/
│   ├── Persistence/        # AppDbContext, EF Core migrations
│   └── Http/               # ScraperHttpClientFactory (anti-bot headers, proxy)
├── Scrapers/               # IPropertyScraper, Pararius, Vesteda, Huurwoningen
├── Services/               # UserService, CityService, NotificationDispatcher
├── Workers/                # ScraperWorker (BackgroundService), TelegramBotWorker
└── Bot/
    ├── Conversations/      # In-memory ConversationStateCache (step machine)
    └── Handlers/           # MessageHandler (all Telegram UX flows)
```

### Background workers

| Worker | Behaviour |
|---|---|
| `ScraperWorker` | Runs all three scrapers every 60–120 s (randomised). Persists new listings and fires `NotificationDispatcher`. |
| `TelegramBotWorker` | Long-polls the Telegram Bot API via `StartReceiving`. Routes messages to `MessageHandler`. |

### Bot commands

| Command | Access | Description |
|---|---|---|
| `/start` | Active users | Begin or resume onboarding wizard |
| `/settings` | Active users | Update budget or cities |
| `/mycities` | Active users | Show current preferences |
| `/activate <chat_id>` | Admin only | Grant a pending user access and start their onboarding |

---

## Required configuration

### Production (GitHub Actions secrets → environment variables)

Set these as repository secrets in **Settings → Secrets and variables → Actions**. They are injected at deploy time via systemd `EnvironmentFile` or `Environment=` directives.

| Secret name | Maps to config key | Description |
|---|---|---|
| `CONNECTIONSTRINGS__POSTGRES` | `ConnectionStrings:Postgres` | Full Npgsql connection string, e.g. `Host=localhost;Port=5432;Database=iwehz;Username=iwehz_user;Password=…` |
| `TELEGRAM__BOTTOKEN` | `Telegram:BotToken` | Token from [@BotFather](https://t.me/BotFather) |
| `TELEGRAM__ADMINCHATID` | `Telegram:AdminChatId` | Your personal Telegram chat ID (numeric). Obtain via [@userinfobot](https://t.me/userinfobot) |
| `SCRAPER__PROXYURL` | `Scraper:ProxyUrl` | Optional. HTTP/SOCKS5 proxy URL, e.g. `http://user:pass@proxy.host:3128`. Leave empty to disable. |

### Local development

Copy `appsettings.Development.json` (gitignored) and fill in the values:

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
    "ProxyUrl": ""
  }
}
```

---

## Database

EF Core migrations run automatically on startup. A fresh database is fully provisioned including the 25 seeded Dutch cities.

### Manual migration (optional)

```bash
cd IWEHZ
dotnet ef database update
```

### Adding cities

Insert directly into the `cities` table:

```sql
INSERT INTO cities (name_nl, name_en, is_active)
VALUES ('Wageningen', 'Wageningen', true);
```

---

## Running locally

```bash
cd IWEHZ
dotnet run
```

Health check: `GET http://localhost:5000/health`

---

## Deploying to Hetzner (systemd)

```bash
# 1. Publish self-contained binary
dotnet publish -c Release -r linux-x64 --self-contained true -o /opt/iwehz/publish

# 2. Create systemd unit at /etc/systemd/system/iwehz.service
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
Environment=CONNECTIONSTRINGS__POSTGRES=Host=localhost;Port=5432;Database=iwehz;Username=iwehz_user;Password=SECRET
Environment=TELEGRAM__BOTTOKEN=YOUR_BOT_TOKEN
Environment=TELEGRAM__ADMINCHATID=YOUR_CHAT_ID
Environment=SCRAPER__PROXYURL=

[Install]
WantedBy=multi-user.target

# 3. Enable and start
systemctl daemon-reload
systemctl enable iwehz
systemctl start iwehz
systemctl status iwehz
```

---

## Memory profile

Configured for Workstation GC (`ServerGarbageCollection=false`, `GCConserveMemory=7`) to minimise heap footprint on the shared Hetzner CX23 (4 GB RAM). EF Core uses `AsNoTracking` on all read queries. `HttpClient` instances for scraping are created per-scrape-cycle and disposed immediately.
