# Design — AIVES

A locked design system for the AI-powered Viva Exam System. Every page redesign
reads this file before emitting code.

## Genre
Modern-minimal, technical and austere. AIVES is an academic operations tool,
not a marketing site.

## Macrostructure family
- App pages: Workbench — task-first surfaces, evidence before decoration.
- Content pages: Catalogue — scan-friendly question and rubric inventories.
- AI interview pages: Map / Diagram — transcript, prompt and scoring connected spatially.

## Theme
- Paper: `oklch(98.5% 0.004 250)`
- Paper 2: `oklch(96% 0.007 250)`
- Ink: `oklch(24% 0.020 258)`
- Ink 2: `oklch(38% 0.018 257)`
- Rule: `oklch(88% 0.010 250)`
- Accent: `oklch(52% 0.190 256)`
- Focus: `oklch(64% 0.180 250)`

## Typography
- Display: Space Grotesk, weight 600, normal.
- Body: IBM Plex Sans, weight 400.
- Mono: JetBrains Mono, weight 500, for system state only.
- Display tracking: `-0.025em`.

## Spacing
4-point named scale from `--space-3xs` through `--space-3xl`. Pages use named
tokens and avoid raw spacing values.

## Motion
- Easings: `--ease-out`, `--ease-in`, `--ease-in-out`.
- Only transform and opacity animate.
- Reduced motion: static layout with at most 120 ms opacity feedback.

## Microinteractions stance
- Silent success; errors explain recovery.
- Focus indicators appear immediately.
- Hover is supplementary and always has keyboard/touch parity.

## CTA voice
- Primary: compact cobalt rectangle, 6 px radius, specific verb.
- Secondary: paper surface with visible rule, same height.

## Per-page allowances
- App pages use no decorative enrichment; function carries the page.
- AI state uses one dark graphite surface only when live interview context exists.

## What pages MUST share
The AIVES wordmark, cobalt signal accent, type pairing, tight radii, hairline
structure, button voice, focus ring and mobile navigation behaviour.

## What pages MAY differ on
Density, column ratios and whether the task is list-led, form-led or transcript-led.

## Exports
The canonical CSS export is [`tokens.css`](tokens.css). It maps directly to the
theme values above and is mirrored into the web project's static assets.
