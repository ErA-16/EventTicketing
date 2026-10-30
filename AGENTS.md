           # EventTicketing — Agent Journal (AGENTS.md)

Living documentation of what was built, why, and the concepts ErA asked about.
Updated after every slice. Study the "Questions asked" section for weak spots.

---

## Working rules (ErA's standing instructions)

- AI writes the code; ErA reviews, tests and asks questions. One step at a time, changes reported per file.
- Ask before creating any new file.
- Ask before adding any NuGet package; get ErA's permission first.
- Class layout everywhere: private fields at the top, constructor, all private methods, then protected/public members. Private methods must appear before any protected or public method, including overrides.
- Always end a slice with a manual test list ErA runs himself. No automated test code yet.
- Short answers. Explain the *why* of every decision in plain words.
- Secrets never in source. The design PDF (`Brief and Guidiance/`) is the source of truth.
- Password policy: 8+ chars, uppercase, lowercase, digit, special symbol.

## Stack and structure

ASP.NET Core 10 Web API, EF Core + Npgsql (Neon PostgreSQL), ASP.NET Core Identity (Guid keys, JWT),
Asp.Versioning.Mvc (URL segment: `/api/v1/...`), built-in rate limiter, clean architecture:
Domain → Application → Infrastructure → Api. Deployment later: Render (API), Cloudflare R2 (images), cloud pages (frontend).

## Configuration map

| What | Where (local) | Where (Render) |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | user secrets, Api project `UserSecretsId 5068da3c…` | env var `ConnectionStrings__DefaultConnection` |
| `Jwt:Key` | user secrets (48 random chars) | env var `Jwt__Key` |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes` | `appsettings.json` | same |
| `RefreshToken:IdleMinutes`, `AbsoluteMinutes`, `RotationGraceSeconds` | `appsettings.json` (480 / 10080 / 10) | same |
| `DataRetention:*` | `appsettings.json` (Slice 18) | same |
| `Seed:AdminEmail`, `Seed:AdminPassword` | user secrets | env vars `Seed__AdminEmail`, `Seed__AdminPassword` |
| `Seed:DemoOrganizerEmail`, `Seed:DemoOrganizerPassword` | user secrets | env vars `Seed__DemoOrganizerEmail`, `Seed__DemoOrganizerPassword` |

`Jwt:ExpiryMinutes` is **15** (Slice 18). Validation keeps the existing 1-minute clock skew, so a token
may be accepted for about 16 minutes. Refresh tokens **rotate on every refresh** (Slice 18): each refresh
issues a new token in the same family, the old one is revoked and linked through `ReplacedByTokenHash`;
replaying a rotated token after the 10-second grace window revokes the whole family. The 8-hour idle
window ($480$ minutes) and the 7-day absolute cap (`AbsoluteMinutes` 10080) both apply; tokens are stored
hashed only, and logout or suspension revokes the whole family.

`DemoDataSeeder` is opt-in: without the demo organizer email and password it skips. With them, a fresh
database gets one organizer and exactly the five portfolio events. Existing local events are not changed
or archived; the demo events are added alongside them.

User secrets only load in Development. `dotnet ef` needs `ASPNETCORE_ENVIRONMENT=Development`
or it cannot find the connection string.

---

## Slice journal

### Slice 0 — skeleton (done before this journal)
Four projects, references pointing inward, `Role` enum, `Wallet` entity, `EventTicketingUser`,
`AppDbContext` (IdentityDbContext with Guids), `IdentityService.RegisterAsync`.

### Slice 1 — auth, roles, rate limiting (DONE)

**Database provider swap.** SQL Server → Npgsql 10.0.3. Old SQL Server migration deleted,
`InitialCreate` created and applied to Neon. Note: the DB currently holds two migrations —
ErA's `Initial Migration` (with a space) plus an empty `InitialCreate`. Harmless; tidy later.

**Secrets.** Visual Studio created `secrets.json` but never wrote `<UserSecretsId>` into the Api
csproj, so the app could not find it. Fixed by adding the ID manually. Infrastructure briefly had
its own empty ID — removed (only the entry project's ID is auto-loaded).

**Wiring (`Program.cs`).**
- Fail-fast config checks (JWT key, issuer/audience, connection string) — no mystery 500s.
- `AddIdentity<EventTicketingUser, IdentityRole<Guid>>` + EF stores; unique email; password policy; lockout 5 attempts / 5 min.
- JWT bearer after `AddIdentity` (overrides the cookie default); `MapInboundClaims = true` so `"role"` maps to `ClaimTypes.Role` and `[Authorize(Roles=…)]` matches.
- `UseForwardedHeaders` first in the pipeline (Render proxy, otherwise all clients share the proxy IP and the rate limits break).
- Global exception handler: `{ code, message, details }` shape; full exception only logged; stack traces never leak.
- `InvalidModelStateResponseFactory`: automatic model validation answers in the same shape, not ProblemDetails.

**Auth core (`IdentityService`).**
- `AuthResponse` (was `AuthResult`): `Succeeded`, `Token`, `Errors`. Identity's own error descriptions
  flow into `Errors` — e.g. "Passwords must have at least one uppercase ('A'-'Z')."
- Register: server maps the `IsOrganizer` boolean to Attendee/Organizer. Client can never send a role.
  If role assignment fails, the user row is deleted (no half-registered accounts).
- Duplicate email is checked with `FindByEmailAsync` first, returning "An account with this email
  already exists." Identity's own check would have reported the *username* (we set it to the email)
  and leaked an implementation detail.
- Login: `SignInManager.CheckPasswordSignInAsync(lockoutOnFailure: true)` — built-in failed-attempt
  counting and lockout. Generic "Invalid email or password." for both unknown email and wrong password
  (stops account probing). Suspension checked last.
- Token claims: `sub` (user id), `email`, `jti` (token id, for future revocation), `name`, one `role` per role.
  HMAC-SHA256, expiry from `Jwt:ExpiryMinutes`.
- Class layout: fields → constructor → private `GenerateTokenAsync` → public methods.

**Seeding (`IdentitySeeder`).** Runs before `app.Run()`. Seeds the three role *rows*
(Admin, Attendee, Organizer — enum supplies names, rows live in Identity tables) and exactly one
*user*: the admin from `Seed:AdminEmail`/`Seed:AdminPassword`. Identity hashes the password at
creation; the DB never stores plaintext. Create-once: the password is never overwritten on later
boots. Missing config → warning + skip, never a default password.

**Endpoints (`AuthController`, `Asp.Versioning.Mvc`).**
- `POST /api/v1/auth/register` → 201, or 400 `{ code: "validation_failed" | "registration_failed", message, details }`.
- `POST /api/v1/auth/login` → 200 `{ token }`, or 401 `{ code: "login_failed", … }`.
- URL-segment versioning (`/api/v{version}/auth`); responses carry `api-supported-versions: 1.0`.

**Rate limiting (built-in).** Fixed window partitioned by client IP:
login 5/min, register 3/min. 429 in the same error shape. Verified: 4th register attempt in a minute → 429.

**Verified end to end (against Neon):** weak password → 400 with Identity messages; valid register → 201;
duplicate email → 400; wrong password → 401; admin login → 200 with JWT; rate limit → 429.

---

## Questions asked → weak points to revisit

| Topic | What to review |
|---|---|
| User secrets | How `UserSecretsId` links a project to its secrets folder; only the entry project loads; Development only; env vars win in production |
| Connection strings | Npgsql `Host=…;SSL Mode=…` format vs `postgresql://` URI; pooled (`-pooler`) vs direct Neon endpoint |
| `AuthResponse` vs `bool` | A result object carries token + reasons; `bool` forces exceptions for ordinary outcomes |
| UserManager vs SignInManager | Store operations vs sign-in flow (lockout counting); why login uses SignInManager |
| Role mapping | Server maps boolean → role; client can never self-promote; role *rows* are lookup data, not the same as seeded *users* |
| Lockout vs suspension | Lockout = temporary, automatic, after 5 bad passwords; suspension = admin ban flag |
| JWT claims | `sub` = subject (user id), `jti` = JWT id, `role` claim ↔ `[Authorize(Roles=…)]` via inbound claim mapping |
| Password hashing | Cannot pre-hash for `CreateAsync`; Identity hashes with a random salt at creation; plaintext never stored |
| "Hanging" on run | Web server never exits (Ctrl+C); startup silence = seeder waiting on cold Neon, not a freeze |
| Rate limit duration | Fixed window = 1 minute per partition (login 5/min, register 3/min); window resets on the clock, not per successful request; lockout (5 wrong passwords) is a separate 5-minute timer |
| Extra JSON fields | Unknown members are ignored by default (e.g. `isOrganizer` on login); the binded DTO decides what matters, nothing is "accepted" |
| Organizer vs event suspension | Organizer suspension is a user flag and hides every event derived from it; event suspension changes one event to `Suspended`. Unsuspend returns the event to `Approved`, so the organizer must publish again |
| 5-minute JWT | `Jwt:ExpiryMinutes = 5`, 1-minute clock skew means roughly 6 minutes of acceptance; the 30-minute sliding refresh token renews it without rotation |
| Refresh vs session | Refresh token is an idle timeout, not an absolute session; every successful refresh resets it to 30 minutes. The API cannot see scrolling, so the frontend must refresh while the user is active |

## Revisit later

- Clean up the migration pair in Neon (`Initial Migration` + empty `InitialCreate`).
- `.github/` was removed entirely; `AGENTS.md` remains the only project instruction source.
- Password reset: not in the API contract; email provider decision open (default: log to console).
- Expiry job now marks stale holds `Expired` every 60s (batch 100). The Slice 18 retention sweep now
  purges dead rows, old keys and dead refresh tokens; see Slice 18.
- Wallet funding is intentionally non-idempotent (test-only); a real payment provider must own top-up idempotency.
- `me/tickets` is cursor-paginated; see Slice 17.

---

## Slice 2 — events, ticket types, R2 image (DONE)

### What was built
- Domain: Event, TicketType, EventStatus. Wallet FK/index deferred to slice 5.
- Persistence: AppDbContext + migration 20261022135942_AddEventsAndTicketTypes applied.
- Application: DTOs, IOrganizerEventService (Create/Update/Get/AddTicketType/SetImage), IImageStorage, ApiExceptions, Mapperly replacing AutoMapper.
- Infrastructure: OrganizerEventService (combined predicate -> 404, LoadOwnedAsync split, GetAsync, _db.TicketTypes.Add), R2ImageStorage (chunked signing disabled), R2Options merged with MaxImageBytes.
- Api: OrganizerEventsController GET + CreatedAtAction with requested version; Program.cs Mapperly/R2 wiring.

