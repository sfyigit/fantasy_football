# Fantasy Football Match Day Engine

.NET 9 · PostgreSQL 16 · Redis 7 · RabbitMQ 3 · Docker Compose

---

## Çalıştırma

```bash
docker compose up --build -d
docker compose ps
docker compose logs -f match-data-ingestion   # 60 sn içinde fixture akışı başlamalı
```

| Servis | URL |
|---|---|
| MatchApi | http://localhost:8080 |
| Swagger UI | http://localhost:8080/swagger |
| RabbitMQ Management | http://localhost:15672 (`footballgame` / `footballgame567`) |
| MatchDataIngestion health | http://localhost:8082/health |
| ScoringWorker health | http://localhost:8083/health |
| LeaderboardWorker health | http://localhost:8081/health |

---

## Mimari

```
mock-data/*.json
       │ 60 sn PeriodicTimer
       ▼
MatchDataIngestion ──► PostgreSQL
       │ PlayerStatUpdatedEvent
       ▼
   RabbitMQ (fantasy.events, topic exchange)
       ├──► ScoringWorker  ──► PostgreSQL + ScoreCalculatedEvent
       └──► LeaderboardWorker ──► Redis Sorted Sets
                                       ▲
                               MatchApi (WebSocket + HTTP)
                                 Client ◄──► MatchApi
```

Pipeline tek yönlü. MatchApi, veri akışına yazmaz; okuma ve push tarafında durur.

### Servisler

| Servis | Tip | Sorumluluk |
|---|---|---|
| **MatchDataIngestion** | `IHostedService` + `PeriodicTimer` | Mock JSON okur, normalize eder, DB'ye yazar, event yayınlar |
| **MatchApi** | ASP.NET Core 9 minimal API | WebSocket + HTTP opcode dispatcher, JWT auth, rate limiting |
| **ScoringWorker** | RabbitMQ consumer | Fantasy puan kurallarını uygular, `ScoreCalculatedEvent` yayınlar |
| **LeaderboardWorker** | RabbitMQ consumer | Redis Sorted Set günceller (`leaderboard:global`, `leaderboard:league:{id}`) |
| **Shared** | Class library | Domain entity'leri, EF Core DbContext + migration'lar, event ve opcode contract'ları |

---

## Opcode Protokolü

```json
// Request
{ "opcode": 1001, "requestId": "uuid-v4", "payload": {} }

// Response
{ "opcode": 1001, "requestId": "uuid-v4", "status": "success", "data": {}, "error": null }
```

| Opcode | İsim | Yön | Auth |
|---|---|---|---|
| 1001 | GET_MATCHES | C→S | ❌ |
| 1002 | SUBSCRIBE_LIVE_SCORE | C→S | ❌ |
| 1003 | UNSUBSCRIBE_LIVE_SCORE | C→S | ❌ |
| 1004 | LIVE_SCORE_UPDATE | S→C | ❌ |
| 2001 | SAVE_SQUAD | C→S | ✅ |
| 2002 | GET_MY_SCORE | C→S | ✅ |
| 2003 | GET_PLAYER_STATS | C→S | ❌ |
| 3001 | GET_LEADERBOARD | C→S | ❌ |
| 3002 | GET_MY_RANK | C→S | ✅ |
| 9000 | HEARTBEAT | C↔S | ❌ |
| 9999 | ERROR | S→C | ❌ |

Her opcode için ayrı `IOpcodeHandler` implementasyonu vardır. `OpcodeDispatcher` DI'dan aldığı handler'ları `Dictionary<int, IOpcodeHandler>` ile yönlendirir. Auth enforcement merkezidir: `IAuthenticatedHandler` marker'ı taşıyan handler'lara token eksikse dispatcher 9999 döner.

---

## Mimari Kararlar

### Shared Kernel
Tek `Shared` class library — entity, DbContext, migration, event ve opcode contract'ları burada. Gerçek mikroserviste her servis kendi şemasına sahip olur; case study ölçeğinde compile-time coupling, dağıtık şema yönetiminin operasyonel maliyetinden daha az zararlı.

### Tek PostgreSQL
Database-per-service, Squad + Score + User'ı kapsayan sorgular için distributed transaction / saga gerektirir. Bu ölçekte fazla maliyet.

### Redis Sorted Set (Leaderboard)
`ZADD` / `ZRANK` / `ZRANGE` — O(log N) insert + rank sorgusu. PostgreSQL `ORDER BY` her skor güncellemesinde index invalidation riski taşır. **Trade-off:** Redis flush → leaderboard kaybolur; mitigation: AOF persistence veya startup'ta `PlayerGameweekScore` replay.

### RabbitMQ Topic Exchange
`fantasy.events` topic exchange; routing key = event tipi. Yeni consumer eklemek için publisher'a dokunmak gerekmez. Kafka'nın operasyonel maliyeti case study için orantısız. **Trade-off:** Geçmiş event replay için broker değil PostgreSQL kullanılır.

### WebSocket + HTTP Hibrit
Opcode 1004 (server push) ve 9000 (bidirectional) WebSocket gerektiriyor. Aynı zarfı HTTP `POST /opcode` üzerinde de açmak, Swagger'dan token gerektirmeden test imkânı sağlar. **Trade-off:** Stateful WS bağlantıları load-balance'da sticky session veya Redis pub/sub gerektirir.

