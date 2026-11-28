# mise工具箱 · desktop design

## Direction

A macOS-grade Windows workbench: one integrated 64 px toolbar carries the app identity, page title, terminal action and custom window controls (WindowChrome, no native title bar), above a light navigation rail and a calm gray canvas. Materials — not borders — create hierarchy: white cards float on #F1F4F8 with a hairline and a soft shadow, inputs are filled fields that turn white and ringed when focused. The design uses WPF resource dictionaries and shared UI primitives in the single executable.

## Primary workflows

- First installation: show mise path, tools path and proxy mode/address; one primary action is Install mise.
- Existing files on a new device: show concise terminal registration status; one primary action is Restore environment.
- Ready environment: one primary action updates mise and tools. Paths and installation details stay in Settings.
- The 64 px command band aligns the app identity, current page, terminal action and window controls (minimize, maximize/restore, close). The 200 px rail separates the three main views (Environment, Tools, Settings) from Updates, with the selected item filled in action blue and white content, matching macOS Settings. The footer reports operation state and offers cancellation where applicable.
- Tools starts with the install queue and keeps installed versions below. Enter queues tools, and the primary action submits a snapshot. The input stays usable during installation. The compact graphite status strip displays one state line and up to one recent-output line. Its terminal icon opens the complete log. Maintenance and export remain secondary.

## Tokens

- Canvas #F1F4F8, rail #F7F9FC, surface #FFFFFF, ink #172131, muted #535F70, hairline #E7ECF3, filled field #EFF2F7 (hover #E7ECF3).
- Action blue #2458C1, strong text accent #17438F, selected fill tint #EAF0FD. Success #2E8B5F marks ready states, danger #B92F39 marks invalid states. Graphite console #192230 with mint state text #A8E4CB and neutral output text #E3EBF5.
- Typography: Segoe UI Variable Display with Segoe UI and Microsoft YaHei UI fallbacks; page title 22, sections 16, body 13, metadata 11–12. No custom font dependency.
- Page edges 28 px, section gap 16 px, control gap 8 px. Radius scale: cards 12, controls and inputs 8, chips 14, popovers 10.
- Inputs and action buttons 40 px high. Form label column 112 px, right controls stretch, browse/action column 88–104 px. Rows vertically center labels and controls.
- Table rows are inset 2 px with 8 px rounded hover/selection veils and a hairline separator; columns share left/right content boundaries and 16 px cell insets. Paths truncate with full-value tooltips.

## Motion

One motion language lives in Controls/Motion and the shared dictionaries, so every surface moves the same way. Hover feedback is a dark veil at 4–5% opacity fading in 100 ms and out 140 ms; keyboard focus is a 1.5 px accent ring outside the frame, so focus never shifts layout. Pressing scales the button frame to 97% and springs back. State changes that persist (switches, selected nav) stay instant to avoid replaying motion when a page reloads. Navigation cross-fades and eases the page body up 10 px over ~230 ms; the same rise-in greets queue chips and dialog windows on first load. The status card dot turns success green with a small pulse when ready and breathes while working. Popovers, tooltips and combo dropdowns carry a soft 18 px shadow for elevation. Inside templates every resource reference is DynamicResource — deferred StaticResource lookups at template load are unreliable and must not be reintroduced.

## Icons

The language mirror picker is shared by the Tools command bar and the queue tag editor. One ecosystem and one provider are selected at a time. The dialog previews affected values and keeps the scope, apply action, and generated-file instructions visible without adding rows to the main workspace.

Lucide official SVG sources are retained in Assets/Icons with the upstream license. WPF Geometry resources render these as 24×24 strokes using round caps and joins; all views use this one icon family — navigation, section headers, status cards, buttons and window controls included. No SVG runtime DLL or separate control assembly.

## Controls and behavior

PathPicker and StatusCard remain templatable controls in the main executable. Template parts tolerate missing/wrong types, old events are removed when the template changes, and visual states are declared in Generic.xaml at each template root. StatusCard accepts an optional icon geometry beside its caption.

The borderless window keeps native resize, snap and drag through WindowChrome; only the terminal action and window buttons are hit-test visible inside the caption. Maximizing insets the root grid so the resize frame never clips content. Closing still routes through the existing close-choice flow.

Low-frequency path, proxy, language, automation and tray options are grouped in Settings. Operation settings are disabled while work runs. Logs, cancellation and navigation stay accessible. Queue item icons carry localized accessible names and a separate accessible hint identifying the affected tool. The primary installation action and inline status stay in the same card.

## Verification

Capture first-install, restore-needed and ready states, all auxiliary pages and the minimum 1000×700 window from the current build. Check Chinese and English text, icon consistency, row baselines, keyboard focus rings, and the single- and double-line status states. Hover, focus and page transitions must not shift layout; keyboard focus stays visible on every interactive element. Window controls must survive maximize/restore with no clipped content, and the close button still opens the close-choice dialog.