### Rules/decisions
- Ownership via token sub; OrganizerId server-side; foreign/missing -> 404; Event.IsOwnedBy removed.
- Drafts editable, image/ticket types optional; IsEditable Draft-only.
- Cancellation tokens only for real I/O. List<T> in DTOs. Single R2Options. CreatedAtAction uses HttpContext.RequestedApiVersion.

### Lessons learned
- Postgres quote check-constraint columns; EF Guid via navigation can UPDATE -> concurrency; AWS SDK v4 needs signing/checksum tweaks; appsettings BOM/brace.

### Verified
- Create 201 Draft with versioned Location; GET own 200; no token 401; attendee 403; other org 404; /api/v2 404; ticket-type Location to event; JPEG 200 + r2.dev; fake JPEG 400.

### Code map
- Auth: Api/Controllers/AuthController, Application/Interfaces/IIdentityService, Infrastructure/Services/IdentityService, Infrastructure/Seed/IdentitySeeder
- Events: Domain/Entities/Event,TicketType; Application/DTOs; Application/Interfaces/IOrganizerEventService; Infrastructure/Services/OrganizerEventService; Api/Controllers/OrganizerEventsController; Infrastructure/Persistence/AppDbContext
- Storage: Application/Interfaces/IImageStorage, Infrastructure/Storage/R2ImageStorage, Infrastructure/Storage/R2Options
- Mapping: Application/Mapping/EventMapper.cs

---

## Slice 3 — submit/publish, admin review (DONE)

### What was built
- **Domain:** EventStatus (Draft=1, PendingApproval=2, Approved=3, Suspended=4, Published=5) — Rejected removed. Event.RejectionReason added. IsEditable still Draft-only.
- **Persistence:** RejectionReason (max 1000), index on Status. Migration 20261023150659_AddEventRejectionReasonAndPublishedStatus applied.
- **DTOs:** RejectEventRequest (Reason), EventResponse.RejectionReason.
- **Application:** IOrganizerEventService.SubmitAsync/PublishAsync, IAdminEventService (GetQueueAsync/ApproveAsync/RejectAsync).
- **Infrastructure:** OrganizerEventService aggregates submit errors (image, ticket types, dates/rules), clears reason on submit, publish only from Approved. AdminEventService enforces queue/defaults, approve/reject with required reason (trimmed, <=1000), reject -> Draft + reason.
- **Api:** POST /organizer/events/{id}/submit, POST /organizer/events/{id}/publish (organizer). New AdminEventsController [Authorize(Roles=Admin)] GET queue, POST approve/reject under pi/v{version}/admin/events.

### Rules/decisions
- **Publish:** Organizer must publish after approval (deviation from original PDF). Only Approved -> Published; no republish.
- **Edit lock:** After Approved or Published nothing editable (enforced by LoadOwnedDraftAsync + IsEditable). Only publish allowed after approval.
- **Reject:** PendingApproval -> Draft + required reason; organizer can edit/resubmit (reason cleared on submit).
- **Admin:** Never edits content; reason required on reject.
- **Ownership:** combined predicate -> 404 on foreign/missing.

### Verified
- Build clean (1 warning). Migration applied.
- - -  
  
 - - -  
 ## Slice 4 � public browsing + Redis caching (DONE)
- Public API: EventsController [AllowAnonymous] [EnableRateLimiting('public')] under api/v{version:apiVersion}/events (GET /, GET /{id:guid})
- DTOs: EventSummaryResponse (Id,Title,Venue,StartsAt,ImageUrl,MinPrice), EventDetailsResponse (Id,Title,Description,Venue,StartsAt,ImageUrl,List<TicketTypeResponse> TicketTypes)
- Application/Infra: IEventQueryService, EventQueryService + RedisCacheService (StackExchange.Redis), RedisOptions
- Rules: Published AND StartsAt>=now only; cursor by StartsAt then Id, ILike on Title/Venue; CacheKeys; fallback if Redis down; invalidate list prefix + detail on mutations; public 60/min/IP; no stock cached
- OSE: CacheKeys, fail-safe ICacheService, plain Redis supported, no existence leak
- Verified: Build succeeded. Redis wired. DI/rate limiting/invalidation in place.

---

## Slice 5 — wallet (DONE)

### What was built
- `Wallet` entity/service/controller; one wallet per user; auto-created on registration with a lazy fallback on first use.
- Funding capped at 10,000 per request; wallets start at 0.00.
- Persistence correction: `Wallets` table (precision 18,2, unique `UserId`, cascade FK, `CK_Wallets_Balance_NonNegative`), migration `AddWallets` applied to Neon.

### Rules/decisions
- Users cannot withdraw or transfer; funding is a test affordance, not a payment integration.
- Balance is server-owned; the client only sends an amount.

---

## Slice 6 — reservations and stock concurrency (DONE)

### What was built
- `ReservationStatus` (Held, Paid, Expired, Cancelled) + `Reservation`; migration `AddReservations` applied.
- `ReservationService.ReserveAsync`: Published/suspended/sales-open/started/prior-reservation checks, `FOR UPDATE` row lock on the ticket type, stock count of `Paid` + unexpired `Held`, 10-minute hold, price locked in `PriceAtReservation`.
- `ReservationsController` under `api/v{version:apiVersion}`; `ClaimsPrincipalExtensions.GetUserId`.
- Rate limits: public 100/min/IP, reserve 5/min/user, wallet 5/min/user.

### Rules/decisions
- One reservation per user per event, **forever** — expired and cancelled attempts still block a new one.
- A reservation is one ticket.
- Sold out / already reserved → 409; foreign or missing → 404 (no existence leak).
- Stock is never cached; Redis only serves public event content.

---

## Slice 7 — payment, ticket issuance, cancel, idempotency (DONE)

### What was built
- **Domain:** `Ticket` (1:1 reservation, owner/event FKs, unique code, `CheckedInAt`) and `Reservation.PaymentIdempotencyKey`.
- **Persistence:** migrations `AddTicketsAndPaymentIdempotency` and `ScopePaymentIdempotencyKeyToUser`.
- **Wallet:** `FundAsync` uses an atomic increment; `DebitAsync` uses `UPDATE ... WHERE Balance >= amount` and returns 409 when 0 rows match. Debit participates in the caller's transaction (no nested transaction); price 0 skips debit.
- **Application/Infra:** `PaymentResponse`, `IReservationService.PayAsync/CancelAsync`, `GoneException`.
- **Api:** `POST /reservations/{id}/payment`, `DELETE /reservations/{id}`; pay rate limit 5/min/user; `gone` maps to **410** `reservation_expired`.

### Rules/decisions
- Pay requires `Idempotency-Key` (blank or >100 chars → 400).
- Same key on the **same** reservation replays the stored ticket with 200; a different key on a paid reservation → 409.
- The key is stored on the reservation only when payment succeeds, in the same transaction as the wallet debit, ticket insert, and `Paid` status. A failed payment does **not** burn the key, so a retry after funding works.
- **Idempotency keys are scoped per user:** unique `(UserId, PaymentIdempotencyKey)`, not global. A global unique index made two users' identical keys collide; that is semantically wrong.
- Expired or past `ExpiresAt` → 410 for both pay and cancel. Cancel of a held reservation → 204 and is idempotent; cancel of a paid reservation → 409; foreign reservation → 404.
- Ticket code: 8 chars from the CSPRNG safe alphabet `A-HJ-NP-Z2-9` (no I/O/0/1), unique in the DB. A collision rolls back to a savepoint and regenerates, up to 5 tries, because Postgres aborts the whole transaction on a unique violation.
- Concurrency: the reservation row lock serializes pay vs cancel vs double-pay; the atomic wallet update serializes two reservations spending one wallet. A future expiry job must take the same reservation lock and re-check state.
- Wallet funding stays non-idempotent; production top-up must be owned by a payment provider.

### Lessons learned
- Idempotency is per actor, not per table. Scope the key to the user.
- Postgres unique violations poison the transaction; use a savepoint if the operation must retry.
- Two different reservation rows can still contend on one wallet; make the balance update atomic instead of read-modify-write.
- A test harness must use DB connect/statement timeouts; a stalled transaction can otherwise hang an UPDATE indefinitely (test-only fix).

### Verified
- Build clean (0 warnings). Both migrations applied to Neon.
- Manual smoke suite: 50/50 checks passed — public visibility, reserve/pay, insufficient funds, replay, different-key conflict, ownership 404s, cancel paths, 410 expiry, sold out, rate limit 429, double-pay race, one-wallet race, exactly one ticket per paid reservation, no negative wallets, code format.
- Current limits: public 100/min/IP, reserve 5/min/user, pay 5/min/user, wallet 5/min/user.

### Code map
- Payments: `Infrastructure/Services/ReservationService.cs`, `WalletService.cs`; `Api/Controllers/ReservationsController.cs`
- Entities: `Domain/Entities/Reservation.cs`, `Ticket.cs`, `Wallet.cs`; `Domain/Enums/ReservationStatus.cs`
- Errors: `Application/Common/ApiExceptions.cs`; `Api/Program.cs` error mapping
- Persistence: `Infrastructure/Persistence/AppDbContext.cs`; migrations `AddWallets`, `AddReservations`, `AddTicketsAndPaymentIdempotency`, `ScopePaymentIdempotencyKeyToUser`

---

## Slice 7.5 - buy now, 24h idempotency retention (DONE)

### What was built
- `POST /api/v1/events/{eventId}/purchase` (`ReservationsController.Purchase`) with `Idempotency-Key`, body `{ ticketTypeId }`, reusing the `pay` rate limit.
- `Reservation.PaidAt` (nullable, timestamptz). Migration `20261024153814_AddPaidAtToReservations` backfills existing `Paid` rows with `PaidAt = CreatedAt`, then adds the column to Neon.
- `IReservationService.PurchaseAsync` + `PurchaseTicketRequest`.
- `IdempotencyKeyExpiredException` mapped to `409 { code: "idempotency_key_expired" }`.
- Shared `IssueTicketAsync` used by both `PayAsync` and `PurchaseAsync` (wallet debit, Paid transition, key/PaidAt stamp, ticket savepoint retry).
- `RequireIdempotencyKey` extracted in the controller; `Pay` and `Purchase` use the same header validation.
- `ev` renamed to `eventEntity` in `ReservationService` and `EventQueryService` only.

