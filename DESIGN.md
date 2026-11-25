# Portable mise desktop

## Direction

Compact Windows utility with restrained Apple-inspired styling. No slogans, hero banners, English marketing captions, decorative statistics, emoji, or imitation macOS window buttons. The official mise favicon remains the application identity.

## Primary workflows

- First installation: show mise path, tools path and proxy mode/address; one primary action is Install mise.
- Existing files on a new device: show concise terminal registration status; one primary action is Restore environment.
- Ready environment: one primary action updates mise and tools. Paths and installation details stay in Settings.
- Three main navigation items: Environment, Tools, Settings. Logs and version/update operations are secondary pages reached from utility actions.

## Tokens

- Sidebar and canvas: #F5F5F7; surface: #FFFFFF; ink: #1D1D1F; muted: #6E6E73; line: #E5E5EA.
- Single action/selection color: #007AFF; selected tint: #EAF3FF.
- Typography: Segoe UI with Microsoft YaHei UI fallback; headings 20–24, sections 15–16, body 13, metadata 11–12. No custom font dependency.
- Spacing follows 8 px increments. Page inset 24, section gap 16, control gap 8. Cards radius 10–12.
- Inputs and action buttons 40 px high. Form label column 112 px, right controls stretch, browse/action column 88–104 px. Rows vertically center labels and controls.
- Table columns share left/right content boundaries and 16 px cell insets. Paths truncate with full-value tooltips.

## Icons

Lucide official SVG sources are retained in Assets/Icons with the upstream license. WPF Geometry resources render these as 24×24 strokes using round caps and joins; all views use this one icon family. No SVG runtime DLL or separate control assembly.

## Controls and behavior

PathPicker and StatusCard remain templatable controls in the main executable. Template parts tolerate missing/wrong types, old events are removed when the template changes, and visual states are declared in Generic.xaml at each template root.

Low-frequency path/proxy/automation/tray options are grouped in Settings. Operation settings are disabled while work runs; logs, cancellation and navigation stay accessible. Descriptive instructions belong in tooltips or README, except concise state/error text needed to choose an action.

## Verification

Capture first-install, restore-needed and ready states, all auxiliary pages and the minimum 1000×700 window from the current build. Check Chinese text, icon consistency, row baselines and all control edges; no feature may be removed to hide a layout defect.
