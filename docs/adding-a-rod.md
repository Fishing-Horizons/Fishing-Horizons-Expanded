# Adding a new fishing rod

Every custom rod is one entry in `AllRods` in `Framework/Rods/RodsModule.cs`. The module builds the
`Data/Tools` entry, loads the icon, colours the casting animation and stocks Willy's shop from that
one entry — there is no other code to touch.

A rod added this way is an ordinary fishing rod with its own art, price and tier. **It does not get
the extra-fish mechanic**: that is the feeder rod's gimmick, and `DoubleHookPatches` arms it by
looking for that rod's ID specifically.

## The four steps

### 1. Draw the icon

A **16×16** PNG in `assets/`, e.g. `assets/example-rod.png`. That is all the art needed — the
in-hand casting and reeling animation is generated at runtime by copying the vanilla frames out of
the player's own game files, so it stays correct even if they use a tool recolour mod.

### 2. Add the two translation keys

In `i18n/default.json`, and ideally `ru.json` and `uk.json` too:

```json
"item.example-rod.name": "Example Rod",
"item.example-rod.description": "What the rod does.",
```

The prefix (`item.example-rod`) is what goes in `TranslationKey`. If a translation is missing the
game falls back to English, so a missing `ru`/`uk` entry is untidy but not broken.

### 3. Copy the template in `AllRods`

The commented-out template block sits at the bottom of the list in `RodsModule.cs`. Uncomment it and
change the values:

| Field | What it does |
| --- | --- |
| `Id` | Unqualified tool ID. Must be globally unique — keep the `waymeeNhaku.FHE_` prefix. |
| `Name` | Internal `Data/Tools` name, no spaces. Not shown to the player. |
| `TextureAsset` | The content name the icon is served under. Follow the existing pattern. |
| `TexturePath` | Path to the PNG from step 1. |
| `TranslationKey` | The prefix from step 2. |
| `Price` | Shop price and sale price. Fibreglass is 1 800g, iridium 7 500g. |
| `FishingLevel` | Level Willy requires before stocking it. Fibreglass 2, iridium 6. |
| `UpgradeLevel` | Tier, and with it the attachment slots. See below. Defaults to 3. |
| `CastingTint` | Colour of the rod in the player's hands. Omit for the vanilla colour of that tier. |
| `IsEnabled` | Optional config toggle. Omit and the rod is always on. |

Attachment slots come from `UpgradeLevel`, exactly as in vanilla:

| `UpgradeLevel` | Vanilla equivalent | Slots |
| --- | --- | --- |
| 0 | Bamboo Pole | none |
| 1 | Training Rod | none |
| 2 | Fibreglass Rod | bait |
| 3 | Iridium Rod | bait + tackle |
| 4 | Advanced Iridium Rod | bait + 2 tackle |

### 4. Optional: a config toggle

Add a `bool` to `ModConfig.cs`, point `IsEnabled` at it, and register it in
`Framework/GmcmIntegration.cs` next to the feeder rod's toggle if it should appear in the settings
menu. Skip all of this and the rod is simply always available.

## A note on `CastingTint`

`Game1.drawTool` tints the in-hand animation with `FishingRod.getColor()`, which vanilla hardcodes
per upgrade level — violet at level 3, goldenrod at 0, and so on. That colour *multiplies* the
artwork, so a tint baked into the frames would otherwise come out as the product of the two rather
than the colour that was asked for.

`RodPatches` handles this: when a rod sets `CastingTint`, the draw colour is forced to white, so the
colour set here is the colour that reaches the screen. Leave `CastingTint` out and the frames stay
greyscale and vanilla tints them by tier, like an ordinary rod.

## Giving a rod special behaviour

`RodDefinition` deliberately covers appearance and availability only. Anything that changes how
fishing *works* needs real code, and the pattern to copy is the feeder rod: put the mechanic in the
module that owns it, and have that module recognise the rod by its qualified ID
(`RodsModule.FindRod(...)` or a direct ID comparison). Patches shared by all custom rods go in
`RodPatches`.