### Rules/decisions
- Buy-now is reserve + pay in one transaction. If the user already has an unexpired `Held` reservation for the same event and ticket type, that reservation is paid instead of creating another.
- A held reservation for a **different** ticket type → 409. A paid/cancelled/expired reservation for the event → 409. An expired hold → 410.
- A direct purchase creates a `Paid` reservation internally, so stock math and the one-ticket-per-reservation rule stay unchanged.
- The 24h retention is measured from `PaidAt`, not creation. Same key + same user + same event/type replays the ticket; after 24h → 409 `idempotency_key_expired`.
- Keys stay stored for uniqueness; the Slice 18 retention sweep clears them after the 24h window. A key reused for a different event/type → 409.
- A failed payment does not burn the key: the reservation/ticket/key change is one transaction.
- Concurrent same-key purchases: the loser sees the unique `(UserId, PaymentIdempotencyKey)` violation, rolls back to a clean transaction, and replays the winner's ticket.
- Existing reserve → pay flow unchanged.

### Verified
- Build clean: **0 warnings, 0 errors**.
- Migration applied to Neon.
- Targeted buy-now smoke suite: **34/34 passed** - direct purchase, paid status, replay same code/reservation, key reuse across ticket type/event, paying an existing hold, hold mismatch 409 then correct type, insufficient funds, failed payment not burning the key, 24h expiry (`idempotency_key_expired`), concurrent same-key 201/201 with one ticket, single-stock purchase, sold-out 409, wallet debits, stored key/PaidAt, one ticket per reservation.
- Earlier Slice 7 regression run: **35/35 checks passed** before the test server was killed; the full 50-check suite was not rerun after the refactor.

### Manual test list (ErA)
1. Fund a fresh attendee, buy a ticket with `Idempotency-Key: p1` → 201, `status: Paid`, one wallet debit.
2. Repeat the same request with `p1` → 201, same reservation id and ticket code, no second debit.
3. Reuse `p1` for a different ticket type or event → 409.
4. Reserve a ticket, then buy the same ticket type with a new key → 201; the original reservation becomes `Paid` and no second reservation is created.
5. Reserve type A, buy type B with a new key → 409.
6. Try buy-now with an empty wallet → 409; fund the wallet, retry with the same key → 201 (key was not burned).
7. Buy, set `PaidAt` to 25 hours ago in the DB, replay the same key → 409 `idempotency_key_expired`.
8. Send two concurrent purchases with the same key → both 201, same ticket, one ticket row.
9. Buy the last ticket, then have another user try → 409 sold out.

---

## Slice 8 - reservation expiry job, buy-after-expiry, history (DONE)

### What was built
- `ReservationExpiryJob` (`BackgroundService` + `PeriodicTimer`): immediate first sweep, then every
  `ReservationExpiry:IntervalSeconds` (default 60, minimum 5); batch `BatchSize` (default 100, 1-1000).
  One parameterized raw SQL statement selects due `Held` rows
  (`ORDER BY ExpiresAt LIMIT @batch FOR UPDATE SKIP LOCKED`) and updates them to `Expired` in the same
  statement. Logs only when rows expire; DB errors are caught/logged and the loop continues.
- `AppDbContext` partial indexes:
  - `IX_Reservations_Status_ExpiresAt` on `(Status, ExpiresAt) WHERE Status = 'Held'` for the job scan.
  - `IX_Reservations_UserId_EventId_Active` unique on `(UserId, EventId) WHERE Status IN ('Held','Paid')`,
    replacing the old unconditional unique index.
- Migrations `20261025145721_AddReservationExpiryIndex` and `20261025145820_MakeReservationUniquenessActiveOnly`
  applied to Neon.
- `ReservationService`:
  - active lookup only `Held`/`Paid`; a stale `Held` row is marked `Expired` and saved before a new `Paid`
    row is inserted;
  - reserve still blocks after `Expired` (409); buy-now allows after `Expired`; `Cancelled` stays blocked;
  - `now` is re-read after every row lock in Reserve/Pay/Cancel/Purchase;
  - `GetMyReservationsAsync` + `MyReservationResponse` + `GET /api/v{version}/me/reservations`
    (Attendee only, newest first).
- `PurchaseAsync` catches both unique indexes (idempotency key and active reservation).
- `Program.cs` registration and `appsettings.json` `ReservationExpiry` config.

### Rules/decisions
- Option A: the old `Expired` row remains. The user cannot reserve that event again, but can buy-now after
  expiry, which creates a **new** `Paid` reservation. `Cancelled` is terminal and does not unlock buying.
- Uniqueness is active-only, so an `Expired`/`Cancelled` row does not collide with a new `Paid` row.
- Raw SQL is used only where it matters: `FOR UPDATE SKIP LOCKED` + `LIMIT` + `now()` in one atomic
  statement claims due rows safely across instances. Everything else stays LINQ.
- Deploy order: migrations must run **before** the new code. Otherwise the old unique index rejects the
  new `Paid` row after expiry.
- History: `IsExpired = Status == Expired || (Status == Held && ExpiresAt <= now)`. A `Paid` row is never
  flagged expired. No pagination, cache or new rate-limit policy.
- The job uses DB `now()`; request paths use app UTC `DateTime.UtcNow`. Both are UTC.
- No purge: `Expired`/`Cancelled` rows and idempotency keys are retained as history.

### Verified
- Build clean: **0 warnings, 0 errors**.
- Both migrations applied; pre-check found no duplicate active rows; old and new indexes verified.
- Slice 8 smoke suite: **112/112 passed** - job expiry and log line, paid/future holds untouched,
  reserve-after-expiry 409, buy-after-expiry new Paid row with old Expired row and one ticket, different
  ticket type, stale hold, cancellation blocking, failed payment not burning the key, sold out after
  expiry, concurrent different-key race (201/409, one ticket), list ownership/isolation/order/fields,
  no token 401, organizer 403.
- Expected 409s are logged by the global handler as `fail:` entries; that is existing behavior, not a
  job failure.

### Manual test list (ErA)
1. Create and publish two events with ticket types; register two attendees and fund their wallets.
2. Attendee A reserves event 1; set that row's `ExpiresAt` to the past in the DB. Within ~60s the job
   marks it `Expired`; the API log says `Expired 1 stale reservation(s).`
3. `GET /api/v1/me/reservations` as A -> the row is `Expired`, `isExpired: true`, no ticket code.
4. Try to reserve event 1 again as A -> 409. Buy-now with a new `Idempotency-Key` -> 201 `Paid`; the list
   now shows the old `Expired` row and the new `Paid` row, with no ticket on the old row.
5. Buy event 1 after expiry with an empty wallet -> 409; fund, retry with the same key -> 201 (key not burned).
6. Cancel a held reservation, then buy the same event -> 409; the row stays `Cancelled`, `isExpired: false`.
7. Set a `Paid` row's `ExpiresAt` to the past -> the job must leave it `Paid`; list `isExpired: false`.
8. Set a `Held` row's `ExpiresAt` in the future -> the job must leave it `Held`.
9. Event with one ticket: A holds the last ticket, let it expire, B buys it -> 201; A buy -> 409 Sold out;
   A's wallet unchanged and A still has one row.
10. Concurrent buy-now for the same user/event with two different keys -> one 201, one 409, one `Paid` row,
    one ticket.
11. No token -> 401 on `me/reservations`; organizer token -> 403.

### Code map
- Job: `Infrastructure/Jobs/ReservationExpiryJob.cs`; config `Api/appsettings.json` (`ReservationExpiry`);
  registration `Api/Program.cs`.
- Indexes/migrations: `Infrastructure/Persistence/AppDbContext.cs`;
  `20261025145721_AddReservationExpiryIndex`, `20261025145820_MakeReservationUniquenessActiveOnly`.
- Purchase/list: `Infrastructure/Services/ReservationService.cs`;
  `Application/Interfaces/IReservationService.cs`; `Application/DTOs/MyReservationResponse.cs`;
  `Api/Controllers/ReservationsController.cs`.

---

## Slice 9 - ticket check-in, attendee ticket history (DONE)

### What was built
- No domain or persistence change. Reused `Ticket.Code` and `Ticket.CheckedInAt`; no migration.
- DTOs:
  - `CheckInTicketRequest` (`Code`, required, max 20).
  - `CheckInTicketResponse` (`TicketId`, `Code`, `EventId`, `EventTitle`, `CheckedInAt`).
  - `MyTicketResponse` (`TicketId`, `Code`, `EventId`, `EventTitle`, `Venue`, `StartsAt`,
    `TicketTypeName`, `PricePaid`, `PaidAt`, `CheckedInAt`).
- Application: `ITicketService.CheckInAsync` and `GetMyTicketsAsync`.
- Infrastructure `TicketService`:
  - Check-in trims and uppercases the code, then looks it up with a join to `Events` scoped to the
    caller's `OrganizerId`. Unknown or foreign code -> 404; already checked in -> 409.
  - The single-use guarantee is a conditional update:
    `WHERE Id = ticketId AND CheckedInAt IS NULL`. Zero rows updated -> 409. This handles two
    simultaneous scans of the same code without a row lock or transaction.
  - `GetMyTicketsAsync` joins `Tickets` -> `Reservations` -> `Events` -> `TicketTypes`, filters by
    `Ticket.UserId`, and orders by `StartsAt` then `TicketId`.
- Api:
  - `TicketsController`: `POST /api/v1/tickets/check-in` (Organizer, `checkin` rate limit 60/min/user)
    and `GET /api/v1/me/tickets` (Attendee).
  - `Program.cs`: `ITicketService` registration and `checkin` policy.

### Rules/decisions
- Only the event owner can check in a ticket; a different organizer gets 404 (no existence leak).
- A suspended event can still be checked in by its owner; event status is not checked.
  Slice 10 adds an organizer-token suspension check before every organizer endpoint, including check-in.
- The first scan succeeds; every later scan returns 409. Concurrent scans still yield exactly one 200.
- Check-in returns only the ticket/event identity and timestamp; it does not expose attendee details.
- `me/tickets` lists paid tickets only. Held/cancelled/expired reservations have no ticket and never
  appear there. Sort is event `StartsAt` ascending, then ticket id.
