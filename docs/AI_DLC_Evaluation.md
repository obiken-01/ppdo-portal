# AI-DLC Workflows — Evaluation

> **Doc class: Findings** (`docs/SPEC_STANDARD.md` §1). This records what is true about an external
> framework and what we would gain or lose by adopting it. It is **not** a spec, and nothing here is
> approved work. No code, config, or process changes were made as part of this evaluation.
>
> **Subject:** [`awslabs/aidlc-workflows`](https://github.com/awslabs/aidlc-workflows) v2.7.1
> (released 2026-09-01), MIT-0, ~4.4k stars, last push 2026-09-07.
> **Examined:** 2026-09-08, against a shallow clone of `main` — `README.md`, `docs/guide/`,
> and the full `dist/claude/` distribution tree.
> **Our side of the comparison:** repo state at `feature/v1.8.0-ppdo-54-aip-procurement-lines`.

---

## 0. Verdict up front

**Cherry-pick three ideas; do not install the framework.** Revisit the full-install question after
v1.8.0 ships, and only then as a bounded trial in a throwaway worktree.

The reasoning in one paragraph: AI-DLC's 33-stage lifecycle is mostly stages we already perform,
using documents we already wrote (`SPEC_STANDARD`, `TICKET_PROMPT_STANDARD`, `TEST_CONVENTIONS`,
`GIT_CONVENTIONS`, `Phase_Plan`, Linear). What it adds on top of ours is *enforcement* — hooks that
block, an audit trail, and an automated learning loop. Those are real gaps, but the useful ones can
be adopted as practice for a fraction of the cost of installing a 5.9 MB agent harness with 17
blocking hooks, an AWS Bedrock model pin, and a second competing `CLAUDE.md`. The one part genuinely
worth stealing — the learning loop — is a ritual, not a runtime, and we can run it by hand.

---

## 1. What AI-DLC actually is

Not a library, not a dependency — **a process harness you copy into your repository.** It turns the
agent session itself into a state machine with gates.

| Concept | Detail |
|---|---|
| **Lifecycle** | 33 stages across 5 phases: Initialization (0.1–0.3), Ideation (1.1–1.7), Inception (2.1–2.9), Construction (3.1–3.7), Operation (4.1–4.7) |
| **Agents** | 14 — 11 domain experts (product, product-lead, architect, design, developer, quality, devsecops, delivery, operations, compliance, aws-platform), 2 review-only, 1 adaptive composer |
| **Scopes** | 11 presets choosing how many stages run: `poc` (8), `bugfix` (9), `refactor` (10), `security-patch` (10), `express` (10), `infra` (13), `mvp` (23), `classic` (26, the shipped default), `workshop` (26), `feature` (33), `enterprise` (33) |
| **Depth / test strategy** | Minimal / Standard / Comprehensive, set independently of scope |
| **Gates** | A human approval gate per stage — `feature` scope runs 29 of them |
| **Invocation** | `/aidlc <plain-English description>`; scope auto-detected unless forced |
| **Artifacts** | Every stage writes Markdown into `aidlc/spaces/<space>/intents/<YYMMDD>-<label>/`, committed to git |
| **Runtime** | 17 TypeScript hooks + 69 CLI tools, all run through **bun** |

### The three mechanisms that make it more than a prompt pack

1. **Blocking hooks.** `settings.json` wires hooks into `PreToolUse`, `PostToolUse`,
   `UserPromptSubmit`, `SessionStart`, `SessionEnd`, `PreCompact`, `SubagentStop`, and `Stop`.
   `aidlc-state-transition-guard`, `aidlc-plan-approval-guard`, `aidlc-review-freeze`, and
   `aidlc-reviewer-scope` sit in front of `Edit`/`Write`/`Bash` and *refuse the call* when the
   workflow is not in an approved state. The gates are machinery, not etiquette.
2. **The learning loop.** Each stage keeps a `memory.md` diary under four fixed headings —
   Interpretations, Deviations, Tradeoffs, Open questions. At the approval gate the framework
   surfaces every diary line verbatim as a keep/discard candidate and asks "anything to add for next
   time?". Kept items are written into `aidlc/spaces/<space>/memory/project.md` (or promoted to
   `team.md`) and re-read as rules on the next run. Every write leaves a `RULE_LEARNED` audit row.
