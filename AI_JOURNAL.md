# AI working journal

A running log of how AI was used: what was asked, what was accepted, and what was challenged or overridden, with reasons. Not polished on purpose.

Tool: Claude Code (Claude Opus 5.5) in VS Code. The AI generated the bulk of the code. I direct, review and sign off.

---

## 1. Analysing the brief

**Asked:** "Analyse the doc file. This includes the assessment."

**AI produced:** a breakdown of the brief covering explicit requirements, the hidden ones (multi-currency, concurrent pickup, idempotent consumers, claimant data isolation, PII), a recommended scope, a time plan and likely panel questions.

**Accepted:**
- Modular monolith rather than microservices
- REST for commands, Kafka for side effects
- Outbox pattern
- State machine on the aggregate

**Noted:** the brief mentions "managers" but defines only two user types. The decision was to treat team workload and exposure as a staff-role view and to say so explicitly.

## 2. Building the assessment

**Asked:** "Please do the test according to the assessment."

### Environment discoveries (AI checked before choosing a stack)
- .NET 10 and Node 24.14 were installed. Docker, Postgres and Maven were not.
  → Chose **.NET over Spring**, because there was no Maven/Gradle to build Spring.
  → Chose **SQLite** and an **in-process transport behind an `IMessageTransport` seam** so the app runs with zero infrastructure. Real Kafka is supported through `docker-compose.yml`.
  → *Trade-off accepted:* the Kafka path compiles but was not run end-to-end here. The README says so plainly instead of claiming it works.
- The latest Angular CLI requires Node ≥ 24.15. **Used Angular 21** instead of asking for a Node upgrade. Angular itself was non-negotiable per the brief.

### Decisions the AI proposed and that were kept
| Decision | Why it survived review |
|---|---|
| Lifecycle rules live on the `Claim` aggregate; endpoints are thin | One place to test and defend; 9 unit tests cover it |
| Events carry a full snapshot plus version | Consumers never call back into the API; the projector is idempotent and tolerant of reordering through the version check |
| Reserve hidden from claimants; reserve-change events not shown to them | The reserve is an internal liability estimate, so showing it to the customer would be a business and legal issue |
| 404 rather than 403 for another claimant's claim | Doesn't leak which claim ids exist |
| `If-Match` / ETag on staff commands as well as the EF concurrency token | The token only catches *simultaneous* writes; `If-Match` also catches acting on a *stale screen* |
| Polling rather than WebSockets/SSE | Claims change over minutes to hours; polling is simplest and robust. SSE is listed as next step |
| Seeder drives real aggregate methods | Seed data goes through the outbox and consumers, so the demo exercises the real path |

### Things the AI got wrong or that review caught, and what changed
1. **Vulnerable transitive package.** The build warned that `Microsoft.OpenApi 2.0.0` had a high-severity advisory (NU1903). The AI's first fix bumped it to 3.x, which **broke the ASP.NET OpenAPI source generator**. Pinned to **2.12.2** (latest patched 2.x) instead. *Lesson: "latest" is not automatically right.*
2. **EF Core child-entity gotcha (prevented).** New `InformationRequest`s have client-generated Guid keys. EF would treat them as *Modified* rather than *Added* and the update would fail silently. Added `ValueGeneratedNever()` with a comment.
3. **Timezone bug.** SQLite returns `DateTimeKind.Unspecified`, so the API emitted timestamps without `Z`, and browsers in six time zones would have misread them as local time. Fixed at the source with a UTC value converter on all `DateTime` properties. The UI parser is also defensive.
4. **Notification order** was by insertion id, so seeded history came out in the wrong order. Changed to sort by event time.
5. **Polling and route reuse.** The first `poll()` read the claim id once. Navigating from one claim to another (same component, new `:id`) would keep polling the *old* claim. Reworked `poll` to restart whenever its input signal changes.
6. **The polling loop died on the first HTTP error.** Added per-tick `catchError` so a transient failure shows a message and the next tick retries.
7. **Kafka consumer resilience.** A non-fatal `KafkaException` (topic not created yet, broker blip) would have ended the consume loop and stopped the host. It now logs and retries.
8. **Dashboard "as of" time** used only open claims, so it lagged after a claim was closed. It now uses the last event applied.
9. **Dev server only listened on IPv6 `::1`**, which wasn't reachable in this environment. Bound to `127.0.0.1` and pointed the proxy at IPv4.
10. **Test-harness false alarms, kept honest.** The first browser probes reported "hangs". The causes were headless Edge in the sandbox and new tabs having no `sessionStorage` session, not bugs in the app. Re-verified in Chrome before drawing conclusions instead of "fixing" working code.

### Verification performed
- `dotnet test`: 16 passing (10 domain, 6 API integration)
- Manual API smoke test with curl:
  - concurrent pickup → one 200 and one 409
  - stale `If-Match` → 412
  - wrong role → 403
  - another claimant's claim → 404
  - bad market → 400 with field error
  - malformed JSON → 400
- Scripted browser E2E (Playwright and Chrome) covered the full claimant↔handler flow, notifications, the dashboard, dark mode and a 390px mobile width (no horizontal scroll), the role-guard redirect, and showed zero console errors.

### Second review pass
- **Bug: declining a claim with an open question.** The question stayed "Open", so the claimant saw "Action needed" on a declined claim and could still answer it. Now declining withdraws open questions (new `Withdrawn` status) and answers on closed claims are rejected. A test was added first.
- **Gap: only async `SaveChanges` wrote the outbox.** A synchronous save would have dropped events. Both paths now share one staging method.
- **Readability:** long doc comments cut to one-line "why" comments; single-letter names in non-trivial code renamed (`snapshot`, `registration`, `reporting`, `query`, `currentKey`); the Angular CLI boilerplate README replaced.

### Things I would challenge the AI on next
- The outbox dispatcher assumes a single instance. Fine for a demo; needs `SKIP LOCKED` or CDC for horizontal scale.
- Dashboard aggregation is in memory. Fine for hundreds of rows, wrong for production.
- No frontend unit tests were written; the E2E script lived outside the repo.

---

## 3. My review and overrides  ← *candidate: complete before submitting*

> The panel will probe anything I can't defend. Before I submit, I go through each file and record here, in my own words, what I challenged, rejected or changed myself.

| When | What I asked or noticed | What the AI did | What I decided and why |
|---|---|---|---|
| | | | |
| | | | |
