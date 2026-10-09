# Licences

Waypoint's own source code — everything in this repository not listed below — is free software:
you can redistribute it and/or modify it under the terms of the GNU General Public License as
published by the Free Software Foundation, either version 3 of the License, or (at your option)
any later version. See [LICENSE](LICENSE).

Copyright (C) 2026 the Waypoint authors.

It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.

The parts below are not covered by the GPL. They keep their own terms.

## Guild Wars 2 content

Waypoint is an unofficial fan project, not made, endorsed, sponsored or approved by ArenaNet or
NCSOFT. Guild Wars Games © ArenaNet LLC. All rights reserved. NCSOFT, ArenaNet, Guild Wars,
Guild Wars 2, GW2, Heart of Thorns, Path of Fire, End of Dragons, Secrets of the Obscure,
Janthir Wilds, Visions of Eternity, and all associated logos, designs, and composite marks are
trademarks or registered trademarks of NCSOFT Corporation.

The game's artwork is used under ArenaNet's
[Content Terms of Use](https://www.arena.net/en/legal/content-terms-of-use): free and
non-commercial. It is **not** licensed under the GPL, and the GPL gives no rights to it. That
covers:

- `GuildWarsCompanion.Avalonia/Assets/Images/spec_*.jpg` — specialization banners
- `GuildWarsCompanion.Avalonia/Assets/Images/expansion_*.png` — expansion emblems
- anything the app downloads at runtime from the Guild Wars 2 API, `render.guildwars2.com` or the
  Guild Wars 2 Wiki

Marker packs are downloaded by the user from addresses they choose, under each pack's own terms;
none are included here.

## Fonts

Under the SIL Open Font License 1.1, whose text is in `OFL.txt` beside the fonts:

- **Barlow** (`Barlow-*.ttf`) — Copyright 2017 The Barlow Project Authors
- **Marcellus SC** (`MarcellusSC-Regular.ttf`) — Copyright (c) 2012 by Brian J. Bonislawsky DBA
  Astigmatic (AOETI), with Reserved Font Name "Marcellus"
- **Inter**, through the `Avalonia.Fonts.Inter` package — Copyright 2016 The Inter Project Authors

## App icon

The emblem in `tools/appicon/design.png`, and the icons made from it, began as an AI-generated
image (Azure OpenAI). It is shipped with the app under the same terms as the code, as far as
anyone holds rights in it.

## Packages

Fetched from NuGet when building, and included in the built app:

| Package | Licence |
|---|---|
| Avalonia, Avalonia.* | MIT |
| SkiaSharp, HarfBuzzSharp (through Avalonia.Skia) | MIT |
| BlishHUD | MIT |
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.*, Microsoft.Data.Sqlite, Microsoft.AspNetCore.SignalR.Client, System.* | MIT |
| Microsoft.Maui.Graphics, Microsoft.Maui.Graphics.Skia | MIT |
| QRCoder | MIT |
| Velopack | MIT |
| SQLitePCLRaw.bundle_e_sqlite3 | Apache-2.0 (SQLite itself is public domain) |
| ZXing.Net | Apache-2.0 |
| Xamarin.AndroidX.* (Android only) | MIT bindings of Google's Apache-2.0 libraries |

Used only to run the tests, not shipped: xunit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT).

Blish HUD itself is not shipped: the PC Bridge downloads the official release from GitHub when
asked to.
