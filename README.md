# APAC Claims Platform: take-home

This is a claims platform for motor and property claims across six APAC markets. Claimants report incidents, track progress, answer questions and receive decisions. Claims staff work a queue, assess claims, request information, and approve, settle or decline them. Staff can also see team workload and outstanding liability.

* **Backend:** .NET 10 minimal API, EF Core (SQLite), transactional outbox, and Kafka-compatible event consumers
* **Frontend:** Angular 21 (standalone, zoneless, signals), one app with two role-scoped areas
* **Messaging:** REST for commands and queries; Kafka for domain events (in-process transport by default, so it runs with zero infrastructure)

---

## Run it locally

Prerequisites: .NET 10 SDK and Node 22 or 24. Docker is optional and only needed for real Kafka.

```bash
# 1. API  -> http://localhost:5080  (interactive docs: /scalar, OpenAPI: /openapi/v1.json)
cd backend/src/Claims.Api
dotnet run --launch-profile http

# 2. Web  -> http://127.0.0.1:4200  (proxies /api to the API)
cd frontend
npm install
npm start
```

On first start the API creates `claims.db` and seeds 13 demo claims. To reset, delete `claims.db`.

| Sign in as | Role | What to try |
|---|---|---|
| Alice Tan / Ben Wong / Chloe Nguyen | Claimant | Report an incident, answer an information request, watch the Updates feed |
| Hana Lee / Raj Patel / Mia Chen | Claims staff | Pick up from the queue, request info, adjust the reserve, approve, settle, decline; open Team dashboard |

There's no password. The login picker stands in for SSO or a customer IdP (see Shortcuts).

**Tests:** `cd backend && dotnet test`. There are 15 tests. Unit tests cover the state machine. Integration tests run the real API and cover access control, concurrent pickup, stale `If-Match`, and events reaching the read model and notifications.

**With real Kafka:** `docker compose up -d`, then `dotnet run --launch-profile http --Messaging:Transport=Kafka`. Kafka UI runs at http://localhost:8085.
> Docker wasn't available on the machine this was built on, so the Kafka path was **not run end-to-end**. It compiles and uses the same consumers as the tested in-process transport.

`backend/claims.http` contains a scripted API walkthrough.

---

## Architecture

```
 Angular (one app)                       Claims API (.NET, modular monolith)
 ┌───────────────┐   REST + JWT      ┌──────────────────────────────────────────────┐
 │ /claimant/**  │ ───────────────▶  │ Claim aggregate (state machine, rules)        │
 │ /staff/**     │ ◀─── poll 10s ─── │   │ SaveChanges = claim rows + outbox rows    │
 └───────────────┘                   │   ▼            (one transaction)              │
                                     │ OutboxDispatcher ──▶ topic claims.claim-events│
                                     │                      (key = claimId)          │
                                     │   ┌──────────────────────┴─────────────┐      │
                                     │   ▼ group: claims-projector            ▼ group: claimant-notifier
                                     │ ClaimProjection read model       Notification rows
                                     │ (workload + exposure dashboards) (claimant updates feed)
                                     └──────────────────────────────────────────────┘
```

### Key decisions

**1. A modular monolith, not microservices.**
In a few hours, several services would mean mostly plumbing and very little domain. The seams are already drawn, though. The consumers talk to the claims module only through the event contract (`ClaimEventMessage`) and own their own tables. Splitting out **Reporting** (projector and dashboards) and **Notifications** later means moving a folder and pointing it at Kafka, with no rewrite. Claims would stay one service, because its invariants (status, assignee, reserve) must change atomically.

**2. What uses REST and what uses Kafka.**

| Interaction | Transport | Why |
|---|---|---|
| Submit a claim, pick up, request info, approve, settle, decline | **REST (sync)** | A person is waiting for "did that work?" and may be rejected by a business rule (409) or a concurrency conflict (409/412). That answer can't be fire-and-forget. |
| Lists, claim detail | **REST** | Read-your-own-writes on the transactional store |
| Workload and exposure dashboards | **Kafka → projection** | Nobody waits on them, so a few seconds of lag is acceptable. It also keeps reporting load off the transactional tables. |
| Claimant notifications | **Kafka → notifier** | A side effect that must not fail or slow the command. Email, SMS or push channels attach here. |

**3. Transactional outbox.** Each domain event is written to the `Outbox` table in the same `SaveChanges` as the state change. Publishing directly to Kafka inside the request can lose events (DB commits, publish fails) or emit phantom ones (publish succeeds, DB rolls back).

**4. At-least-once delivery, so consumers are idempotent.**
* Events carry the full claim **snapshot plus its version** (event-carried state transfer). The projector applies an event only if `version > LastVersion`, which makes it safe against duplicates and reordering.
* The notifier dedupes on `EventId` with a unique index.
* Messages are keyed by `ClaimId`, so all events for one claim share a partition and arrive in order.

**5. Concurrency.**
* `Claim.Version` is an EF concurrency token. If two handlers press "Pick up" at the same moment, exactly one wins and the other gets 409. This is covered by a test.
* Every staff command also sends `If-Match: "<version>"`. Acting on a stale screen returns 412 and the UI reloads the claim, instead of silently overwriting a colleague's change.

