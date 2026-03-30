# ForgeEngine V1 Backlog & Roadmap

**Last updated:** March 30, 2026

## Current Status
ForgeEngine V1 is in **active pre-alpha**.  
We recently achieved two important milestones:
- The UI now runs without crashes
- The model downloader works reliably (huge pain point solved)

We also got our first visible rendering progress: an NPC spawning as a green square in the viewport.

**Major completed blocks:**
- Polish Round (save/load, validation)
- Graphics G1–G6 (Art Bible through texture loading & bindless)
- Narrative basics (Story Bible, tools, Narrator, voices)
- Weather + Living NPC core (schedules + controller)
- Secure Model Manager + stable onboarding

**Alpha Readiness Score:** ~86/100  
Biggest remaining gap: turning that green square into proper sprites and making the scene feel interactive.

## Definition of Done (Pre-Alpha)
- Small and focused
- Delivers visible or usable progress (something renders, downloads work, no crashes)
- Scene as primary source of truth (light migration OK)
- Includes build + validation steps
- Prioritizes "it works and doesn't crash" over perfect compatibility

## Core Principles
- Get something visible and interactive as fast as possible
- Scene is the main source of truth
- Local-first, no-code-first
- Lightweight mode + guardrails
- Modular assets + AI recombination
- User in control of AI decisions

## Immediate Next Steps (Turn Green Square into Real Progress)
| Priority | Task | Size | Notes |
|----------|------|------|-------|
| P0 | G7: AI-Generated Entity → Sprite Mapping | Small | Replace green square with actual sprites |
| P0 | Fix model installed-state detection parity | Tiny | Make asset-gen reliable |
| P1 | Basic NPC interaction test | Tiny | Build on the green square spawn |
| P1 | Low-resolution UX pass | Small | Smoother editor feel |

## Next Major Blocks (Visible Progress First)

### Graphics & Modular Content
- **G7**: AI-Generated Entity → Sprite Mapping ← **Do this next**
- **G8**: Modular Kit-Bashing System
- **G9**: Procedural Variations
- **G10**: Dynamic Loot Generation
- **G11**: Live Natural-Language Scene Editing
- **G12**: Hybrid 2D/3D + Mesh Loading

### Music / Audio Layer
- M1–M4 (Music Bible, tracks, mixer, UI)

### Living World & RPG Core
- Quest Runtime System
- Progression & Ability Framework
- Unified Fact / Effect System
- Settlement Layer
- Smart Scene Templates

### Final Stretch (Beta)
- Combat Polish
- NPC Deception / Emotional Appraisal
- Full 7-step AI Orchestration
- Runtime Efficiency (TurboQuant etc.)

## Pre-Alpha Exit Criteria
- G7 completed (generated assets appear as proper sprites on entities)
- Basic NPC spawn + simple interaction works
- Onboarding + asset flow is stable and usable
- Readiness score ≥ 92/100

## Backlog Ordering Guidelines
- Prioritize visible results ("can I see it and interact with it?")
- Keep PRs small
- Light backwards compatibility during pre-alpha
- Reuse existing systems
- Update this backlog after meaningful milestones

---

This version feels honest: it celebrates the stable UI + downloader and the green square as real wins, while keeping the focus on turning that green square into something useful.

Would you like me to:
- Prepare the **prompt for G7** right now (so we can get real sprites on entities), or
- Add anything else to this document first?

Just say the word. The green square is the beginning — let's make it do something cool next. 🚀

What's your move?