- `/me/tickets` is cursor-paginated (Slice 17); the response is `CursorPage<MyTicketResponse>`.
- `me/reservations` now shows the same `CheckedInAt` value for paid rows, because both read the ticket.
- Rate limit is 60/min per organizer, matching the gate-scan use case; public limits are unchanged.

### Verified
- Build clean: **0 warnings, 0 errors**.
- Slice 9 smoke suite: **55/55 passed** - empty history, no token 401, organizer 403 on `me/tickets`,
  attendee 403 on check-in, foreign/unknown code 404, blank code 400, lowercase plus surrounding spaces
  accepted, response fields, repeat 409, history ordering and held-ticket exclusion, field mapping,
  attendee isolation, `me/reservations` timestamp, DB `CheckedInAt`, concurrent 200/409 with one
  persisted check-in, and 61st check-in in a minute -> 429.
- Expected 409s appear as `fail:` stack traces in the API log; that is the global handler's existing
  behavior for handled exceptions.

### Manual test list (ErA)
1. Publish an event with a ticket type; register an attendee, fund the wallet, and buy a ticket.
2. As the owning organizer, `POST /api/v1/tickets/check-in` with the code -> 200 with ticket id, code,
   event id, event title, and `checkedInAt`.
3. Repeat the same scan -> 409 `conflict`.
4. Send the code in lowercase with spaces around it -> 200.
5. No token -> 401; attendee -> 403; a different organizer -> 404; unknown code -> 404; blank code -> 400.
6. `GET /api/v1/me/tickets` as the buyer -> own paid tickets only, ordered by `startsAt`; held
   reservations are absent; `pricePaid`, `paidAt`, and `checkedInAt` are correct.
7. Another attendee's ticket is absent; an organizer gets 403; no token gets 401.
8. After check-in, `GET /api/v1/me/reservations` shows the same `checkedInAt`.
9. Send two simultaneous scans of one code -> one 200, one 409, one stored timestamp.
10. Send 61 check-in requests in one minute as one organizer -> the 61st is 429.
11. Set an event to `Suspended` in the DB, then scan its ticket as the owner -> 200 (intentional).

### Code map
- DTOs: `Application/DTOs/CheckInTicketRequest.cs`, `CheckInTicketResponse.cs`, `MyTicketResponse.cs`.
- Service: `Application/Interfaces/ITicketService.cs`, `Infrastructure/Services/TicketService.cs`.
- Api: `Api/Controllers/TicketsController.cs`; `Api/Program.cs`.

---

## Slice 10 - suspension and admin organizer list (DONE)

### What was built
- **Admin organizer list:** `GET /api/v1/admin/organizers` (Admin only), cursor-paged, ordered by
  `Id DESC`, limit 1-50 (default 20). Filters: `q` over first name, last name and email; optional
  `suspended=true|false`. Response `AdminOrganizerResponse`: `Id`, `FirstName`, `LastName`, `Email`,
  `IsSuspended`.
- **Organizer suspension:** `POST /api/v1/admin/organizers/{id}/suspend` and `/unsuspend`, both 204.
  Unknown user -> 404; non-organizer -> 409; already suspended/active -> 409. No event statuses are
  rewritten; public visibility is derived from the user flag.
- **Event suspension:** `POST /api/v1/admin/events/{id}/suspend` and `/unsuspend`, both 200 with
  `EventResponse`. Suspend only from `Approved` or `Published` -> `Suspended`; unsuspend only from
  `Suspended` -> `Approved`; other states -> 409; unknown event -> 404.
- **Public visibility:** `EventQueryService` list and detail now require
  `!_db.Users.Any(u => u.Id == e.OrganizerId && u.IsSuspended)`. Published events from suspended
  organizers disappear without touching their rows.
- **Immediate organizer token block:** JWT `OnTokenValidated` checks `IsSuspended` only when the token
  has the Organizer role. One indexed DB query per organizer request. Attendee/admin tokens skip it.
- **JWT expiry:** `Jwt:ExpiryMinutes = 5`; `ClockSkew` stays at 1 minute, so effective acceptance is
  about 6 minutes. Slice 11 adds the refresh path; Slice 10 remains the suspension behavior.
- **New files:** `Application/DTOs/AdminOrganizerResponse.cs`, `AdminOrganizerQuery.cs`;
  `Application/Interfaces/IAdminOrganizerService.cs`;
  `Infrastructure/Services/AdminOrganizerService.cs`;
  `Api/Controllers/AdminOrganizersController.cs`.
- **Modified files:** `IAdminEventService`, `AdminEventService`, `AdminEventsController`,
  `EventQueryService`, `Program.cs`, `AGENTS.md`. No migration and no NuGet package.

### Rules/decisions
- Organizer suspension is a ban flag, not an event edit. The organizer's events keep their own status;
  public queries combine `Published` with a non-suspended organizer.
- Event suspension is explicit and affects one event. Unsuspend goes to `Approved`, not `Published`;
  the organizer must publish again. This keeps the Slice 3 publish rule intact.
- Organizer suspend/unsuspend clears **both** public cache prefixes because every event and detail of
  that organizer may be cached. Event suspend/unsuspend uses the existing detail + list invalidation.
- Admin organizer list is not cached; it is a rare admin view and can change at any time.
- Suspended organizer login is still 401 `login_failed` with the suspension message. An already-issued
  organizer token is rejected with 401; after unsuspend the same token works again because the check is
  live, not stored in the token.
- Reserve/purchase already returned 404 for a suspended organizer; that behavior is unchanged.
- Check-in: a suspended event can still be scanned by its active owner. A suspended organizer cannot
  reach check-in at all because the JWT check runs before the controller.
- 5-minute tokens are short by design; Slice 11 adds the refresh endpoint and Slice 11's manual list
  covers client-side renewal and idle handling.

### Verified
- Build clean: **0 warnings, 0 errors**.
- Slice 10 smoke suite: **74/74 passed**:
  - admin organizer list fields, role isolation, search, suspended filter, cursor paging, bad filter/cursor;
  - organizer suspend/unsuspend, repeated-state 409s, unknown 404, attendee 409;
  - public list/detail hidden on organizer suspension and restored on unsuspend, including warmed cache;
  - old organizer token 401 during suspension and 200 after unsuspend; suspension login 401 and restored login 200;
  - reserve blocked (404) while organizer suspended;
  - event suspend/unsuspend, status transitions, admin queue visibility, public hiding, owner visibility,
    reserve blocked, republish after unsuspend, draft 409, unknown 404, permissions 401/403;
  - JWT lifetime exactly 300 seconds.
- API stopped after the run; port 5099 free.

### Manual test list (ErA)
1. Register two organizers and one attendee. Create and publish one event per organizer.
2. As admin, `GET /api/v1/admin/organizers?q=<stamp>` -> only organizers, with `isSuspended: false`.
3. Page with `limit=2` and the returned `nextCursor` -> no duplicate ids, `hasMore` correct.
4. Search the attendee email -> empty list; organizer/attendee tokens -> 403; no token -> 401.
5. Load the public list and one event detail, then suspend that event's organizer -> 204.
6. Reload the same public list URL -> that organizer's event is gone; detail -> 404.
7. Use the organizer's old token on an organizer endpoint -> 401; log in -> 401 with the suspension message.
8. Try to reserve the event as the attendee -> 404.
9. Unsuspend the organizer -> 204; public list/detail return; the old token works again; login -> 200.
10. Suspend a published event as admin -> 200 `Suspended`; public detail 404; reserve 404; admin queue
    `?status=Suspended` shows it; owner can still see it.
11. Unsuspend that event -> 200 `Approved`; public detail is still 404; organizer publishes -> 200
    `Published`; public detail returns.
12. Try suspending a draft -> 409; unknown event -> 404; already suspended -> 409; unsuspend a
    non-suspended event -> 409; no token -> 401; non-admin -> 403.
13. Decode a fresh JWT: `exp - nbf = 300`. Remember the 1-minute validation skew.

### Code map
- Organizers: `Application/DTOs/AdminOrganizerResponse.cs`, `AdminOrganizerQuery.cs`;
  `Application/Interfaces/IAdminOrganizerService.cs`;
  `Infrastructure/Services/AdminOrganizerService.cs`;
  `Api/Controllers/AdminOrganizersController.cs`.
- Events: `Application/Interfaces/IAdminEventService.cs`;
  `Infrastructure/Services/AdminEventService.cs`;
  `Api/Controllers/AdminEventsController.cs`.
- Visibility/token: `Infrastructure/Services/EventQueryService.cs`; `Api/Program.cs`.

---

## Slice 11 - refresh token, logout, idle session (DONE)

### What was built
- **Domain:** `RefreshToken` (`Id`, `UserId`, `TokenHash`, `CreatedAt`, `ExpiresAt`, `RevokedAt`).
- **Persistence:** `RefreshTokens` table, unique `TokenHash`, index on `UserId`, cascade FK to the user.
  Migration `20261026000729_AddRefreshTokens` applied to Neon.
- **DTOs:** `RefreshTokenRequest` (`RefreshToken`, required, max 200). `AuthResponse` now carries
  `RefreshToken`, `ExpiresIn` and an optional `ErrorCode`.
- **Application/Infrastructure:** `IIdentityService.RefreshAsync` and `LogoutAsync`; `IdentityService`
  issues a 64-char random token, stores only its SHA-256 hex hash, and slides `ExpiresAt` on refresh.
- **Api:** `POST /api/v1/auth/refresh` and `POST /api/v1/auth/logout`; login now returns
  `{ token, refreshToken, expiresIn }`. New `refresh` rate limit: 60/min/IP, shared by refresh and logout.
- **Config:** `RefreshToken:IdleMinutes = 30` in `appsettings.json`.
- No new NuGet package. No separate database.

### Rules/decisions
- Access token stays **5 minutes**. Refresh token is a **30-minute sliding idle** token: every successful
  refresh extends it by 30 minutes. There is no absolute session cap.
- **No rotation, no token families, no per-request session lookup.** The same refresh token is reused
  until logout, suspension, or 30 minutes without a refresh.
- One row per login. Multiple devices get separate tokens. Logout revokes only the token sent by that
  client, so it is idempotent and never 404s.
- The raw refresh token is never stored; only its hash is.
- Refresh returns a new access token and `expiresIn` only; the refresh token does not change.
- Suspended user: refresh revokes the stored token and returns `401 account_suspended` with
  "This account has been suspended." The existing organizer JWT check already blocks the current access
  token immediately.