3. **Sensors.** Six deterministic checks (`linter`, `type-check`, `required-sections`,
   `traceability`, `claim-sources`, `upstream-coverage`) that fire on stage output as a second
   opinion independent of the model. Each is a small manifest — id, command, glob, severity,
   timeout — bound to the stages whose output it should police.

### What it installs into the repo

| Path | Size / count |
|---|---|
| `.claude/CLAUDE.md` | 19 KB (a second project constitution) |
| `.claude/settings.json` | 9 KB — Bedrock env, model `opus[1m]`, `effortLevel: xhigh`, statusline, all hooks |
| `.claude/agents/` | 14 files |
| `.claude/skills/` | 43 skill dirs (one per stage, plus scope shortcuts) |
| `.claude/tools/` | 69 files |
| `.claude/hooks/` | 18 files |
| `.claude/knowledge/` | 59 files (per-agent knowledge bases) |
| `.claude/aidlc-common/` | 42 files |
| `.claude/scopes/` · `rules/` · `sensors/` | 11 · 1 · 6 files |
| `aidlc/` | 49 KB workspace shell — required; `/aidlc --doctor` fails without it |
| **Total** | **~5.9 MB, ~260 files** |

---

## 2. What we already have

This is the bulk of the comparison. Most AI-DLC stages map onto an asset already in this repo.

| AI-DLC stage | Our equivalent | Coverage |
|---|---|---|
| 0.1–0.3 Initialization | n/a — no workspace state machine | — |
| 1.1 Intent Capture | `CLAUDE.md` § "Eliciting feature details" — batch questions, propose a default | ✅ Strong |
| 1.2 Market Research | n/a — single internal client (PPDO) | Not applicable |
| 1.3–1.4 Feasibility, Scope Definition | `docs/SPEC_STANDARD.md` §2.1 Goal, §2.2 Decisions, §non-goals | ✅ Strong |
| 1.5 Team Formation | n/a — solo developer | Not applicable |
| 1.6 / 2.5 Mockups | `docs/DESIGN_SYSTEM.md`, `PPDO_PROJECT_CONTEXT.md` (Penpot frames), `docs/v1.8/AIP_Form_Spec.md` | ✅ Strong |
| 2.1 Reverse Engineering / CodeKB | `PROJECT_DOCUMENTATION_NET_AZURE.md`, `CLAUDE.md` architecture rules | ⚠️ Partial — prose, not a refreshable per-repo store |
| 2.2 Practices Discovery | `CLAUDE.md` (808 lines) + `NAMING_CONVENTIONS`, `DESIGN_SYSTEM`, `PERFORMANCE_GUIDELINES`, `GIT_CONVENTIONS` | ✅ Strong — richer than discovery would infer |
| 2.3 Requirements Analysis | `docs/SPEC_STANDARD.md` + `docs/vX.Y/*_Requirements.md` | ✅ Strong |
| 2.4 User Stories | `SPEC_STANDARD` §2.3 given/when/then, incl. per-role rows | ✅ Strong |
| 2.6 Domain Design | Clean Architecture layering + the delivery order in `CLAUDE.md` | ✅ Strong |
| 2.7 Units Generation | `docs/v1.8/Phase_Plan.md` — 7 phases / 73 items | ✅ Strong |
| 2.8 Contract Design | `docs/External_AIP_API_Contract.md`; `SPEC_STANDARD` §2.4 API contract + error shapes | ✅ Strong |
| 2.9 Delivery Planning | `Phase_Plan.md` + the Linear PPDO-28 epic | ✅ Strong |
| 3.1 Functional Design | `SPEC_STANDARD` §2.3–2.5 | ✅ Strong |
| 3.2–3.3 NFR Requirements & Design | `docs/PERFORMANCE_GUIDELINES.md`, `docs/v1.8/Permission_Matrix.md`, `USER_GUIDE_Account_Security.md` | ✅ Strong |
| 3.4 Infrastructure Design | `PROJECT_DOCUMENTATION_NET_AZURE.md`, `CLAUDE.md` Production Deployment table | ✅ Strong |
| 3.5 Code Generation | `docs/TICKET_PROMPT_STANDARD.md` — numbered steps with per-step verification | ✅ Strong |
| 3.6 Build and Test | `docs/TEST_CONVENTIONS.md`, `dotnet test`, the 1,061-test suite | ✅ Strong |
| 3.7 CI Pipeline | `.github/workflows/ci.yml` — backend build+test, frontend build+lint on every PR | ✅ Strong |
| 4.1–4.3 Deployment | `.github/workflows/deploy.yml`, `docs/v1.8/Pre_Deployment_Checklist.md`, `GIT_CONVENTIONS.md` | ✅ Strong |
| 4.4 Observability | Application Insights + `GET /api/health` | ⚠️ Partial — wired up; no dashboards/alarms/SLO doc |
| 4.5 Incident Response | n/a | ❌ Gap (low priority at this scale) |
| 4.6 Performance Validation | `docs/Performance_Audit_2026-07-16.md` — done once, not a standing gate | ⚠️ Partial |
| 4.7 Feedback & Optimization | `docs/v1.8/RETROSPECTIVE.md` | ✅ Strong |

