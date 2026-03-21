# GameForge V1 UX Copy Standards

## Purpose
Standardize plain-language copy for high-frequency actions so non-coders can confidently create, test, and publish games in a local-first workflow.

## Scope and Guardrails (V1)
- Audience: non-coders first, power users second.
- Product scope: single-player only, local-first desktop app.
- Platform context: Windows and Ubuntu.
- Technical stack context: C++ runtime, C# editor UI, Python AI orchestration (not exposed in user-facing copy unless user requests advanced details).
- Out of scope language: multiplayer, marketplace, first-party cloud hosting.

## 1) UX Copy Table (Buttons, Dialogs, Tooltips, Warnings)

### 1.1 Core Buttons and Primary Actions
| Area | User intent | UI label (plain language) | Avoid this wording |
|---|---|---|---|
| Project Home | Start new work | **Create New Game** | Initialize Project |
| Project Home | Continue previous work | **Open Game Project** | Load Workspace |
| AI Interview | Begin discovery flow | **Start Planning Chat** | Start Requirement Elicitation |
| Prototype | Build first playable | **Generate Playable Prototype** | Compile Scaffolding |
| Prototype | Rebuild part only | **Regenerate This Part** | Partial Regeneration |
| Editor | Protect current selection | **Lock Selection** | Freeze Node |
| Editor | Allow edits again | **Unlock Selection** | Unfreeze Node |
| Editor | Undo last change | **Undo** | Revert Mutation |
| Editor | Reapply undone change | **Redo** | Re-execute Mutation |
| Editor | Preview pending major edit | **Preview Changes** | Diff Preview |
| Testing | Run automated validation | **Run AI Playtest** | Execute Bot Harness |
| Testing | Flag manual testing | **Mark for Human Test** | Escalate to Manual QA |
| Publish | Evaluate readiness | **Check Publish Readiness** | Run Compliance Gate |
| Publish | Create local package | **Build Local Release** | Build Deployment Artifact |
| Publish | Continue despite warnings | **Continue with Warnings** | Override Non-Critical Compliance |

### 1.2 Dialog Titles and Body Copy
| Trigger | Dialog title | Body copy |
|---|---|---|
| Major AI edit request | **Review Major Changes** | "This update changes core gameplay or systems. Review the preview before applying." |
| Editing locked content | **Locked Content Protected** | "You selected content that is locked. Unlock it or pick another target to continue." |
| AI uncertain target | **Choose What to Edit** | "I need a clearer target. Select an object, region, or system, then try again." |
| AI failed 5 times in row | **AI Needs Backup Plan** | "I couldn't complete this request after 5 tries. You can switch to guided manual steps or try AI again." |
| Publish warnings present | **Warnings Found Before Publish** | "Your game can be published, but some issues may affect player experience." |
| Publish blocked by critical issues | **Publish Blocked** | "Critical issues must be fixed before publishing. Open the checklist to resolve blockers." |

### 1.3 Tooltip Copy Standards (Examples)
| Control | Tooltip |
|---|---|
| Generate Playable Prototype | "Builds a first playable version from your plan." |
| Lock Selection | "Prevents AI or manual edits to what you selected until unlocked." |
| Preview Changes | "Shows before/after changes before anything is saved." |
| Run AI Playtest | "Runs automated play sessions to find balance and progression issues." |
| Check Publish Readiness | "Scores your project and lists what must be fixed before publish." |

### 1.4 Warning Patterns with Explicit Impact
| Severity | Prefix | Warning copy template |
|---|---|---|
| Warning (non-blocking) | **Heads up:** | "Heads up: [issue]. If you continue, [likely impact]." |
| Critical (blocking) | **Action required:** | "Action required: [issue]. Publishing is blocked until this is fixed." |
| Data-risk destructive | **This will remove data:** | "This will remove data: [scope]. You can [undo availability or recovery path]." |

---

## 2) Confirmation Templates

### 2.1 Destructive Action Confirmations
Use this 4-line structure for all destructive confirmations:
1. **Action** (what the user asked)
2. **Impact summary** (exactly what will be removed/overwritten)
3. **Recovery path** (undo, backup, or irreversible)
4. **Decision buttons** (safe default first)

#### Template A — Delete Content
- **Title:** Delete [item name]?
- **Body:** "This will permanently remove [item name] from this project. This affects [systems/scenes/assets count]."
- **Recovery:** "You can undo this from history until the project is closed." (or "This cannot be undone." if true)
- **Buttons:** `Cancel` (default), `Delete`

#### Template B — Overwrite Generated Section
- **Title:** Replace current version?
- **Body:** "This will replace [section name] with a new generated version. Your current version will be removed."
- **Recovery:** "A checkpoint will be saved before replacement." (required when available)
- **Buttons:** `Keep Current`, `Replace`

#### Template C — Reset Settings/System
- **Title:** Reset [system] settings?
- **Body:** "This will reset [system] to defaults and remove custom changes in this area."
- **Recovery:** "You can restore from the latest checkpoint." (or explicit irreversibility)
- **Buttons:** `Cancel`, `Reset to Defaults`

### 2.2 Publish Warning Confirmations

#### Template D — Warning-Level Publish Continue
- **Title:** Continue with warnings?
- **Body:** "Your project has [N] warning(s): [top 1-2 warning summaries]. These may affect player experience but do not block publishing."
- **Decision:** "I understand the risks and want to continue."
- **Buttons:** `Go Back and Fix`, `Continue with Warnings`

#### Template E — Critical Blocker Publish Stop
- **Title:** Publish is blocked
- **Body:** "[N] critical issue(s) must be fixed before publishing: [top critical summary]."
- **Decision:** No override button in V1.
- **Buttons:** `Open Fix Checklist`

---

## 3) Technical-to-User Language Mapping

| Technical/internal term | User-facing term | Notes |
|---|---|---|
| Project | Game Project | Use "game" in primary navigation/actions |
| Runtime | Game Engine | Mention only in advanced/help contexts |
| Scene Graph | World Structure | Prefer "world" in standard UI |
| Entity / Actor | Object | "Object" is default non-technical term |
| Component | Behavior Part | Use in simple mode; show "Component" in advanced mode only |
| Serialize / Serialization | Save Project Data | Never expose "serialize" in primary flow |
| Build Artifact | Local Release Build | Keep "local" explicit for local-first promise |
| Validation Pipeline | Readiness Check | Keep action-oriented naming |
| Compliance Gate | Publish Check | Avoid legal jargon in primary UI |
| Manual QA | Human Test | Matches testing stage language |
| Bot Harness | AI Playtest | Keep "AI" explicit, avoid "harness" |
| Regenerate | Rebuild | For user-visible hints/tooltips, "rebuild" is friendlier |
| Diff | Before/After Preview | Plain-language explanation of change comparison |
| Rollback | Undo Changes | Keep familiar productivity terms |
| Locked Node/Region | Locked Content | Preserve concept, remove graph jargon |

## Copy Consistency Rules
1. Prefer verb-first labels: "Generate", "Check", "Lock", "Build", "Open".
2. Use one concept name consistently across stages (for example "Publish Readiness", not mix of "Compliance", "Validation", "Gate").
3. Every destructive/critical action must state user impact in one sentence.
4. Safe option goes first and is default in destructive dialogs.
5. Keep tooltips to one sentence and avoid implementation terms.

## Acceptance Checklist for This Copy Set
- [x] Core actions use clear verbs and non-technical wording.
- [x] Destructive/critical actions include explicit impact summaries.
- [x] Publish warnings distinguish non-blocking vs blocking issues.
- [x] Technical terms have user-facing alternatives for non-coder flows.
