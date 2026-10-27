# OctaGrid.Client — Spacing Guidelines

Tokens live in `Styles/Spacing.axaml`; reusable style classes live in `Styles/SpacingStyles.axaml`. For fonts, see [TYPOGRAPHY.md](TYPOGRAPHY.md).

## 1. Principles

1. **Everything sits on a 4px grid.** Each padding, gap, margin and control height is a multiple of 4. The only exceptions are `2` (hairline gaps) and `6` (dense text rows). A fixed base unit makes vertical rhythm come out right without extra effort, and it ends the "is this 14 or 15?" debate at review time.
2. **Proximity shows grouping.** The space *inside* a group is always smaller than the space *between* groups (Gestalt proximity). Users read structure from whitespace before they read borders. A card's inner padding (16) is smaller than the gap between cards (20), which is smaller than the gap between page sections (24).
3. **The parent owns spacing.** Put spacing on the container with `StackPanel.Spacing` or a `Border.Padding`, not on the children as `Margin`. A child that carries its own margin cannot be reused somewhere else, and margins collapse unevenly at the ends of a list.
4. **Views reference semantic tokens, not numbers.** `Gap.CardContent` describes intent. `12` is only a value. When a value needs to change, you edit one token instead of hunting through 40 views. Primitive `Space*` keys exist only to keep the semantic values on the grid.
5. **Nested radius shrinks.** An inner element's corner radius should be no larger than the outer radius minus the padding between them. This is why cards use 16 and the controls inside them use 8.

## 2. Primitive scale

| Key | px | Typical use |
|---|---|---|
| `Space0` | 0 | reset |
| `Space0_5` | 2 | nav item list, hairline gaps |
| `Space1` | 4 | icon button padding, tight inline |
| `Space1_5` | 6 | key/value rows, chip vertical padding |
| `Space2` | 8 | inline gap, button vertical padding |
| `Space3` | 12 | card content gap, icon-to-label gap, horizontal control padding |
| `Space4` | 16 | card padding, button horizontal padding |
| `Space5` | 20 | card grid gap, sidebar vertical padding |
| `Space6` | 24 | page section gap, page vertical padding, dialog padding |
| `Space8` | 32 | page horizontal padding |
| `Space10` | 40 | empty states |
| `Space12` | 48 | large empty states, onboarding |
| `Space16` | 64 | hero / splash only |

Naming: step × 4 = px (`Space5` = 20). This keeps names stable even if a future density mode changes the values.

## 3. Semantic tokens

### Gaps (`x:Double` → `StackPanel.Spacing`)

| Token | px | Where |
|---|---|---|
| `Gap.Hairline` | 2 | between sidebar nav items |
| `Gap.KeyValue` | 6 | Address / Protocol rows |
| `Gap.Inline` | 8 | chips row, header toolbar, status dot + label |
| `Gap.IconLabel` | 12 | 40px icon tile + title block, nav icon + text |
| `Gap.CardContent` | 12 | header / status / details / footer inside a card |
| `Gap.CardGrid` | 20 | between printer cards |
| `Gap.PageSection` | 24 | header → filter chips → grid |
| `Gap.FormField` | 4 | a label and its input |
| `Gap.Form` | 16 | between form fields, and fields → dialog buttons |

### Padding (`Thickness`)

| Token | Value | Where |
|---|---|---|
| `Padding.Page` | 32,24 | main content area |
| `Padding.Sidebar` | 12,20 | sidebar |
| `Padding.Card` | 16 | printer cards |
| `Padding.Dialog` | 24 | dialogs and flyouts |
| `Padding.NavItem` | 12,8 | sidebar entries |
| `Padding.Button` | 16,8 | default and primary buttons |
| `Padding.ButtonSm` | 12,6 | "Open", inline actions |
| `Padding.Input` | 12,8 | search and text inputs |
| `Padding.Chip` | 12,6 | filter chips |
| `Padding.IconButton` | 4 | "⋮" more-options button |
| `Padding.DividedSection` | 0,12,0,0 | a section under a top hairline |
| `Padding.Tile` | 12 | stat tiles inside a card (temperatures) |
| `Padding.Badge` | 8,2 | status pills |
| `Margin.DialogInset` | 24 | space between an overlay dialog and the window edge |

### Radius and control heights

| Token | Value | Where |
|---|---|---|
| `RadiusSm` | 4 | active nav indicator, tags |
| `RadiusMd` | 8 | buttons, inputs, nav items, icon tiles |
| `RadiusLg` | 12 | menus, popups |
| `RadiusXl` | 16 | cards, dialogs |
| `RadiusFull` | 999 | chips, toggles |
| `ControlHeightSm` | 28 | chips, small buttons |
| `ControlHeightMd` | 36 | buttons, inputs, nav items |
| `ControlHeightLg` | 44 | touch-first targets |

## 4. Mapping from the current Printers mock-up

The mock-up used off-grid values. Each one snaps to the nearest token that keeps the proximity hierarchy intact:

| Element | Mock-up | Token | New |
|---|---|---|---|
| Page padding | 28 / 36 | `Padding.Page` | 24 / 32 |
| Header → chips → grid | 22 | `Gap.PageSection` | 24 |
| Card grid gap | 18 | `Gap.CardGrid` | 20 |
| Card padding | 18 | `Padding.Card` | 16 |
| Card inner gap | 14 | `Gap.CardContent` | 12 |
| Icon tile ↔ title | 11 | `Gap.IconLabel` | 12 |
| Nav item padding | 9 / 10 | `Padding.NavItem` | 8 / 12 |
| Nav icon ↔ label | 11 | `Gap.IconLabel` | 12 |
| Sidebar padding | 20 / 14 | `Padding.Sidebar` | 20 / 12 |
| Header toolbar gap | 10 | `Gap.Inline` | 8 |
| Primary button padding | 10 / 16 | `Padding.Button` | 8 / 16 (+ MinHeight 36) |
| Search input padding | 9 / 12 | `Padding.Input` | 8 / 12 |
| Chip padding | 7 / 13 | `Padding.Chip` | 6 / 12 |
| Status dot ↔ label | 7 | `Gap.Inline` | 8 |
| Button / input / nav radius | 9–10 | `RadiusMd` | 8 |

The card grid gap moves up to 20 while card padding moves down to 16. This is deliberate: in the mock-up both values were 18, so the cards visually merged into one block.

## 5. Style classes

| Class | Target | Applies |
|---|---|---|
| `page-sections` | `StackPanel` | `Gap.PageSection` |
| `card-content` | `StackPanel` | `Gap.CardContent` |
| `key-value` | `StackPanel` | `Gap.KeyValue` |
| `nav` | `StackPanel` | `Gap.Hairline` |
| `form` | `StackPanel` | `Gap.Form` (stack of `field`s) |
| `field` | `StackPanel` | `Gap.FormField` (label + input) |
| `inline` | `StackPanel` | horizontal + `Gap.Inline` |
| `icon-label` | `StackPanel` | horizontal + `Gap.IconLabel` |
| `gap-0_5` … `gap-8` | `StackPanel` | primitive escape hatch |
| `page`, `sidebar`, `card`, `dialog` | `Border` | padding (+ radius for card and dialog) |
| `divided` | `Border` | top hairline + `Padding.DividedSection` |
| `icon-tile` | `Border` | 38×38, `RadiusMd` |
| `tile` | `Border` | `Padding.Tile`, `RadiusMd` |
| `badge` | `Border` | `Padding.Badge`, `RadiusFull` |
| *(none)*, `primary`, `small`, `icon` | `Button` | plain `Button` gets the default padding, min height and radius; the classes adjust it |
| `nav` | any `Button` subclass | nav item padding and height |
| `chip` | `ToggleButton` | chip padding, pill radius |
| `search` | `TextBox` | input padding and height |
| `section-label` | `TextBlock` | sidebar section label inset |

### Example: one printer card

```xml
<Border Classes="card" Background="{DynamicResource CardBackgroundBrush}">
  <StackPanel Classes="card-content">
    <DockPanel>
      <Button Classes="icon" DockPanel.Dock="Right" AutomationProperties.Name="More options" />
      <StackPanel Classes="icon-label">
        <Border Classes="icon-tile" />
        <StackPanel>
          <TextBlock Text="{Binding Name}" />
          <TextBlock Text="{Binding Kinematics}" />
        </StackPanel>
      </StackPanel>
    </DockPanel>

    <StackPanel Classes="inline">
      <Ellipse Width="8" Height="8" />
      <TextBlock Text="{Binding StatusText}" />
    </StackPanel>

    <Border Classes="divided">
      <StackPanel Classes="key-value"> … </StackPanel>
    </Border>

    <Border Classes="divided">
      <DockPanel>
        <Button Classes="small" DockPanel.Dock="Right" Content="Open" />
        <ToggleSwitch Content="Enabled" />
      </DockPanel>
    </Border>
  </StackPanel>
</Border>
```

Notice that the card contains no `Margin` at all. The containers own every gap.

## 6. Rules

**Do**

- Use `StackPanel.Spacing` for gaps between siblings.
- Use a semantic class or token first. Fall back to a `gap-*` or `Space*` primitive only if nothing semantic fits.
- If you use the same primitive twice for the same purpose, promote it to a semantic token.
- Use `DynamicResource` for tokens in styles so a density swap reflows at runtime.
- Set `MinHeight` from `ControlHeight*` rather than relying on padding for height.

**Don't**

- Don't hard-code numbers such as `Margin="0,0,0,14"` in views.
- Don't use `Margin` on list items to create gaps. Use the panel's spacing, or the item container's layout, instead.
- Don't use off-grid values (5, 7, 9, 10, 11, 14, 18, 22 …).
- Don't make the gap inside a group equal to the gap between groups.

## 7. Grids and item lists

- **Wrapping or uniform card grid:** a panel without its own spacing needs a gap trick. Put `Margin = Gap.CardGrid / 2` (10) on the item container, and a negative margin of `-10` on the `ItemsControl`, so the outer edges stay flush with the page padding.
- **`Grid` / `WrapPanel`:** newer Avalonia releases add `Grid.RowSpacing` / `ColumnSpacing` and `WrapPanel.ItemSpacing` / `LineSpacing`. Check that your package version has them. If it does, bind them to `Gap.*` tokens and drop the margin trick.

## 8. Density (optional)

Every style reads tokens through `DynamicResource`. A compact mode is therefore just a second dictionary with the same keys (for example `Padding.Card` 12, `Gap.CardGrid` 12, `ControlHeightMd` 32). Swap it into `Application.Resources.MergedDictionaries` at runtime, and no view markup has to change.