**6. Multi-market money.**
* A claim is booked in its market's currency, and amounts are never summed across currencies silently.
* The exposure dashboard shows local-currency totals as authoritative. A USD column uses *static, labelled indicative* rates for comparison only.

**7. Security boundaries live in the API, not the UI.**
* Role policies sit on the endpoint groups.
* Claimants get **404** (not 403) for other people's claims, so ids can't be probed.
* The **reserve** (the insurer's internal liability estimate) and internal events are stripped from claimant responses.
* The actor always comes from the token, never from the request body.

**8. Frontend structure.**
* One app, with `claimant/` and `staff/` as lazy feature areas behind `canMatch` role guards. A claimant never downloads the staff bundle.
* A shared `core/` holds auth, the interceptor and the API client.
* Async state is handled by **polling every 10–15s**. Claims change on a scale of minutes to hours, so polling is the simplest reliable choice. SSE fed by the same event stream is the upgrade path.

**9. The contract.** The backend owns it: DTOs are published as OpenAPI (`/openapi/v1.json`) and the UI's `api.models.ts` mirrors them. With more time I'd generate the TS client from the spec in CI so drift fails the build.

### Domain model

```
Claim (aggregate root)                    InformationRequest      ClaimHistoryEntry (audit, append-only)
  Reference  e.g. SG-261002-3D3F8D          Question / Response     Version, EventType, Actor, At, Detail
  Claimant, Market, Currency, Type          Open | Answered
  Status, Assignee
  ClaimedAmount (claimant estimate)
  ReserveAmount (insurer estimate = outstanding liability)
  SettlementAmount, DecisionReason, Version

Submitted ──pickup──▶ UnderAssessment ⇄ AwaitingInformation
                          │ approve              │
                          ▼                      │ decline (from either)
                       Approved ──settle──▶ Settled           Rejected
```

* Only the assignee can act on a claim.
* You can't approve while questions are open.
* Settling or declining releases the reserve to 0.
* Each transition bumps `Version`, appends history and raises an event.

---

## What I prioritised, and what I left out

**Built:** the full claim lifecycle end to end for both user types, plus the event-driven read side. I chose to make the parts the panel will probe solid: the state machine, concurrency, the outbox, idempotent consumers, access control and multi-currency. Breadth came second.

**Deliberately not built:**
* Document and photo upload. Claimants answer in text. Next step: blob storage plus pre-signed URLs, with a virus scan on upload.
* Policy lookup and coverage validation
* Fraud scoring
* Payments integration ("settle" records the decision only)
* Reassignment and team-lead workflows
* SLA timers
* Email and SMS delivery

### Shortcuts taken under time pressure

| Shortcut | Production answer |
|---|---|
| Demo login, symmetric JWT key in `appsettings.json` | OIDC (Entra ID for staff, B2C or similar for customers); signing keys in Key Vault |
| SQLite and `EnsureCreated` | SQL Server or Postgres with EF migrations; per-market data residency to be confirmed |
| Projector and notifier in the same process and DB | Separate deployables with their own stores, once team or scale needs it |
| Outbox polled every 500ms; single dispatcher | Row locking or `SKIP LOCKED` for multiple instances, or CDC (Debezium) |
| Poison messages are logged and skipped after 3 attempts | Dead-letter topic plus alerting and a replay tool |
| Dashboards aggregate in memory | SQL aggregation, or a store suited to analytics |
| Static FX rates | Daily rates feed; report in a configured base currency per region |
| Hand-written TS types | Generated from OpenAPI in CI |
| No frontend unit tests | Component tests for the action panel; Playwright E2E in CI. I ran an E2E pass manually while building: submit, pick up, request info, answer, approve, settle, notifications, dashboard, mobile and dark mode. |

### With more time, in priority order

1. **Observability:** OpenTelemetry traces across HTTP, outbox and consumers; outbox-lag and consumer-lag metrics; a correlation id on every event
2. **Real identity and authorisation:** handler-to-market scoping (an SG handler only sees SG claims), PII masking and audit
3. Generated API client and a contract test
4. Document upload, then SLA timers (e.g. "unassigned more than 2 days" alerts) driven off the same events
5. Push updates over SSE to replace polling
6. Split Reporting out as its own service on Kafka

One observation worth raising: a claim submitted *without* an estimate starts with a reserve of 0, so it is invisible in exposure until someone reviews it. Real systems usually apply an **initial average reserve** per claim type and market, which I'd add next.

## Repository layout

```
backend/src/Claims.Api      Domain/  Infrastructure/  Messaging/  Features/  Auth/  Program.cs
backend/tests/Claims.Tests  ClaimLifecycleTests (unit) · ApiTests (integration)
backend/claims.http         API walkthrough
frontend/src/app            core/ (auth, api)  shared/ (ui)  features/{login,claimant,staff}
docker-compose.yml          Kafka (KRaft) + topic init + Kafka UI
AI_JOURNAL.md               AI working log
```