### EF Core `MigrateAsync()` Startup'ta
`docker compose up` için en sade yaklaşım. **Trade-off:** Çoklu instance eş zamanlı başlarsa EF lock'u yavaşlatır; üretimde init container olarak migration job tercih edilmeli.

---

## Idempotency

`Fixture.Id` tüm pipeline boyunca tek idempotency key olarak taşınır:

| Aşama | Mekanizma |
|---|---|
| MatchDataIngestion | `Fixtures` tablosunda unique constraint — aynı `Id` tekrar insert edilmez |
| ScoringWorker | `PlayerGameweekScore.IdempotencyKey` üzerinde indexed DB read — duplicate ise skip |
| LeaderboardWorker | Redis `SET NX` (`TryMarkProcessedAsync`) — atomic, O(1) |

RabbitMQ at-least-once delivery nedeniyle aynı event iki kez gelebilir (retry, consumer restart). Bu kontroller olmadan bir oyuncu aynı gol için iki kez puan alırdı. **Trade-off:** LeaderboardWorker'ın Redis processed-keys set'i leaderboard ile aynı instance'ta; Redis flush olursa her ikisi birden kaybolur → replay double-bump üretir.

---

## Performans

- **Leaderboard okuma:** Redis Sorted Set `ZRANK` / `ZRANGE` — O(log N), DB'ye hit yok.
- **Puan hesaplama:** ScoringWorker, API path'inden tamamen izole; queue burst'leri absorbe eder.
- **Rate limiting (HTTP):** Fixed window, 60 req/dk per IP (`Microsoft.AspNetCore.RateLimiting`) — sıfır in-memory overhead, O(1) karar.
- **Rate limiting (WS):** Per-session fixed window, 120 mesaj/dk. Heartbeat (9000) kota sayımına dahil değil. Limit aşılırsa 9999 `RATE_LIMIT_EXCEEDED` döner, bağlantı açık kalır.
- **Idempotency check:** Her aşamada indexed lookup — hot path'te ek round-trip yok.
- **Migration:** Startup'ta tek seferlik; Worker başlamadan önce tamamlanır.
- **Outbox relay:** 5 sn'de bir polling, batch 50 — DB write ile broker publish aynı transaction'da, crash durumunda event kaybı yok.

---

## Güvenlik

### Alınan Önlemler

- **JWT authentication** — `/auth/register` + `/auth/login` REST endpoint'leri; 3 opcode (2001, 2002, 3002) `Authorization: Bearer` header'ı olmadan 9999 döner
- **BCrypt password hashing** — `BCrypt.Net-Next`, varsayılan cost factor
- **Rate limiting** — tüm public endpoint'lerde 60 req/dk per IP, 429 reject
- **JWT stateless validation** — her MatchApi instance token'ı shared store olmadan doğrular; horizontal scaling uyumlu
- **Centralized auth enforcement** — `IAuthenticatedHandler` marker pattern; auth logic her handler'da ayrı ayrı yazılmadı
- **Environment-based secrets** — `JWT_SECRET`, DB ve RabbitMQ credential'ları `docker-compose.yml` environment variable'ları olarak geçiyor, kod içinde hardcode yok

### Bilinen Eksiklikler — Üretimde Giderilmeli

Bu eksiklikler case study kapsamında bilinçli olarak ertelendi. Gerçek bir deployment'tan önce aşağıdakilerin mutlaka ele alınması gerekir:

- **Secrets yönetimi** — `JWT_SECRET`, PostgreSQL ve RabbitMQ credential'ları şu an `docker-compose.yml` içinde; üretimde secret manager veya `.env` dosyasına taşınmalı, default değerler startup'ta reddedilmeli
- **Login brute-force koruması** — `/auth/login` generic IP rate limit paylaşıyor; per-account lockout, şifre karmaşıklık kuralı ve e-posta format validasyonu yok
- **WebSocket token re-validation** — JWT yalnızca handshake anında doğrulanıyor; socket açık kaldığı sürece süresi dolmuş token erişmeye devam eder. Periyodik re-validation ve `jti` blacklist mekanizması gerekli
- **Veritabanı constraint eksiklikleri** — `Users.Email` ve `Users.Username` sütunlarında unique index yok (büyük/küçük harf varyantlarıyla duplicate kayıt ve check-then-insert race condition mümkün); `SquadPlayers.PlayerId`'de `Players` tablosuna foreign key yok, `SaveSquadHandler` keyfi player ID ve sınırsız kadro boyutu kabul ediyor
- **Input validation** — handler'larda pagination cap, kadro kompozisyon kuralı, REST ve WebSocket frame boyut limiti yok
- **Transport security** — HTTPS enforcement, HSTS ve secure-headers middleware yok; JWT'ler şifreli olmayan bağlantıda clear text taşınıyor
- **Hata mesajları** — response'larda iç tanımlayıcılar (user ID, player ID) sızıyor; enumeration saldırılarına zemin hazırlıyor

---

## Gelecek Geliştirmeler

| Konu | Açıklama |
|---|---|
| Per-gameweek leaderboard | `leaderboard:gameweek:{gw}` key'i yok; sadece toplam puan bazlı sıralama var |
| LeaderboardRebuild job | Redis flush sonrası `PlayerGameweekScore` replay edilmiyor |
| AsyncAPI dökümanı | WebSocket opcode protokolü OpenAPI ile tarif edilemiyor; AsyncAPI gerekiyor |