**Additionally, AI-DLC's own `memory/project.md` template turns out to be a subset of our
`CLAUDE.md`.** Its headings are: Way of Working, Walking Skeleton, Testing Posture, Deployment, Code
Style, Tech Stack, Decided, Scope Overrides, Forbidden. We have every one of those, in more detail
and with the reasoning attached — including a literal "What NOT to Do" list that is exactly
`## Forbidden`.

---

## 3. What we don't have

The honest gap list. These are the only reasons to consider adopting anything.

| Gap | What AI-DLC does | How much it hurts us today |
|---|---|---|
| **Enforcement** | Hooks physically refuse `Edit`/`Write`/`Bash` outside an approved state | **Medium.** Our gates are prose that the agent follows. They mostly hold, but nothing stops a session coding ahead of an unwritten spec. |
| **Automated learning loop** | Each stage diaries its interpretations/deviations/tradeoffs; the gate turns kept lines into durable rules | **High — the real gap.** Our equivalent is "remember to edit `CLAUDE.md`". Its own Implementation Status section admits it *"drifted eleven weeks and six minor versions behind the code"*. That is exactly the failure mode the loop prevents. |
| **Decision audit trail** | 91-event audit log committed as shards; every rule write and gate approval leaves a row | **Low–Medium.** Ours lives in Linear comments and commit messages — fine solo, thin if anyone else joins. |
| **Deterministic doc sensors** | `required-sections` and `traceability` sensors check produced Markdown against a schema | **Medium.** Nothing today verifies that a `*_Requirements.md` actually contains all of `SPEC_STANDARD` §2. Compliance is by eye. |
| **Written brownfield safeguards** | A named matrix: blast-radius analysis → diff preview → test baseline *before* → test validation *after* → rollback plan | **Medium–High.** We do most of this instinctively, but it is nowhere written as a required step — and the v1.4 and v1.7 patch trains are what happens when it is skipped. |
| **Structured scope/depth ladder** | One command picks 9 vs 26 vs 33 stages of ceremony | **Low.** We do this by judgment, and it works. |

---

## 4. What we have that AI-DLC does not

Adopting wholesale is not additive — these would have to be preserved by hand, or lost.

- **Azure.** AI-DLC ships an `aws-platform` agent, AWS MCP servers, and AWS-shaped infrastructure
  stages. Our entire deployment story is Azure SWA + Functions + SQL, with hard-won rules
  (colocate in Southeast Asia; CI does not run EF migrations; CORS is portal-configured, not
  `host.json`). None of that survives a generic infrastructure stage.
- **Our domain.** PR No. formats, Manila-time refs, FIFO batch allocation, the DBM BOM reference
  code layout, the permission matrix. Irreplaceable and entirely ours.