- Invalid, expired or revoked refresh token returns `401 refresh_failed` with a generic session message.
- No cleanup job: expired/revoked rows remain as history.
- Client contract: refresh silently just before access expiry (or on the first 401 and retry once). The
  API cannot see scrolling, so the frontend must refresh while the user is active; 30 minutes without a
  refresh means login again. Clear both tokens on refresh failure and show the suspension message when
  `code == "account_suspended"`.
- Logout leaves at most 5 minutes of access-token residual validity.

### Verified
- Build clean: **0 warnings, 0 errors**.
- Migration applied to Neon.
- Slice 11 smoke suite: **39/39 passed**:
  - login returns access + refresh + `expiresIn: 300`; refresh token is 64 uppercase hex chars;
  - DB stores only the hash, one row per login, expiry about 30 minutes;
  - refresh returns a new working access token and extends the same row;
  - missing token 400; unknown/expired/revoked token 401 `refresh_failed`;
  - logout 204, refresh after logout 401, logout idempotent and unknown-token 204;
  - two concurrent refreshes both 200 (no rotation);
  - suspended organizer refresh 401 `account_suspended`, token revoked, then unsuspend cleanup;
  - 61 concurrent refresh requests on one IP: 60 x 200, one 429.
- The initial sequential rate check crossed the fixed-window boundary; the concurrent check on a fresh
  IP confirmed the 60/min policy.

### Manual test list (ErA)
1. Log in; response has `token`, `refreshToken`, `expiresIn: 300`.
2. Call refresh immediately with the token -> 200, new access token, same refresh token, no new DB row.
3. Inspect `RefreshTokens` in Neon: hash only, `ExpiresAt` about 30 minutes ahead.
4. Set that row's `ExpiresAt` to the past, then refresh -> 401 `refresh_failed`.
5. Logout -> 204; refresh the same token -> 401; logout again -> 204.
6. Log in as an organizer, suspend them as admin, then refresh -> 401 `account_suspended`; row has
   `RevokedAt`; unsuspend and log in again.
7. Send two simultaneous refreshes with one token -> both 200.
8. Send 61 refreshes in one minute from one IP -> 60 succeed, one 429.
9. Existing login, register, reservation and check-in flows still work.
10. Frontend: refresh around 4:30, treat scroll/click/keypress as activity, and clear tokens after
    30 minutes without activity.

### Code map
- Entity: `Domain/Entities/RefreshToken.cs`.
- DTO: `Application/DTOs/RefreshTokenRequest.cs`; `AuthResponse.cs`.
- Service: `Application/Interfaces/IIdentityService.cs`; `Infrastructure/Identity/IdentityService.cs`.
- Persistence: `Infrastructure/Persistence/AppDbContext.cs`; migration `20261026000729_AddRefreshTokens`.
- Api: `Api/Controllers/AuthController.cs`; `Api/Program.cs`; `Api/appsettings.json`.

---

## Slice 12 - concurrency and load testing (DONE, lock hold shortened)

### What was built
- k6 **v2.2.0** installed at `C:\Program Files\k6\k6.exe`; not on the current shell PATH.
- Temporary harnesses only, under `%TEMP%\opencode`: `load-setup.ps1`, `race.js`, `write-load.js`,
  `rate-limit.js`, `read-load.js`, and `et_db.py load-snapshot`.
- No production code, NuGet package, migration, or committed test code changed.
- Load data used the current Neon DB with prefix `load.20261026011404`: 23 users and 6 published events.

### Results
- Correctness race: **11/11 checks passed**.
  - Stock: 20 concurrent buyers on 1 ticket -> exactly 1 x 201, 19 x 409, no oversell.
  - Same idempotency key: 5 concurrent replays -> all 201, one reservation/ticket/code.
  - Check-in: 10 concurrent scans -> 1 x 200, 9 x 409, one stored check-in.
  - Wallet: 2 concurrent purchases against a 5.00 wallet -> 1 x 201, 1 x 409, no negative balance.
- Public rate limit: **100 x 200, 1 x 429**; p95 723 ms.
- Cached public reads: 60 VUs for 30 s -> **2,326 requests, 74 req/s, 0 errors, 100% checks,
  p95 577 ms**.
- Authenticated write load: 20 concurrent purchases on one ticket type -> **16 x 201, 4 x 500**.
  - Latency: avg 24 s, p95 36 s.
  - The 4 failures were Npgsql `TimeoutException: Timeout during reading attempt` while waiting on
    `SELECT ... FOR UPDATE` in `ReservationService.PurchaseAsync` (line 430), not expired JWTs.
  - Retrying the same 4 buyers after contention cleared -> all 201.
- Final DB snapshot: Write 20 paid/20 tickets; Stock 1/1 and checked in; SameKey 1/1; Wallet one
  paid/one ticket; Read 0; no oversell, no negative wallets, no duplicate active reservations.

### Finding
- Purchases for the same ticket type serialize on the ticket-type row lock. With Neon latency, 20
  concurrent buyers can exceed the default 30-second Npgsql command timeout and return 500.
- This is a real contention boundary, not a correctness failure: stock, idempotency, and wallet
  invariants held in every completed transaction.
- Fix applied in 12.1: shorten the lock hold; no timeout increase.

### Slice 12.1 - lock hold shortening (DONE)

**What changed (`ReservationService.cs` only)**
- `IssueTicketAsync` no longer debits the wallet. It only marks Paid, stores key/PaidAt, inserts the
  ticket, and keeps the ticket-code savepoint retry.
- New private `DebitIfNeededAsync` used by `PayAsync`, the existing-Held purchase branch, and the new
  purchase branch.
- New private `LockWalletAsync` locks `Wallets` by `UserId` before the new-purchase debit.
- New-purchase order: validate event/ticket type -> lock wallet -> re-check idempotency -> compute price
  and debit -> lock ticket type -> count stock -> insert reservation/ticket -> commit.
- Removed the redundant in-lock idempotency lookup. The early lookup plus the unique
  `(UserId, PaymentIdempotencyKey)` catch still handle replay.
- The ticket-code savepoint stays **after** the ticket-type lock. Rolling back to a savepoint created
  before the lock would release the stock lock and risk overselling.
- No migration, NuGet package, API contract, wallet-service, index, or timeout change.

**Why the wallet lock is needed**
- First attempt debited before the ticket-type lock without re-checking. Five concurrent same-key
  requests then debited repeatedly; some losers hit "Insufficient wallet balance" instead of replaying.
- Locking the user's wallet row serializes same-user purchases before the debit. The re-check under
  that lock lets same-key losers replay the winner without a second debit. Different users use
  different wallet rows and are unaffected.

**Results**
- Build clean: **0 warnings, 0 errors**.
- `race.js`: **11/11 checks passed** on `load.20261026023453`:
  stock 1 x 201 / 19 x 409, same-key 5 x 201 with one ticket, check-in 1 x 200 / 9 x 409, wallet
  1 x 201 / 1 x 409.
- `write-load.js`: **20/20 x 201, 0 x 500**. Latency avg 22.02 s, p95 34.45 s, max 35.87 s.
  - Prior result: 16 x 201, 4 x 500, avg 24 s, p95 36 s.
  - No Npgsql command timeout. The p95 still exceeds the script's 3 s threshold because the last
    buyers wait behind 19 serialized stock-lock holders; total request time is not a single command.
- DB snapshot: Write 20 paid/20 tickets; Stock 1/1 checked in; SameKey 1/1; WalletB 1/1; no oversell,
  no negative wallets, no duplicate active reservations.
  - Stock winner wallet 4.00 (10.00 - 5.00 stock - 1.00 write); the 19 stock losers stayed at 9.00,
    proving sold-out rollback did not charge them.
- `Pay` path: reserve -> pay -> replay same key returned the same ticket/code; wallet debited once.
- Insufficient funds: first buy 409, fund, retry the **same** key -> Paid (key not burned).
- One race run had six transient `NpgsqlException: No such host is known` failures before any work;
  a clean rerun passed. That is environment/DNS, not this change.

### Manual test list (ErA)
1. Keep the API on `http://127.0.0.1:5099`, then run `load-setup.ps1`; it creates 23 users and
   6 published events under a new `load.<timestamp>` prefix.
2. Run `race.js`; confirm the 11 checks pass and inspect `me/reservations`/DB counts if desired.
3. Run `rate-limit.js`; confirm 100 allowed and one 429.
4. Run `read-load.js`; confirm 0 errors and p95 below 1500 ms.
5. Run `write-load.js`; expect **20/20 x 201 and 0 x 500**. Tail latency is still high because buyers
   serialize on the ticket-type lock; that is a remaining performance limit, not a correctness failure.
6. Check the Write event buyers' wallets: each was debited exactly once.
7. Run `et_db.py load-snapshot load-spec.json`; confirm 20/20 Write paid/tickets, no oversell,
   no negative wallets, no duplicate active reservations.
8. Confirm the 19 stock-race losers were **not** charged for the sold-out attempt.
9. Reserve a ticket, pay it, then replay the same `Idempotency-Key`; same ticket/code, wallet debited once.
10. Buy with an empty wallet -> 409; fund it, retry the same key -> 201 (key not burned).

## Slice 13 - frontend API support + React client (DONE)

### What was built
- **CORS:** `Cors:AllowedOrigins` in `appsettings.json` (`http://localhost:5173`); named policy
  `Frontend`; no credentials; `UseCors` runs before authentication. Production origin comes from
  `Cors__AllowedOrigins__0`.
- **Live stock:** `IStockQueryService` + `StockQueryService` count `Paid` plus unexpired `Held`
  reservations and clamp remaining at zero. `TicketTypeResponse` now carries `Sold`, `Held`,
  `Remaining`; `EventSummaryResponse` and `EventResponse` carry `TicketsLeft`.
- **Cache rule:** event content is still cached, but stock is overlaid live after every cache hit or
  miss. Stock is never cached.
- **Organizer data:** `EventDetailsResponse`, `EventResponse` and `MyTicketResponse` carry
  `OrganizerName`; `EventResponse` carries `ImageUrl`; public detail keeps the current early-bird
  price.
- **Organizer list:** `GET /api/v1/organizer/events` returns the caller's events with stock and image.
- **Draft ticket types:** `PUT` and `DELETE /organizer/events/{id}/ticket-types/{ticketTypeId}`.
  Draft-only via the existing ownership/edit checks; update keeps stock counts; delete is blocked
  when any reservation references the tier.
