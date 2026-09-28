# Admin Design Guide

The public `help-motivate-me` client application is the visual source of truth. The private SaaS control plane should feel like an operational area of the same product, not a separate brand. Do not change the public application to accommodate the admin site; bring changes from the client into this repository deliberately.

## Source Of Truth

When visual guidance conflicts, follow these public files in order:

1. `frontend/tailwind.config.js` for color, font, and breakpoint tokens.
2. `frontend/src/app.css` for controls, cards, focus states, and base typography.
3. `frontend/src/lib/components/layout/TopNav.svelte` for the header and mascot treatment.
4. Existing public product screens for spacing, density, and interaction patterns.

The SaaS pages currently share one server-rendered shell in `DashboardEndpoints.Page()`. Keep common styling there until the control plane adopts a dedicated frontend build.

## Brand Foundation

Use Nunito as the primary typeface, with Inter and system sans-serif as fallbacks. Typography should be friendly but practical: bold Nunito headings, readable body copy, and compact labels. Do not introduce serif display fonts or unrelated type systems.

Use the client palette directly:

| Role | Token | Value |
| --- | --- | --- |
| Primary action | `primary-500` | `#d4944c` |
| Primary hover | `primary-600` | `#b87a3a` |
| Primary dark | `primary-700` | `#965e2c` |
| Subtle accent | `primary-100` | `#fdf6ed` |
| Border | `primary-200` | `#f9e8d2` |
| Page background | `warm-cream` | `#faf7f2` |
| Surface | `warm-paper` | `#fdfcfa` |
| Secondary surface | `warm-beige` | `#f5f0e8` |
| Heading | `cocoa-900` | `#4a3830` |
| Body | `cocoa-800` | `#5f483d` |
| Secondary text | `cocoa-600` | `#8a6a54` |
| Muted text | `cocoa-500` | `#9a7d64` |
| Positive status | `sage-500` | `#768862` |
| Destructive action | `red-600` | `#dc2626` |

Do not substitute a green, blue, gray, or corporate admin theme for these tokens. Sage is a semantic accent, not the primary action color.

## Mascot And Header

Use `wwwroot/appbarsloth.png`, copied from the public app's canonical asset, in the shared header. The mascot overlaps the lower edge of the header in the same way as the client top navigation. Keep its dimensions stable so loading and responsive layout do not shift.

The brand text is `HelpMotivateMe`, matching the client header. Match `TopNav.svelte` dimensions exactly: a 56px desktop bar, 96px mascot box, 18px brand text, 12px gap, and 16px image bottom offset. Below the client breakpoint, use a 48px bar, 80px mascot box, 16px brand text, 8px gap, and 14px image bottom offset. The header uses the warm paper-to-beige background, caramel-tinted shadow, and rounded lower corners from the client. Navigation links remain restrained and operational.

When the public mascot changes, copy the canonical asset into this repository and review both desktop and mobile framing. Do not hotlink the public GitHub repository at runtime.

## Components

Buttons use a minimum 44px touch target, 16px radius, bold text, and visible keyboard focus. Primary buttons are caramel; secondary buttons use a paper surface with a warm border; destructive actions are red. Use text labels for explicit commands and add familiar icons only when an icon library is introduced consistently.

Inputs and selects use paper backgrounds, warm borders, 16px radius, and a caramel focus ring. Labels use normal title casing and cocoa text; avoid all-caps form labels.

Cards and operational panels use warm paper, a subtle warm border, restrained shadow, and 20-24px radius to match the established client design. Do not nest decorative cards. Metrics may be separate repeated cards because they are comparable data items.

Tables use a warm beige header, paper rows, warm separators, and a subtle primary tint on hover. Status values should be compact pills with a dot; use sage for healthy/running states and neutral cocoa tones for inactive states.

## Layout And Responsive Behavior

Use a maximum content width around 1120px with 16px desktop gutters and 12px mobile gutters. Admin screens should prioritize scanning and repeated actions over marketing composition.

On narrow screens:

- stack forms, plan choices, metrics, and toolbar actions;
- preserve horizontal table scrolling rather than crushing columns;
- keep the brand, mascot, and navigation on one stable header row;
- reduce heading size explicitly at the breakpoint rather than scaling type with viewport width;
- verify text does not overlap the mascot, controls, or adjacent content.

## Content And Interaction

Use the same calm, direct voice as the client. Explain consequences near destructive, backup, restore, and restart actions. Keep operational state visible and avoid decorative copy inside dense admin surfaces.

Every interactive element needs a visible focus state. Icon-only actions require an accessible name. Mascot images used decoratively should have an empty `alt`; meaningful illustrations require descriptive alternative text.

## Change Checklist

Before merging an admin UI change:

1. Compare tokens and component behavior with the current public client.
2. Keep all implementation changes in `help-motivate-me-saas` unless the client itself needs a product change.
3. Build and run the control plane with the local Compose override.
4. Check signup, admin, workspace settings, wake, not-found, loading, empty, and error states.
5. Capture desktop and mobile screenshots, including a populated wide table.
6. Verify mascot loading, keyboard focus, horizontal overflow, touch targets, and text wrapping.
7. Run `dotnet test HelpMotivateMe.SaaS.slnx` and build the SaaS image.

If the public app changes its visual system, update this guide and the shared SaaS shell in the same private pull request. Treat that as synchronization work, not permission to modify the public app's established styling.