- **Linear.** Ticket prompts, the `PPDO-*`/`RAL-*` prefix history, the milestone structure. AI-DLC
  has its own intent/unit vocabulary that would sit alongside Linear rather than replace it.
- **`docs/LEARNING_TICKET_BANK.md` — the direct conflict.** Ralph is deliberately rebuilding
  hands-on coding fluency, and `CLAUDE.md` codifies a guidance ladder that *withholds* paste-ready
  code. AI-DLC's premise is the opposite: agents drive the lifecycle and the human approves gates.
  Full adoption pushes against a standing project goal. This is not a technical objection, and it is
  the strongest single argument against wholesale adoption.

---

## 5. Costs and collisions specific to this repo

Concrete, verified against the shipped `dist/claude/` tree:

1. **`.claude/settings.json` is overwritten.** Ours carries a curated allowlist
   (`git commit`, `git push`, `dotnet build`, `dotnet test`, …). Theirs allows bare **`Bash`**,
   `Edit`, and `Write` unconditionally. That is a permissions regression, not an upgrade.
2. **Two constitutions.** Their `.claude/CLAUDE.md` (19 KB) loads alongside our root `CLAUDE.md`
   (808 lines). Ours opens with *"If anything in this file conflicts with the documentation, this
   file takes precedence"*; theirs asserts the orchestrator gates everything. Unresolvable as shipped.
3. **AWS Bedrock by default.** `CLAUDE_CODE_USE_BEDROCK=1`, `AWS_REGION=us-east-1`, and pinned
   `global.anthropic.*` model IDs. We have no AWS account for this project. Removable — but it is
   the tested baseline, so running off it is unsupported territory.
4. **Token cost.** `model: opus[1m]` with `effortLevel: xhigh`, 14 agents, and up to 33 stages per
   feature. Materially more spend per ticket than how we work now.
5. **`bun` is not installed** on this machine, and would become a hard dependency of every Claude
   Code session in this repo — all 17 hooks are bun scripts.
6. **Hook-approval friction.** The 2.7.0 notes require Claude Code users to approve project hooks
   via `/hooks` and fully restart. `/hooks` is a terminal-dialog command and is **not available in
   the Claude desktop app's Code tab**, which is where this project is worked on. Installing would
   mean dropping to the `claude` CLI.
7. **`.mcp.json`** must be merged, not copied — we already run Figma, Linear, Gmail, Calendar, and
   Drive servers.
8. **Release churn.** 2.6.124 → 2.7.0 → 2.7.1 inside four days, each with breaking upgrade notes
   ("scripts that consume the retired design artifact names must migrate"). The README itself says
   to pin a known-good version. That is a fast-moving dependency to place at the centre of a process
   that currently has no such dependency.

---

## 6. Recommendation

### Do not adopt the whole repo — now or, on current evidence, later

Not because it is bad. It is well built, and the enforcement machinery is genuinely more rigorous
than ours. The case against is fit:

- We would pay full ceremony to obtain three mechanisms, while re-deriving twenty stages of
  documentation we already wrote and tuned to this domain.
- It is AWS-shaped and we are Azure-shaped.
- It optimizes for agent autonomy; the project has a standing goal of the opposite (§4).
- The 2.7.x cadence would make our development process depend on someone else's release train.

### Definitely do not touch it during v1.8.0

`docs/v1.8/RETROSPECTIVE.md` already names a change of this size as what produced the v1.4 and v1.7
patch trains. v1.8.0 is the largest change attempted, is mid-flight at PPDO-54, and ships a migration
that rewrites existing AIP amounts. A methodology swap would be a second uncontrolled variable —
exactly the mistake the retrospective was written to prevent.

### Cherry-pick, ranked by value per unit of effort