- **Admin queue:** `GET /admin/events?status=All` (case-insensitive) returns all statuses; omitted
  status still defaults to `PendingApproval`. Queue and admin mutation responses hydrate image URL,
  organizer name and stock.
- **Wallet ledger:** `WalletTransaction` + `WalletTransactionType` (`Fund`, `Purchase`), migration
  `20261026035048_AddWalletTransactions` applied to Neon. `GET /wallet` returns balance plus the
  latest 20 transactions, newest first. Funding writes a `Fund` row in the same transaction as the
  balance update; payment writes a `Purchase` row with a negative amount and the ticket code as the
  reference, in the payment transaction.
- **No new NuGet package.** No category filter or category UI, per ErA.

### Rules/decisions
- The ledger is signed: funding is positive, purchase is negative. There is no backfill; old wallet
  balances have no history until new activity.
- Stock counts are derived from reservations, not cached and not trusted from the client.
- Admin `All` is explicit; a missing or blank status still means pending.
- Ticket-type edit/delete is draft-only, matching the existing edit lock.
- The React client will use Vite + React Router + TypeScript + plain CSS under `frontend/`.

### Verified
- Build clean: **0 warnings, 0 errors**.
- Migration applied; `WalletTransactions` table and `(UserId, CreatedAt)` index created.
- Targeted smoke run: CORS preflight 204 with the localhost origin; public list shape; attendee
  register/login; two fund calls created two ledger rows; wallet history newest first; organizer
  register/create/list; draft tier add/update/delete; stock fields; real buy-now purchase created a
  `Purchase` row with reference `EC2LMBFV` and the public stock fell from 2 to 1; `/me/tickets`
  returned the organizer name.
- Expected API behavior after edits has not been re-run under k6.

### Manual test list (ErA)
1. `OPTIONS /api/v1/events` with `Origin: http://localhost:5173` -> 204 and the origin echoed.
2. Load a public event, note `ticketsLeft`; buy one ticket, reload, confirm the count fell.
3. Fund a fresh attendee twice; `GET /wallet` shows both rows newest first with positive amounts.
4. Buy a ticket; confirm a negative `Purchase` row whose reference is the ticket code and that replay
   does not add a second row.
5. As an organizer, `GET /organizer/events`; add a draft tier, edit it, then delete it -> 204.
6. As admin, `GET /admin/events?status=All` -> all statuses, with organizer/image/stock fields.
7. Approve an event; confirm the response carries organizer name, image URL and stock.
8. Confirm no category filter or category stickers are expected anywhere in the UI.

### React client (DONE)

**Stack and wiring**
- React 19 + Vite 8 + TypeScript 6 + React Router 7, plain CSS, no UI kit, query library, Redux or
  Next.js. Source lives in `frontend/`; Cloudflare SPA fallback is `frontend/public/_redirects`.
- `VITE_API_BASE_URL` points at the versioned API (`http://127.0.0.1:5276/api/v1` by default);
  `.env.example` documents overrides and `.env` is ignored.
- `main.tsx` loads `styles/global.css`; `App.tsx` wires BrowserRouter, ToastProvider, AuthProvider,
  AppShell, scroll-to-top and all public/attendee/organizer/admin routes. Missing routes render a
  404 page.
- Role guards: attendee pages require `Attendee`, organizer pages require `Organizer`, admin pages
  require `Admin`. The API still enforces every rule; guards are only navigation.

**Screens and behaviour**
- **Public:** event grid with hero, countdown, filters and favorites; event detail with live stock,
  early-bird price, tier cards, prior-hold/paid/expired/cancelled states.
- **Auth:** login and signup with organizer checkbox, Identity error details, role-based landing and
  `?next=` return path. Registration calls login afterwards because register returns no token.
- **Attendee:** wallet (fund + latest 20 ledger rows), checkout (hold countdown, wallet top-up, pay,
  cancel), my tickets (active holds, flippable ticket front/back, copy code, check-in timestamp).
- **Organizer:** event list with stock and publish action; draft editor (basics, tier CRUD, JPEG
  upload, save draft, save-and-submit); manage page (submit, publish, rejection reason, image, stock);
  check-in page with success/duplicate/not-found results and a local session list.
- **Admin:** review queue with approve/reject reason modal; all-status event list with
  suspend/unsuspend; organizer search, active/suspended filter, cursor "Load more", suspend/unsuspend.

**Client rules**
- JWT session uses localStorage. The client refreshes 15 seconds before expiry or on the first 401,
  retries once, keeps the same refresh token and clears both tokens on failure. Logout revokes the
  stored refresh token. localStorage is an MVP trade-off; httpOnly cookies would be the hardening step.
- One `Idempotency-Key` is generated per checkout/purchase attempt and reused on retry.
- Times are converted between WAT input/display and UTC API values; money uses `en-NG` NGN formatting.
- Event page treats `isExpired` as well as `Expired` so a stale hold immediately becomes buy-now
  before the expiry job flips the row. A cancelled reservation stays terminal.
- Guests can click a tier to reach login. Organizers/admins cannot start a reservation.
- API errors use `{ code, message, details }`; `errorMessage` prefers field details for validation.
- No automated tests are committed; temporary Playwright scripts live outside the repo.
- HTTPS redirection is skipped in Development. Otherwise the Visual Studio `https` profile redirects
  local HTTP API calls with a 307 before CORS runs, producing a misleading browser CORS failure. The
  frontend talks to `http://127.0.0.1:5276` locally; production still applies HTTPS redirection.

**Verified**
- `npm run build`: clean (0 TS errors); `npm run lint`: **0 errors, 16 warnings** (Fast Refresh
  co-exports, async load effects, `Date.now()` purity). Backend `dotnet build -c Release`: 0 warnings.
- Browser suite **28/28**: organizer signup -> draft with JPEG upload (R2) -> submit -> admin approve
  -> publish -> attendee wallet fund -> reserve -> pay -> ticket flip -> wallet debit -> second-ticket
  block -> organizer check-in (lowercase/spaces accepted) -> duplicate 409 -> admin organizer search,
  suspend, public hide, unsuspend, public return. No runtime errors; the logged 409/404 console lines
  are the expected duplicate-scan and suspended-event responses.
- Expiry suite **11/11**: reserve -> expire in DB -> ended-hold checkout -> event offers buy-now before
  the job runs -> purchase -> exactly one debit -> history has the old Expired hold and new Paid row.
- Event suspension suite **11/11**: admin suspend -> public 404 -> organizer sees Suspended ->
  unsuspend -> stays Approved/hidden -> organizer republishes -> public returns; forced token expiry
  refreshes the access token without rotating the refresh token.
- Layout suite **27/27**: 12 routes at 1440px and 390px have no horizontal overflow; ticket back
  matches the front at 390px; no runtime errors.

### Manual test list (ErA - frontend)
1. Start the API in Development and `npm run dev` in `frontend/`; open `http://localhost:5173`.
2. Sign up as an organizer, create an event with a future WAT date, one tier and a JPEG cover; save
   and submit. Confirm the manage page says In review.
3. Log in as admin, approve it, then return as the organizer and publish. The event appears publicly.
4. Sign up as an attendee, fund the wallet, open the event, reserve, wait for the countdown, pay.
   Confirm one ticket, one purchase ledger row and the event page blocking a second ticket.
5. Flip the ticket, copy the code, then check it in as the owning organizer. Repeat the scan -> 409.
6. Cancel a fresh hold and confirm the event cannot be reserved again; after an expired hold use
   Buy now and confirm the old hold stays Expired and the new ticket is Paid.
7. As admin, suspend the organizer -> public event disappears; unsuspend -> returns. Suspend the event
   -> public 404; unsuspend -> Approved but still hidden until the organizer republishes.
8. Resize to a phone width and confirm nav, event page, checkout, ticket and admin tables stay usable.
9. Leave the app open past five minutes and navigate; the access token refreshes silently. Log out
   and confirm protected URLs return to login.

## Slice 14 - portfolio demo: seeded events, search, admin users (DONE)

### What was built
- **Organizer identity:** `EventTicketingUser.OrganizationName` (max 200). Organizer signup takes only
  organization name; attendee signup still takes first/last. Display order everywhere is
  organization name -> full name -> email. `EventResponse`, public details, tickets, admin events and
  admin organizers now expose the organizer name.
- **Newest-first discovery:** `Event.CreatedAt` (timestamptz, default `now()`) plus
  `(Status, CreatedAt)` index. Public list orders by `CreatedAt DESC, Id DESC` with a matching cursor;
  `HasMore` is derived from a real next cursor, not a page-size heuristic. Search `q` filters title and
  venue; the Redis list key includes `q`.
- **Admin users:** read-only `GET /api/v1/admin/users` (Admin only), cursor-paged, limit 1-50, optional
  `q` over first name, last name, organization and email, optional `role`. Returns
  `AdminUserResponse` with roles and suspension state. No create, suspend or password data.
- **Admin organizer data:** the existing list now returns `OrganizationName` and searches it.
- **Admin event search:** existing queue path accepts `q` over title, venue, organizer name and
  organization; `status=All` still returns every status.
- **Migration:** `20261026184921_AddOrganizationNameAndEventCreatedAt` applied to Neon. It replaces the
  earlier two-migration plan with one migration.
- **Demo data:** `DemoDataSeeder` runs after `IdentitySeeder`. It is opt-in through
  `Seed:DemoOrganizerEmail` / `Seed:DemoOrganizerPassword`. On a fresh database it creates one organizer
  plus exactly five Published events, three tiers each, and R2 covers. The seeder uses fixed event IDs
  and sets `ImagePath` to `events/{eventId}/cover.jpg`; those five objects already exist in R2, so no
  local image files or upload step are needed. Seeding is idempotent: existing events are not
  overwritten and existing `ImagePath` values are not replaced.
- **Frontend:** organization field on organizer signup; `/admin/users` directory; search on public home,
  admin users, admin organizers, admin events and admin review; organizer My Events client-side search;
  buy-now on event tiers with a secondary 10-minute hold; 4:3 cover frames on event cards and detail;
  admin review 4:3 preview; Users nav item; organization shown in admin organizer results.
- **Session config:** `RefreshToken:IdleMinutes` is 480 (8 hours). The frontend still refreshes shortly
  before access expiry and reuses the same refresh token.

