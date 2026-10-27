# OctaGrid.Client — Typography Guidelines

Tokens live in `Styles/Typography.axaml`; text style classes live in `Styles/TypographyStyles.axaml`. This is the companion to [SPACING.md](SPACING.md) and follows the same structure: primitives → roles → classes.

## 1. Principles

1. **One family, two weights.** The whole app is Inter (registered by `.WithInterFont()` in `Program.cs`). Only two weights are in use: *Regular* for text and *SemiBold* for emphasis. Size and colour carry the rest of the hierarchy. Fewer weights means a calmer screen and fewer font files to load.
2. **Views pick a role, not a number.** `Classes="subtitle"` says what the text *is*. `FontSize="18"` doesn't. Never put `FontSize`, `FontWeight` or `FontFamily` in a view. If no role fits, that means the role list is missing something, so add one.
3. **Line heights sit on the 4px grid.** Every role has a line height that's a multiple of 4 (16, 20, 24, 28, 32, 36). Stacked text rows then land on the same rhythm as the spacing tokens.
4. **Hierarchy comes from size + weight + colour, in that order.** Use a bigger role first. Use `secondary` (muted colour) for supporting text. Reach for SemiBold only through a role that already defines it.
5. **One role class per `TextBlock`.** Modifiers (`secondary`, `mono`) are added on top. Never combine two roles.
6. **One `TextBlock` per line of meaning.** Don't build mixed-size text with `<Run FontSize=…>` and `<LineBreak/>`. Stack separate `TextBlock`s in a `StackPanel` and let the panel own the gap (see SPACING.md, principle 3). Runs are only for inline pieces that share a role, such as a bullet followed by a state name.

## 2. Primitive scale

| Token | Size | Line height | Token |
|---|---|---|---|
| Caption | 12 | 16 | `Font.Size.Caption` / `Font.LineHeight.Caption` |
| Small | 13 | 20 | `Font.Size.Small` / `Font.LineHeight.Small` |
| Body | 14 | 20 | `Font.Size.Body` / `Font.LineHeight.Body` |
| Lead | 16 | 24 | `Font.Size.Lead` / `Font.LineHeight.Lead` |
| Subtitle | 18 | 24 | `Font.Size.Subtitle` / `Font.LineHeight.Subtitle` |
| Heading | 20 | 28 | `Font.Size.Heading` / `Font.LineHeight.Heading` |
| Title | 24 | 32 | `Font.Size.Title` / `Font.LineHeight.Title` |
| Display | 28 | 36 | `Font.Size.Display` / `Font.LineHeight.Display` |

Weights: `Font.Weight.Regular` (400) and `Font.Weight.SemiBold` (600).
Families: Inter (default, no token) and `Font.Family.Mono` (Cascadia Mono → Consolas → Menlo → system monospace).
Colour: `Brush.TextSecondary` (defined for both Light and Dark themes).

## 3. Roles (style classes on `TextBlock`)

| Class | Size / line | Weight | Use it for |
|---|---|---|---|
| `display` | 28 / 36 | SemiBold | one large readout, e.g. print progress "64%" |
| `title` | 24 / 32 | SemiBold | page title (Printers, Agents, Workspaces) |
| `heading` | 20 / 28 | SemiBold | window or form heading (sign in, choose mode) |
| `subtitle` | 18 / 24 | SemiBold | card title (printer name) |
| `lead` | 16 / 24 | SemiBold | emphasised text inside a card or dialog (dialog titles use the same tokens) |
| `body` | 14 / 20 | Regular | normal reading text (this is already the default) |
| `small` | 13 / 20 | Regular | line under a title, status text, key/value rows |
| `caption` | 12 / 16 | Regular | footnotes, counters, file names, badges |
| `overline` | 12 / 16 | SemiBold, +0.5 tracking, secondary colour | tiny label above a value (HOTEND, BED). Write it in capitals |
| `metric` | 16 / 24 | SemiBold, tabular figures | a live number (temperatures) |

`display` and `metric` use tabular figures (`tnum`), so digits have equal width and a value that updates every second doesn't shift its neighbours.

### Modifiers

| Class | Effect | Use it for |
|---|---|---|
| `secondary` | muted colour (`Brush.TextSecondary`) | supporting text, labels next to a value |
| `mono` | monospace family | machine values: addresses, file names, keys, tokens |

Modifiers are always added to a role: `Classes="small secondary"`, `Classes="caption mono"`.

### Example: printer card header

```xml
<StackPanel>
  <TextBlock Classes="subtitle" Text="{Binding Name}" />
  <TextBlock Classes="small secondary" Text="CoreXY • 300×300" />
</StackPanel>
```

A temperature tile:

```xml
<StackPanel Classes="gap-0_5">
  <TextBlock Classes="overline" Text="HOTEND" />
  <WrapPanel>
    <TextBlock Classes="metric" Text="{Binding NozzleTemperature}" />
    <TextBlock Classes="small secondary" VerticalAlignment="Bottom" Text="° / 220" />
  </WrapPanel>
</StackPanel>
```

## 4. Mapping from the previous layout

| Where | Before | Now |
|---|---|---|
| Printer card name | 24 | `subtitle` (18) — a card title shouldn't be as large as the page title |
| Dashboard card name | 18 | `subtitle` (18) |
| Card sub-line ("Voron 2,4 • Klipper") | 13 | `small secondary` |
| Progress percentage | 28 | `display` |
| Time left | 13 | `small secondary` |
| Temperature label | 12 | `overline` |
| Temperature value | 15 bold | `metric` (16, SemiBold) |
| Temperature target | 12 | `small secondary` |
| File name / footer | 12 | `caption` (+ `mono` for the file name) |
| Sign in / sign up heading | 20 | `heading` |
| Overlay dialog title | 16 Bold | 16 SemiBold via `Font.Size.Lead` / `Font.Weight.SemiBold` |
| Form field labels (sign in) | default | `small secondary` |

Bold (700) is no longer used anywhere: SemiBold replaces it.

## 5. Rules

**Do**

- Pick the role from the table above before thinking about a size.
- Use `secondary` for supporting text instead of setting a `Foreground` colour by hand.
- Use `mono` for anything the user might copy or compare character by character.
- Use `metric` or `display` for numbers that update live.
- Reference `Font.*` tokens directly only inside control templates and styles, where a `TextBlock` class can't be applied (for example a `ContentPresenter` header).

**Don't**

- Don't set `FontSize`, `FontWeight` or `FontFamily` on a control in a view.
- Don't use `FontWeight="Bold"`. Use a role that is SemiBold.
- Don't add sizes outside the scale (15, 17, 22 …). If two sizes are close, pick one of the scale.
- Don't use `<LineBreak/>` and mixed-size `Run`s to fake a stack of lines.
- Don't use `secondary` for text the user has to act on, such as an error. Errors keep their own colour.
- Don't use more than one `display`/`title`-sized element per screen region.

## 6. Adding a role

If the same size/weight combination appears twice with the same meaning:

1. Check that it isn't already an existing role in disguise.
2. Add the size and line height to `Typography.axaml` only if the scale genuinely lacks it.
3. Add one class to `TypographyStyles.axaml` and a row to the table in section 3.
4. Update the preview in `TypographyStyles.axaml` so the role is visible in the designer.

## 7. Density and scaling (optional)

All styles read tokens through `DynamicResource`. A larger-text or compact mode is a second dictionary with the same `Font.Size.*` and `Font.LineHeight.*` keys, swapped in through `Application.Resources.MergedDictionaries` at runtime. No view markup has to change.