| # | What to steal | Where it lands | Effort | Why |
|---|---|---|---|---|
| 1 | **The learning-loop ritual.** After each ticket, before the PR: list the session's interpretations, deviations, and tradeoffs; keep the durable ones as `CLAUDE.md` / `docs/` edits in that same commit. | `docs/TICKET_PROMPT_STANDARD.md` sign-off section | Low | Directly targets the documented 11-week `CLAUDE.md` drift. Highest-value item here. |
| 2 | **The brownfield safeguard matrix** — blast radius → diff preview → test baseline before → test validation after → rollback plan. | `docs/TICKET_PROMPT_STANDARD.md` §6, as required step flags | Low | Names what the v1.4/v1.7 patch trains skipped. Fits the existing `⚠️ MIGRATION` flag pattern. |
| 3 | **A `required-sections` sensor.** A small script checking that every `docs/vX.Y/*_Requirements.md` carries the `SPEC_STANDARD` §2 headings, wired into `ci.yml`. | `scripts/` + `.github/workflows/ci.yml` | Medium | Makes an existing standard machine-checked instead of eye-checked. Good `LEARNING_TICKET_BANK` candidate — small blast radius, testable, reversible. |
| 4 | **The scope ladder as vocabulary** — a named fast path for bugfix/refactor tickets, so small work does not carry full spec ceremony. | `docs/TICKET_PROMPT_STANDARD.md` | Low | We already do this implicitly; naming it makes the choice deliberate. |
| 5 | **The `Decided` convention** — dated decisions that must not be re-asked. | `SPEC_STANDARD` §2.2, adding the date + stage stamp | Low | Cheap improvement to a section we already have. |

Items 1, 2, 4, and 5 are documentation edits to files we own, carry no dependency, and could ship in
a single `docs:` commit whenever there is a quiet moment after v1.8.0.

### If we ever do want the real thing — trial protocol

Only after v1.8.0 is merged to `main`, and only as:

1. A throwaway **git worktree** (we already use `.claude/worktrees/`) — never `main`, never `release/*`.
2. Back up `.claude/settings.json` first; restore our permission allowlist afterwards.
3. Strip the Bedrock `env` block and the `model` / `effortLevel` pins before the first run.
4. Install `bun`; run from the `claude` CLI, not the desktop Code tab (hook approval).
5. Pin the version explicitly, and record which tag was trialled.
6. Run **one** techdebt ticket at `--scope bugfix` (9 stages). Not a feature. Not an AIP ticket.
7. Judge on two questions only: *did a gate catch something our process would have missed?* and
   *did the learning loop write a rule worth keeping?* If both are no, the question is settled.

---

## 7. What would change this verdict

- **A second developer joins.** The audit trail and enforced gates get much more valuable the moment
  more than one person — or agent — is changing the repo.
- **The learning-loop cherry-pick fails in practice.** If the manual ritual does not stick over a
  couple of releases, that is evidence the mechanism needs to be machinery rather than discipline.
- **AI-DLC stabilizes and grows an Azure path.** A slower release cadence plus a non-AWS platform
  agent would remove two of the four objections.
- **We start a genuinely greenfield project.** Nearly every objection here is a brownfield-fit
  objection. On a new repo with no `CLAUDE.md` and no docs, `mvp` scope would be a real contender.

---

## 8. Sources

- Repository: <https://github.com/awslabs/aidlc-workflows> — `main` @ v2.7.1, examined 2026-09-08
- `README.md` (install matrix, Claude Code section); `CHANGELOG.md` (2.7.0 / 2.7.1 upgrade notes)
- `docs/guide/01-getting-started.md` (prerequisites, Bedrock setup), `05-scopes-and-depth.md`
  (scope/stage matrix), `09-rules-and-the-learning-loop.md`, `14-artifacts-reference.md`
- `dist/claude/.claude/settings.json`; `.claude/knowledge/aidlc-shared/brownfield.md`;
  `.claude/sensors/*.md`; `dist/claude/aidlc/spaces/default/memory/project.md`
- Method background: [AI-DLC blog post](https://aws.amazon.com/blogs/devops/ai-driven-development-life-cycle/)

---

*docs/AI_DLC_Evaluation.md — evaluation only, no changes made — 2026-09-08 — Ralph Armand Alcaide*
