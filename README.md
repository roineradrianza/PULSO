# PULSO — Real-Time Emergency Situation Reporting

> A citizen-driven emergency reporting platform built for Venezuela. Reports arrive via Telegram and WhatsApp, are triaged by AI, geo-referenced, and displayed on a live map.

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![Deploy to Production](https://github.com/roineradrianza/PULSO/actions/workflows/deploy-production.yml/badge.svg)](https://github.com/roineradrianza/PULSO/actions/workflows/deploy-production.yml)

🌐 **Live:** [pulsoaid.org](https://pulsoaid.org)

---

## What is PULSO?

PULSO is an open-source emergency coordination platform that aggregates real-time citizen reports during crises (earthquakes, floods, blackouts, medical emergencies). It processes incoming messages from Telegram and WhatsApp, uses Google Gemini to classify and extract structured data (location, severity, type), and displays everything on a geo-referenced interactive map — accessible offline as a Progressive Web App.

---

## Architecture

```
[Telegram / WhatsApp]
        │
        ▼
[IngressApi  ──────────────────────────────────────────────────]
│  • Validates & queues incoming reports                       │
│  • Serves situations, sectors, SSE stream, comments API      │
│  • Rate-limited per IP                                       │
└──────────────────────┬────────────────────────────────────────┘
                       │ Redis Queue
                       ▼
[AiWorker ─────────────────────────────────────────────────────]
│  • Dequeues reports                                          │
│  • Classifies with Google Gemini (severity, type, summary)   │
│  • Geo-codes with LLM + Nominatim fallback (PostGIS)         │
│  • Persists to PostgreSQL                                    │
└───────────────────────────────────────────────────────────────┘
                       │
                       ▼
          [PostgreSQL / Supabase (PostGIS)]
                       │
        ┌──────────────┘
        ▼
[PWA (Svelte + Leaflet) ◄── SSE /stream ── IngressApi]
  • Offline-first (Service Worker)
  • Cluster map with real-time updates
  • Anonymous citizen comments per incident
```

---

## Tech Stack

| Layer | Technology |
|---|---|
| **API & Worker** | .NET 10, ASP.NET Core Minimal APIs (Slim Builder) |
| **Database** | PostgreSQL via Supabase (PostGIS extension) |
| **Queue** | Redis (StackExchange.Redis) |
| **AI Triage** | Google Gemini (structured output) |
| **Geocoding** | LLM primary + Nominatim/OSM fallback |
| **Real-time** | Server-Sent Events (SSE) |
| **Frontend** | Svelte 4, Vite 5, Leaflet, Dexie (IndexedDB) |
| **PWA** | vite-plugin-pwa (Workbox, offline-first) |
| **Reverse Proxy** | Caddy (auto-HTTPS, SSE bypass, cache headers) |
| **Observability** | OpenTelemetry → OTLP (traces, metrics, logs) |
| **Messaging** | Telegram Bot API, WhatsApp (webhook) |
| **CI/CD** | GitHub Actions (staging + production) |

---

## Running Locally

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org) + [pnpm](https://pnpm.io)
- A [Supabase](https://supabase.com) project (PostgreSQL + PostGIS)
- A Redis instance

### 1. Configure the local secrets

Local configurations are managed using .NET User Secrets (recommended) or environment variables. Do not use the `pulso-*.service.template` files for local development; they are systemd unit files intended for VPS staging/production deployments.

Initialize and set the required keys for both projects:

#### For IngressApi:
```bash
cd src/Pulso.IngressApi
dotnet user-secrets init

# Set required database, Redis, and Telegram secret webhook tokens:
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=TU_DB_HOST;Port=6543;Database=postgres;Username=postgres.TU_PROJECT_ID;Password=TU_DB_PASSWORD;SslMode=Require;Trust Server Certificate=true;No Reset On Close=true;"
dotnet user-secrets set "ConnectionStrings:UpstashRedis" "localhost:6379,password=TU_REDIS_PASSWORD,abortConnect=false"
dotnet user-secrets set "Telegram:SecretToken" "TU_TELEGRAM_SECRET_TOKEN"
```

#### For AiWorker:
```bash
cd src/Pulso.AiWorker
dotnet user-secrets init

# Set required database, Redis, Gemini, and Telegram bot tokens:
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=TU_DB_HOST;Port=6543;Database=postgres;Username=postgres.TU_PROJECT_ID;Password=TU_DB_PASSWORD;SslMode=Require;Trust Server Certificate=true;No Reset On Close=true;"
dotnet user-secrets set "ConnectionStrings:UpstashRedis" "localhost:6379,password=TU_REDIS_PASSWORD,abortConnect=false"
dotnet user-secrets set "Telegram:BotToken" "TU_TELEGRAM_BOT_TOKEN"
dotnet user-secrets set "GeminiApiKey" "TU_GEMINI_API_KEY"
```

### 2. Run the database migrations

```bash
# Using the Supabase CLI
supabase db push
```

Or apply the SQL files in `supabase/migrations/` manually in order.

### 3. Start the backend

```bash
# IngressApi (runs on port 5152 by default)
dotnet run --project src/Pulso.IngressApi

# AiWorker (background queue processor)
dotnet run --project src/Pulso.AiWorker
```

### 4. Start the frontend

The PWA frontend development server proxies `/api` requests to `http://localhost:5152` (configured in `vite.config.js`).

```bash
cd src/Pulso.Pwa
pnpm install
pnpm dev
```

---

## Deployment

The project deploys automatically via GitHub Actions on push to `stage` (staging) and `main` (production). See the workflow files in [`.github/workflows/`](.github/workflows/) for details.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to submit issues, propose features, and open pull requests.

---

## Security

See [SECURITY.md](SECURITY.md) for our vulnerability disclosure policy.

---

## License

Copyright 2026 Roiner Adrianza

Licensed under the **Apache License, Version 2.0**. See [LICENSE](LICENSE) for the full text.
