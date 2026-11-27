# Githubie icon

The approved design is a white uppercase H with a bold black outline and three connected Git-style nodes inside it. The background is transparent; the H and node interiors remain opaque white.

- `githubie-source.png`: transparent master, extracted from the approved concept with the built-in image generation tool.
- `githubie.png`: 256 px application preview.
- `githubie.ico`: 16, 20, 24, 32, 40, 48, 64, 128 and 256 px RGBA frames.

Run `scripts/Build-Icons.ps1` on Windows to regenerate the PNG and ICO from the master. It trims transparent margins, resizes the approved artwork and makes nearly opaque white fills/black strokes fully opaque while preserving antialiased edges. Executables embed the ICO at build time. The two desktop UI assemblies also embed it as a managed resource, so tray and dialog icons do not depend on a working directory or installed asset file.

The built-in image generation tool used this final background-extraction prompt:

> Background extraction for a production Windows icon. Preserve the supplied approved icon EXACTLY: the chunky uppercase H silhouette with its thick black outline, WHITE filled H body, three WHITE filled circular nodes with black rings, black left vertical connecting line and black branch crossing the H crossbar curving up to right node. Remove the entire gray backdrop and any ambient shadow; background outside H and in its upper/lower open notches must be fully TRANSPARENT alpha. Keep all WHITE fill INSIDE the H body and circles opaque white. Clean crisp flat pure black and white edges, no gray shading. Do not redesign, no repositioning, no added or removed nodes, no separate enclosure, no caption. One centered icon fitting a square canvas with only 5 percent transparent margins, production clean icon asset.