### Rules/decisions
- Existing local events stay. ErA explicitly rejected archiving the old test events; the five demo events
  are added alongside them. A fresh Render database, however, gets only the seeded organizer and five
  events.
- Local demo events were reordered with a one-off DB update so they occupy the top of the public list.
  On a fresh database the seeder orders them by `seededAt - CreatedOrder seconds`.
- Demo credentials never live in source. They are user secrets locally and Render env vars in production.
  Without them the demo seeder skips instead of inventing a default password.
- Admin user view is strictly read-only. Suspension remains on the organizer endpoints.
- Search uses server-side `ILike` where the dataset can grow; organizer My Events is client-side because
  the page already holds the caller's list.
- Buy-now reuses the existing purchase endpoint. The client keeps one idempotency key per event/tier
  attempt and reuses it on retry; a successful purchase clears it.
- Photo frames are CSS `aspect-ratio: 4/3`; the ticket shape stays 2.4:1 because that is its visual
  identity, not a gallery image.
- No new NuGet package, no new frontend dependency, no committed test code.

### Verified
- Backend `dotnet build EventTicketing.slnx -c Release`: **0 warnings, 0 errors**.
- Frontend `npm run build`: clean, 0 TypeScript errors.
- `npm run lint`: **0 errors, 18 warnings** (pre-existing effect/purity/Fast Refresh patterns; no new
  exhaustive-deps warning).
- Browser smoke suite: **24/24 passed** - five demo events first, all five 4:3 covers load, public search,
  hero unchanged by search, organizer signup with only organization name, admin users row and role,
  admin organizer demo search, admin event search all five demo events, empty admin review search, attendee
  wallet funding, event 4:3 hero, Buy now + Hold 10 min, 8-char ticket, one ticket, one 6000 balance after
  a 4000 early-bird purchase, ledger reference equals ticket code, second ticket blocked, no console errors.
- Temporary smoke users and their ticket/reservation/wallet rows were removed afterwards; the local DB is
  back to the seeded demo events and pre-existing data.
- API list smoke after the seed and reorder: `/events?limit=6` put the demo events first and returned
  `hasMore: true` after the cursor fix.

### Manual test list (ErA)
1. Start the API in Development and `npm run dev`; open the home page. Confirm Neon, Punchline, Sunset,
   Headphone and Culture are first, each has a real 4:3 cover, and the public search returns matches.
2. Sign up as an organizer with an organization name only; confirm the account lands on My Events.
3. Log in as admin; search the new organization on Users, then open Organizers and search `Demo Nights`.
4. Search `Demo Nights` on Admin Events: all five demo events appear. Search it on Review: no pending
   matches, because they are already Published.
5. Sign up as an attendee, fund 10,000, open Culture Yard and buy the 4,000 Community tier. Confirm one
   ticket, a 6,000 balance and one negative ledger row referencing the ticket code.
6. Reload the event as that attendee: Buy now is gone and View ticket is shown.
7. Confirm the existing local events (including Pool Party All Night) were not archived and demo
   covers are not overwritten on the next API start.

### Code map
- Identity/migration: `Domain/Entities/EventTicketingUser.cs` (inherited), `Infrastructure/Persistence/AppDbContext.cs`,
  `20261026184921_AddOrganizationNameAndEventCreatedAt`.
- Seeding: `Infrastructure/Seed/DemoDataSeeder.cs`, `Api/Program.cs`, `Api/EventTicketing.Api.csproj`.
  Cover images live only in R2 under the fixed `events/{eventId}/cover.jpg` keys.
- Discovery: `Infrastructure/Services/EventQueryService.cs`; admin event search in
  `Infrastructure/Services/AdminEventService.cs`.
- Admin users: `Application/DTOs/AdminUserResponse.cs`, `AdminUserQuery.cs`,
  `Application/Interfaces/IAdminUserService.cs`, `Infrastructure/Services/AdminUserService.cs`,
  `Api/Controllers/AdminUsersController.cs`.
- Frontend: `pages/attendee/HomePage.tsx`, `EventPage.tsx`, `components/TicketTier.tsx`;
  `pages/AuthPage.tsx`, `context/AuthContext.tsx`; `pages/admin/AdminUsersPage.tsx`,
  `AdminOrganizersPage.tsx`, `AdminEventsPage.tsx`, `AdminReviewPage.tsx`; `pages/organizer/OrganizerEventsPage.tsx`;
  `App.tsx`, `components/AppShell.tsx`, `api/types.ts`, `styles/global.css`.

---

## Slice 15 - automated test suite (DONE)

### Backend integration tests
- One `EventTicketing.Tests` project using xUnit v3 against the existing Neon `neondb`. No EF
  InMemory, no new database/schema, no truncation. Tests use the prefix `test-<runid>-` and clean
  only their own rows plus the run's admin.
- `ApiFactory` sets configuration through process environment variables before the host is built.
  In-memory `ConfigureAppConfiguration` runs too late for the top-level `Program.cs` checks. JWT,
  Neon connection, admin seed, R2/Redis placeholders and demo-seed suppression are all set there.
  Cache and image storage are fakes; the expiry job is removed.
- Run from the built executable, not `dotnet test`:
  `& ".\\EventTicketing.Tests\\bin\\Debug\\net10.0\\EventTicketing.Tests.exe" -class <class> -noColor`
- **61/61 tests passing** across 12 classes: auth, public events, organizer events, admin events,
  admin users/organizers, wallet, reservations, payments, tickets/check-in, concurrency, security,
  startup/seeding.
- The full suite has not been run in one process; each class has passed independently. That is
  acceptable for a frontend-only change and saves the long Neon run.
- Coverage includes stock races, same-key purchase replays, concurrent check-in, wallet races,
  idempotency retention, role boundaries, CORS, auth rate limits, migrations and seeded admin.
- Gaps: no empty-database migration/seed test, no real Redis/R2 run, and k6 load tests remain
  manual. Those need separate resources or schema approval.

### Frontend tests
- Dev dependencies added: `vitest`, `jsdom`, `@testing-library/react`,
  `@testing-library/jest-dom`, `@testing-library/user-event`, `@playwright/test`.
- `npm test` -> **37/37 Vitest tests** in 6 files. Covers formatting/WAT conversion, JWT claim
  parsing, favorites, API client auth/refresh/error mapping, `EventCard`, and `TicketTier`.
- `npm run build` is clean. `npm run lint` has **0 errors and 18 existing warnings**.
- `npx playwright test` -> **6/6 browser tests** when the E2E credentials are supplied. Public
  flows cover home/search/detail, guest redirects, 404, signup validation, bad login and phone
  overflow. Authenticated flows use `E2E_ADMIN_*` and `E2E_ORGANIZER_*`; they skip when those are
  unset.
- Playwright starts the API and Vite itself. Output folders are ignored in `frontend/.gitignore`.
- Logout fix: `AuthContext.logout` now commits `navigate('/', { replace: true })` with
  `flushSync` before clearing the session. Without that, `RequireRole` redirected protected pages
  to `/login?next=...` first. Backend tests were not rerun for this frontend-only change.

---

## Slice 16 - frontend UX hardening and mobile pass (DONE)

### What was built
- **Safe error copy:** network failures show "We couldn't connect right now. Please try again in
  a moment."; unexpected 5xx responses show "Something went wrong. Please try again." Developer
  terms and backend details are logged, never rendered. Raw `Error` messages fall back to the
  generic message. The sign-in role error is now "Sign-in failed. Please try again."
- **Search styling and sizing:** every `input[type=search]` uses the themed search control.
  `.ci input` had applied a `240px` flex basis inside the column-layout `.field`, which made the
  Home and admin search boxes about 240px tall. Horizontal flex now applies only to `.ci > input`
  and `.ci > .field`; nested search inputs no longer grow vertically. Inline flex styles were
  removed from the five search pages. Search behavior itself is unchanged.
- **Inline validation:** signup password mismatch is a field error with `aria-invalid`,
  `aria-describedby`, focus on the confirm field, and scroll into view. Organizer event validation
  and server alerts moved from the page top to the action panel above save/submit buttons.
- **Mobile navigation:** below 700px the desktop links collapse into a `Navigation menu` toggle.
  The menu has a scrim, Escape and history-close handling, first-link focus, focus return on close,
  and role-specific links plus logout. The toggle keeps a stable accessible name and uses
  `aria-expanded` for state.
- **Mobile sizing:** interactive controls get 44px minimum touch targets; modals cap their height
  and scroll internally; `index.html` uses `viewport-fit=cover`; `100dvh` handles mobile browser
  chrome. Admin tables keep their existing horizontal scroll.
- **Modal focus:** `Modal` now traps Tab, restores focus to the opener, and locks body scrolling
  while open.
- No new source file, NuGet/npm package, backend change, migration, or table redesign.

### Verified
- `npm run build`: clean, 0 TypeScript errors.
- `npm run lint`: **0 errors, 18 existing warnings**; no new warning.
- `npm test`: **38/38 Vitest tests** in 6 files.
- `npx playwright test`: **7/7** with admin and organizer credentials. Added mobile menu
  open/close/navigation/focus and 44px target checks, phone-width password-mismatch focus and
  visibility checks, themed search plus height assertions, admin mobile-menu navigation/logout,
  and organizer create-form alerts staying in view.
- Backend was not rerun because no backend file changed.

### Manual checks remaining
1. At 360px, 390px, 430px and landscape, open the menu, navigate, press Escape and use browser Back.
2. Confirm every menu item and primary button is comfortable to tap.
3. Open the admin reject modal on a short phone viewport; confirm it scrolls internally and returns
   focus to the Reject button.
4. Submit an invalid event form on a phone; confirm the alert sits beside the save buttons.

---

## Slice 17 - ticket history pagination (DONE)

### What was built
- `GET /api/v1/me/tickets` now accepts `cursor` and `limit` and returns
  `CursorPage<MyTicketResponse>`.
- Default page size is 20; maximum is 50. Ordering stays `StartsAt ASC, TicketId ASC`.
- The cursor is base64 JSON containing `StartsAt` and `TicketId`; an invalid cursor returns
  `400 validation_failed` with "Invalid cursor.".
- `TicketService` fetches `limit + 1`, trims the extra row, and builds `NextCursor` only when
  more rows exist.
- `MyTicketsPage` uses the cursor response, adds **Load more**, changes its badge to
  "X shown", and automatically loads further pages when `?new=<ticketId>` is not on page one.
