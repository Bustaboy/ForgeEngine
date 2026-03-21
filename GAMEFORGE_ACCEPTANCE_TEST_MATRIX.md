# GameForge V1 Acceptance Test Matrix

This matrix turns V1 goals into concrete pass/fail checks.
Use it for sprint demos and release-candidate verification.

## Legend
- Priority: P0 (blocker), P1 (high), P2 (normal)
- Type: Functional, Performance, Reliability, Compliance, UX

| ID | Area | Priority | Type | Test | Pass Criteria |
|---|---|---:|---|---|---|
| AT-001 | Startup | P0 | Functional | Fresh install starts app | App launches with no crash |
| AT-002 | Project lifecycle | P0 | Functional | Create/save/reopen project | No data loss on reopen |
| AT-003 | Interview continuity | P0 | Functional | Resume long Q&A session | Session state restored correctly |
| AT-004 | 3-option behavior | P1 | Functional | Ambiguous input flow | Exactly 3 options shown and selectable |
| AT-005 | Think-of-something | P1 | Functional | Trigger autonomous concept mode | 3 coherent options produced + confirmation gate |
| AT-006 | Prototype generation | P0 | Functional | One-click prototype from design brief | Playable prototype launches |
| AT-007 | Lock protection | P0 | Reliability | Regenerate near locked content | No silent overwrite of locked content |
| AT-008 | Manual failover | P1 | UX | Repeat same failed request | Guided manual mode offered after 5 sequential failures |
| AT-009 | Undo/redo | P0 | Reliability | Multi-step edit rollback | All tested steps reversible |
| AT-010 | Windows smoke | P0 | Functional | Core flow on Windows | Passes end-to-end smoke run |
| AT-011 | Ubuntu smoke | P0 | Functional | Core flow on Ubuntu | Passes end-to-end smoke run |
| AT-012 | Vulkan validation | P1 | Functional | Render path startup | Vulkan-first path initializes correctly |
| AT-013 | Asset import | P0 | Functional | Import AI and manual assets | Assets indexed and searchable |
| AT-014 | License allow-list | P0 | Compliance | Import blocked licenses | Blocked with explicit reason |
| AT-015 | Attribution export | P1 | Compliance | Export project with CC-BY assets | Attribution bundle generated |
| AT-016 | Readiness gate | P0 | Compliance | Publish with critical issues | Publish action blocked |
| AT-017 | Warning override | P1 | Compliance | Publish with warning-level issues | Allowed after explicit acknowledgement |
| AT-018 | Audit trail | P1 | Compliance | Publish prep flow | Audit trail generated locally |
| AT-019 | Audit consent | P1 | Compliance | External audit submission | User consent required before upload |
| AT-020 | Crash-free target | P0 | Reliability | Core workflow sessions | >= 97% crash-free sessions |
| AT-021 | FPS target | P0 | Performance | Core gameplay validation scenes | 60 FPS target achieved |
| AT-022 | FPS critical floor | P0 | Performance | Core gameplay stress run | No sustained < 30 FPS |
| AT-023 | Frame-time p95 | P1 | Performance | Frame-time capture | p95 < 33ms |
| AT-024 | Initial load time | P1 | Performance | First scene load on target spec | < 20 seconds |
| AT-025 | Save/load integrity | P0 | Reliability | Regression loop on saves | 100% pass |
| AT-026 | Quest dead-end blockers | P0 | Functional | Quest progression checks | 0 critical blocker dead-ends |
| AT-027 | Git default behavior | P2 | UX | Create new project | Git is OFF by default |
| AT-028 | Git opt-in | P2 | UX | Enable Git action | Repository initialized successfully |
| AT-029 | Core flow visibility | P1 | UX | Navigate top-level app sections | Project Home -> Interview -> Prototype -> Editor -> Testing -> Publish is discoverable without docs |
| AT-030 | Simple vs advanced controls | P1 | UX | Edit selected object in editor | Simple controls visible by default, advanced controls accessible but collapsed |
| AT-031 | Plain-language action labels | P1 | UX | Review core action labels/dialogs | Labels and confirmations are understandable without coding knowledge |

## Release Candidate Rule
V1 release candidate can proceed only if:
- all P0 tests pass,
- no unresolved compliance blockers,
- and no open regression in lock/undo/save systems.

## Execution Notes
- Automate P0 tests first.
- Run cross-platform smoke tests every sprint once Milestone 1 is complete.
- Track pass/fail history per build for trend visibility.
