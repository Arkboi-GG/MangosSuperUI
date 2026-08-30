# GOA_ItemQuality client patch

Fixes in-client item-quality colors for the Reforged (5) / Legendary (6) /
Artifact (7) / Relic (8) tier shift, see `World Content Model.md` in the GOA
WIKI for the full story.

`ITEM_QUALITY_COLORS` (client `UIParent.lua`) is just a cache populated once
by calling the native `GetItemQualityColor(i)` — the tooltip and every other
in-client consumer calls that function live, not the cache table. So the fix
has to override the function itself, which `GOA_ItemQuality.lua` does. It's
loaded by appending its filename to a copy of the real client `FrameXML.toc`
(`FrameXML.toc` in this folder) — FrameXML always loads for every player
unconditionally, unlike an AddOns-folder addon, which needs a manual
per-player install/enable step.

## Rebuilding (only needed if the colors change)

There's no permanent button for this — it's a rare, one-off build. Add a
temporary controller action anywhere `MpqBuilderService` and `IConfiguration`
are already injected (e.g. `ItemsController`) and remove it again once run:

```csharp
var builder = new MpqBuilderService();
builder.AddFile("Interface\\FrameXML\\GOA_ItemQuality.lua",
    System.IO.File.ReadAllBytes("<repo>/MangosSuperUI/ClientPatches/GOA_ItemQuality/GOA_ItemQuality.lua"));
builder.AddFile("Interface\\FrameXML\\FrameXML.toc",
    System.IO.File.ReadAllBytes("<repo>/MangosSuperUI/ClientPatches/GOA_ItemQuality/FrameXML.toc"));
builder.Build(Path.Combine(_config["Vmangos:ClientDataPath"]!, "patch-5.MPQ"));
```

`patch-5.MPQ` is the next free patch number as of 2026-08-30 (patch/patch-2/
patch-3/patch-4 already exist) — check the client Data folder before reusing
it. The built MPQ is a deploy artifact, not committed here (same as
`wwwroot/patches/unified/patch-4.MPQ`).

If `FrameXML.toc` itself ever needs re-deriving (a client patch changes the
real file list), extract the live one first —
`MpqReaderService.ExtractFile("Interface\\FrameXML\\FrameXML.toc")` — rather
than hand-editing this copy from memory.