- Holds from `/me/reservations` are unchanged.

### Rules/decisions
- No new database table, migration, package, or source file.
- The existing event-date ordering is preserved so tickets stay chronological.
- The page has no total count because `CursorPage` carries only the current items. The
  badge therefore reports loaded rows, not lifetime issuance.
- The `?new` auto-load can make several requests for a large history; it stops as soon as
  the new ticket is found or the last page is reached.

### Verified
- Backend build: **0 warnings, 0 errors**.
- New pagination integration test: **1/1 passed** (two pages, stable order, no duplicates,
  invalid cursor 400).
- Existing `MyTickets_ListsOnlyPaidOwnTicketsInStartOrderAndShowsCheckIn`: **1/1 passed**.
- Frontend build: clean.
- Frontend lint: **0 errors, 18 existing warnings**.
- Vitest: **38/38 passed**.
- Playwright: 4 passed, 2 skipped, 1 existing public-home failure; the home page was still
  showing "Finding events" after the 10-second assertion during a slow API start. The
  touched ticket page is not covered by the Playwright suite.
- The ticket class run also hit the known fixed-window check-in rate-limit test failure when
  its 60 sequential requests crossed the one-minute boundary; that test is unrelated to
  this slice.

### Manual test list (ErA)
1. Open My tickets as an attendee with fewer than 20 tickets: no Load more button.
2. Create more than 20 paid tickets; confirm the first page shows 20, Load more appends the
   rest, and no ticket appears twice.
3. Confirm tickets remain ordered by event date and ticket id across pages.
4. Call `/api/v1/me/tickets?cursor=not-base64` -> 400.
5. Buy a ticket and land on My tickets with `?new=<ticketId>`; confirm the New sticker appears
   even when the ticket is beyond the first page.
6. Confirm active holds still appear above the ticket list.

---

## Repository cleanup (pre-GitHub, DONE)

- Added the root `.gitignore` for .NET build output, IDE files, frontend dependencies/build output,
  local secrets, generated test reports, `/docs/`, and local scratch files.
- Kept the existing `frontend/.gitignore`; it handles frontend-specific generated output.
- Updated `.dockerignore` for `docs/`, generated test output, and removed the old documentation paths.
- Moved `Brief and Guidiance/`, `Website inspo/`, and `AI_WORKING_TEMPLATE.md` into ignored local
  `docs/`.
- Deleted `ctx_tmp.cs`, unused `frontend/src/App.css`, `frontend/src/index.css`, and the unused starter
  images in `frontend/src/assets/`.
- Removed `EventSeedImages/` and its API content-copy entry. Demo covers already exist in R2, so the
  seeder now stores their fixed keys directly.
- Removed `.github/` entirely; `AGENTS.md` remains the only project instruction source.
- No Git repository was initialized and no files were committed.

---

## Slice 18 - refresh-token rotation, absolute cap, data-retention purge (DONE)

### What was built
- **Refresh-token rotation with reuse detection.**
  - `RefreshToken` gains `FamilyId`, `ReplacedByTokenHash`, `AbsoluteExpiresAt`.
  - Every successful refresh issues a **new** token in the same family; the old row is revoked and
    linked through `ReplacedByTokenHash`. History is one row per rotation.
  - Replaying a revoked token is allowed only inside a 10-second grace window (client retry
    protection). After that the **whole family is revoked** (token-theft detection).
  - Absolute cap: `RefreshToken:AbsoluteMinutes` (default 10080 = 7 days). The access token is
    clamped to the session's absolute end, so it can never outlive the family; `expiresIn` reflects it.
  - `RefreshAsync` locks the row with `FOR UPDATE` (`FromSqlInterpolated`) so concurrent refreshes
    serialize; `LogoutAsync` revokes the entire family. login returns `refreshToken` again and every
    refresh returns a fresh pair.
- **Data-retention job:** `DataRetentionService` + `DataRetentionJob` (immediate first sweep, then
  `DataRetention:IntervalMinutes`, default 60, minimum 5).
  - Deletes `Expired`/`Cancelled` reservations whose event `StartsAt` is 30+ days past **and** which
    have no ticket (`DeadReservationDaysAfterEvent`).
  - Clears `PaymentIdempotencyKey` from `Paid` reservations older than the 24h replay window; the row
    is retained, only the key is nulled.
  - Deletes refresh tokens 7 days (`RefreshTokenDaysAfterExpiry`) after `ExpiresAt`, `RevokedAt` or
    `AbsoluteExpiresAt`.
  - All three are batched raw SQL with `FOR UPDATE SKIP LOCKED`; batch 500 and max 20 batches per
    sweep, both config-validated (fail-fast construction).
  - `ReservationService`: replays against a paid reservation whose key was cleared are handled
    (`EnsureClearedIdempotencyKeyNotExpired` -> `idempotency_key_expired` when past the window);
    `IdempotencyRetention` became internal so the retention service reuses the same constant.
- **Config changes:** `Jwt:ExpiryMinutes` 5 -> **15**; new `DataRetention` section in appsettings.json.
- **Migration `20261029214056_AddRefreshTokenRotationAndRetentionIndexes` (applied to Neon):**
  3 new RefreshToken columns; `IX_RefreshTokens_{FamilyId,ExpiresAt,AbsoluteExpiresAt,RevokedAt}`;
  `IX_Reservations_Status_ExpiresAt_Dead` (Expired/Cancelled filter);
  `IX_Reservations_PaidAt_Idempotency` (key-not-null filter); `IX_Events_StartsAt`.
- **Behavior change for old rows:** the migration defaults `AbsoluteExpiresAt` to `-infinity`, so any
  pre-existing refresh token dies on its next refresh and every client must log in once.

### Rules/decisions
- Family revocation is the accepted trade-off: after the grace window, replaying any rotated token
  kills the whole family - attacker and legit client both re-login. Simple, standard, no token-family
  metadata beyond `FamilyId` + `ReplacedByTokenHash`.
- The deletion of tickets-bearing reservations is explicitly forbidden by the ticket `NOT EXISTS`
  check - check-in history stays complete forever.
- Key clearing keeps the reservation row (uniqueness/history) while ending the replay guarantee after
  24h, matching the pre-existing `idempotency_key_expired` semantics.
- All purges keep ACID batch small and can be run repeatedly; nothing deletes `Paid` rows or rows with
  tickets.

### Verified (against Neon; API on port 5599 then stopped, port free)
- Build clean: **0 warnings, 0 errors**. All 14 prior migrations confirmed applied before this one.
- Smoke suite passed: login -> 900s access + 64-char refresh; rotate -> new pair; grace replay of the
  old token (<10 s) -> 200 with a further new pair; late reuse (>10 s) -> 401 refresh_failed and the
  whole family revoked (the rotated child also 401); DB rows show `revoked=true` +
  `ReplacedByTokenHash` set on each rotated row, unrelated family unaffected; logout -> 204 ->
  refresh 401, second logout refresh 401 (idempotent).
- Public events still read (data intact - 181 reservations in the dev DB).
- Service-level check left to ErA's list below (token `AbsoluteExpiresAt`, family rows).

### Manual test list (ErA)
1. Login -> `token`, `refreshToken`, `expiresIn: 900`.
2. Refresh -> new `token` + **new** `refreshToken`; DB shows the old row revoked with
   `ReplacedByTokenHash` pointing at the new row's hash.
3. Send two refresh calls back-to-back with the same old token -> both 200 (grace window).
4. Log in on two devices; refresh each independently -> both families keep working.
5. Reuse a rotated token after 10 s -> 401; the newer token of that family is dead too -> relologin.
6. `Refresh:AbsoluteMinutes` cap: set a token's `AbsoluteExpiresAt` to the past in the DB -> refresh
   401 `refresh_failed` (never extends).
7. `__EFMigrationsHistory` contains `20261029214056…`; `RefreshTokens` has the three new columns and
   four new indexes.
8. Data-retention purge: set one Expired reservation's event `StartsAt` to 31 days ago -> within one
   hour (or restart) the sweep deletes it and logs the count; a Paid reservation or one holding a
   ticket is never deleted.
9. Set a Paid reservation's `PaidAt` to 25 h ago -> next sweep nulls its key; replaying that key then
   maps to the expired-key/already-paid response.
10. Restart the API with nothing due -> no retention log line (silence is normal).

---

## Next
- **Render deployment is paused by ErA for now.** The Render/Cloudflare notes below are future
  reference only.
- **Git is set up and pushed:** the repo is initialized with remote `origin` at
  `https://github.com/ErA-16/EventTicketing.git`; `main` is pushed through `2afed67`
  (`Slice 18: refresh-token rotation + data-retention purge`); the working tree is clean.
  Do not push again without permission.
- `Dockerfile` + `.dockerignore` now exist at the repo root. The API image uses .NET 10 multi-stage,
  listens on Render's `PORT` (fallback 8080), and runs as the non-root `app` user. Docker is not
  installed locally, so the image itself has not been built; `dotnet publish -c Release` was verified,
  and the published DLL started with Production env vars and `--urls` -> public events 200.
- Render deployment: create a Docker web service in Ohio from the GitHub repo, then set
  `ConnectionStrings__DefaultConnection`, `Jwt__Key`, `Seed__AdminEmail`, `Seed__AdminPassword`,
  `R2__AccessKeyId`, `R2__SecretAccessKey`, `Redis__Configuration` and `Cors__AllowedOrigins__0`.
  Non-secret R2/JWT issuer/audience/expiry and `RefreshToken:IdleMinutes` already live in
  `appsettings.json`. Apply EF migrations before the new image starts if the schema changed.
- Cloudflare Pages: build `frontend/`, set `VITE_API_BASE_URL=https://<render-host>/api/v1`, and add
  the Pages domain to `Cors__AllowedOrigins__0`. `_redirects` supplies the SPA fallback.
- Redis is optional; decide its host/region before production. R2 image upload is now proven from the
  client but production CORS/bucket policy must still be checked.
- The write test has **0 failures**, but p95 is still ~34 s because 20 buyers serialize on one
  ticket-type row. Decide whether to accept this or move to Option B (split reserve/pay) or Option C
  (atomic `ReservedQuantity` counter) before production load.
- Slice 18 is committed and pushed (`2afed67`). The working tree is clean; commit future slices only
  on ErA's say-so.
- Housekeeping: clean the Neon migration pair and remove the temporary `fe.*` test users/events if the
  shared DB should stay tidy.
