# Roadmap

What is left to build, what is deliberately not being built, and what the GW2 API has already
been proven unable to do. Kept in the repo because this list has been lost to a compacted
conversation once already.

Last verified against the code on 2026-10-07.

---

## Next

The file promises to say what is left, and for a long time it only said what was done. This is the
shortlist as it stands, with the reasoning, so the next session does not have to rediscover it.

### Could be built next

Nothing small is waiting just now; what is left needs the game or a device (below).

### Smaller things, carried rather than forgotten

- **A test failed once on 2026-10-05 and has not failed since.** One run reported one failure out
  of 1199; fifteen runs after it passed, and which test it was is not known. If it comes back, run
  with `--logger trx` straight away — the name is in the .trx file, not in the quiet console.
- **Karka Queen keeps the map centre**, and should. v1 publishes four events with her exact name on
  Southsun Cove, all with different centres; she is fought at whichever settlement she attacks. A
  precise answer to a question with four answers would be worse than the hollow needle.
- **What is left of `MapViewModel` has no unit tests.** Everything pure has been extracted —
  `TileGeometry`, `QuarterTurn`, `ZoomPad`, `MapSpace`, `MarkerLibrary`, `MarkerIndex`, `MapPicks`,
  `FollowBox` and `MapFocus`, the last of which holds the rule that a pinned map outranks the
  game. What remains is starting a load, moving the centre and telling the canvas to redraw.
  `MapCanvas` itself is tested since 2026-10-02: it draws from `IMapScene` rather than from the
  view model, so `MapCanvasTests` draws a fake scene onto a recording canvas and checks the zoom
  pad (a dimmed button keeps its plate solid, the pad comes after every tile and the player) and
  which tiles are drawn, asked for and kept. Markers, routes and the guide are still checked on a
  device only.
- **Tekkit's jumping puzzles that never say "jp"** are not imported. `MarkerKinds.Tekkit` takes a
  puzzle from its `…_startsign` only under a path with a `jp` word, which finds 63; Abaddon's
  Ascent (`tw_lws3_sirenslanding_abaddonsascent`) and others like it have the same signs and trail
  steps without the word. Telling them from a chest run's start sign needs the siblings, which the
  per-marker reading does not see.
- **Tekkit sends no `ETag` or `Last-Modified`**, so its UPDATE downloads all 47 MB every time.
- **The objective alone in the game is untested in the game.** Since 2026-10-06 an objective with
  no trail goes to the module by itself (`GuidedTrail.IsObjectiveOnly`), drawn with the trail's
  beam and ring: at its own height for a marker pack chest (heights kept since `ImportFormat` 5),
  at the character's height for the API's flat points (`height unknown`), and not at all for a
  world boss. Covered by tests and the test plan; the drawing itself needs the real game, since
  MumbleSim gets Blish HUD to update the module only every ten seconds.

### Waiting on hardware, or on the game

- **Whether the Special track's "Tenebral Ward" can be claimed.** The API reports the objective as
  0 of 0 and unclaimed, so `VaultSignal` counts it as a reward waiting and the vault's dot in the
  bar stays lit. If the game has nothing to claim there, objectives that count to 0 should be left
  out, of the dot and of the vault screen alike.
- **The map's painting behind Today, in the game.** While a character is in the game, Today's
  masthead stands on the loading screen of their map (`WikiArt.ForMapAsync`). Seen with MumbleSim
  in Crystal Oasis, which ticked `today-scene` on the test plan; 17 of 18 map names sampled match the
  wiki's "<map> loading screen" files, and a map that does not keeps its wash.

- **The Ubuntu Touch click has never been on a device.** `python
  packaging/ubuntu-touch/build-click.py` publishes linux-arm64 and writes
  `artifacts/gw2companion.mpsoft_<version>_arm64.click` itself, with no clickable and no
  container. The framework `ubuntu-touch-24.04-1.x`, the AppArmor `networking` policy group and
  policy version 2404.1 come from the UBports forum and want confirming there. The open questions
  are the device's: does Lomiri start a Wayland client that is not Qt (forum reports are mixed),
  is libicu present for .NET, and does the file picker work under confinement, where there is no
  portal and ContentHub is the platform's way.
- **What Ubuntu Touch will not have**, because the platform does not: a live camera scanner (the
  desktop's decode-a-picture scanner stands in), a WorkManager to poll for reminders with the app
  closed, and home screen widgets.
- **The same linux-arm64 build should run on a Switch 1** under switchroot's L4T Ubuntu Noble,
  which is X11, so without `--wayland`. Untested; the risk is the Tegra GL driver, which is what
  `--software` is for. Since 2026-10-05 there is a way to get it there: `packaging\linux\build.ps1`
  on the PC makes `artifacts\linux-arm64` — the self-contained app, `install.sh`, the menu entry's
  icon, a `BUILD` stamp — and `bash install.sh [--software]` on the Switch installs it for the user
  into `~/.local/share/gw2companion`, with a launcher in `~/.local/bin` and a menu entry, and
  `--uninstall` takes it away again, leaving the data. The script refuses a build for the other
  processor and names missing ICU or fontconfig. Tested end to end in WSL with a linux-x64 build,
  software rendering included; not yet on the Switch. On Linux the app has no reminders or widgets
  — there is no background poll — and keeps the API key as a file only the user can read.
- **Whether Visions of Eternity ever appears in `access`.** On 2026-10-05 the account's array was
  `["GuildWars2", "PathOfFire", "EndOfDragons"]` though Visions of Eternity is bought, and the API
  documents no flag for it. The player means to visit its maps and see whether that changes it; the
  debug build lets the raw answer be read from the phone's snapshot store. Until then Standing lists
  what the API says, which is the only honest thing it can do.
- **The reminders not yet seen on the shade.** The reset reminder, meta reminders and the session
  recap were each seen generated and held back by the player's quiet hours (17:00–22:30, all day at
  weekends) rather than seen arriving. A weekend, or an evening inside the window, settles it.
- **How soon the API's playtime moves during play.** The session recap reads sessions from
  `/characters/:id/core`'s `age`. If it rises while playing, Today says "This session · … so far";
  if it only moves at logout, a session appears in one step after it, which the recap handles but
  the "playing now" wording never gets to show. Only a real evening settles which.
- **Whether the phone clears the objective on arrival.** The in-world trail is confirmed in the
  game; the phone's half, `TrailShare.HasArrived`, is not. Expect up to two seconds of poll lag
  plus the 2.4 s arrival animation. MumbleSim cannot settle it: without a GW2 process Blish HUD
  runs module updates only about every ten seconds.

### Paused

- **The Linux desktop head**, set aside in favour of the Android app rather than for any fault.
  It runs under X11 in WSL and natively on Wayland (a CachyOS VM, Cinnamon), with Today, the
  timetable, the compass dial, and the map with tiles, markers and zoom. WSLg cannot test Wayland
  at all: its compositor has no `xdg_wm_base`, which Avalonia's Wayland backend requires.

---

## Done

### On the phone, 2026-10-07: a redesign, and a name

The app had a clean structure and looked conservative. It now has a painted look, a bottom bar, the
game's own art where ArenaNet's terms allow it, and a name of its own. Every entry was seen on the
phone unless it says otherwise.

**Done: one design system.** What the screens repeated by hand is named once in `App.axaml`:
`Border.card`, `TextBlock.heading.small`, `TextBlock.caption.small` and `controls:SectionBand`.
The metal golds mean coin only; Acclaim, achievement points and every other count are brass. Text
after a row's spine starts 16 in on every screen. The vault's rows were the odd ones out and match
Today's now, with the count in a column one width on every row so the bars all end together.

**Done: a bottom bar instead of the drawer.** Today, the vault and Characters always; the fourth
place is the compass at first and then whatever was last opened from More (setting `nav.fourth`;
Settings and the test plan never take it); and More, a sheet of the rest with a line of news where
there is some — the next world boss from the schedule alone, undercut and outbid counts once the
orders have been read. The vault's entry carries a dot while a reward waits (`VaultSignal`, fed by
whatever reads a track, so no request of its own). A window 720 wide or more has a rail of every
area instead.

**Done: painted mastheads.** Each area has a tint taken from the game, published as
`AreaTintBrush`, and a wash painted once by `tools/render-washes.py` (headless Chrome: Avalonia
cannot run SVG turbulence filters). A page that leads with a masthead sets `PageView.ShowsWash`.
Today's countdown, the vault's Acclaim and the wallet's coin are each their page's headline. The
live tab and every section heading take a dry-brush stroke of the tint — a hair texture from
`tools/render-brushstroke.html` used as an opacity mask, after a smooth fade was judged a smudge.

**Done: the game's art, where it may be used.** ArenaNet's Content Terms of Use let fan projects use
released content, with their notice (now at the foot of Settings, with a line saying the app is
unofficial) and without their logos — so no wallpapers carrying an expansion logo. `Ui/WikiArt.cs`
fetches from the Guild Wars 2 Wiki when first needed and keeps it in the cache directory:
- a character's elite specialization painting ("Spec image X.jpg"; the nine End of Dragons ones
  have none and use their "concept art N", the finished painting rather than the silhouette study),
  or the profession's for a character with no elite, lifted off its white paper by lightness so
  the ink stays; the page's wash and strokes take the profession's colour (`PageView.WashTint`, one
  white `wash_mask.png` for all nine);
- the loading screen of the map being played, behind Today;
- a featured vault reward's banner, over its icon.

**Done: Compass and Map in the painted look**, as a band rather than a masthead: they are all
tool from the bar down. `PageView.ShowsBand` puts the area's wash behind the bar alone, cropped
rather than squeezed, over a rule of the area's tint; the map's panel takes the same tint on its
edge.

**Done: a Here widget, and the widgets in the painted look.** A third home screen widget shows the
map being played, who is there, and up to three things left on it, one a line — the same words as
Today's "Here:" line (`HereWidget` in Core, tested in `HereWidgetTests`). Today stores the reading
the moment it works the line out and redraws the widgets (`IWidgetRedraw`); otherwise the
widgets' quarter-hour poll asks the PC itself, without a sweep and with five seconds' leash. A
reading older than 20 minutes says only where they were last, never old boss times as now. All
three widgets carry the area's wash, dimmed, and a brush stroke of its tint; the headings are
painted into a bitmap in Marcellus SC, because the phone's launcher ignored the font resource on a
TextView. After an install, a launcher can keep the old version's resources and show "widget
cannot be loaded" until it restarts; that is the launcher, not the app.

**Done: a loading sign on every screen.** A stroke of brass glides under the bar while the page's
view model is busy, and an empty list says "Loading…" instead of inviting a pull already under way.
The shell follows `BaseViewModel.IsBusy` itself. An Avalonia animation must run on the control, not
on its `TranslateTransform`: the first version did the latter and crashed the app at start-up.

**Done: a map's icons are found on its other floors.** Crystal Oasis names floor 1 as its default
and answers 404 there; its hearts and waypoints are on floor 49. The compass and the map showed no
icons on it at all. `GetMapDetailAsync` now tries the map's other floors (`MapDetailFloorTests`). The
same failure made the map's panel jump: a failed load was asked for again several times a second,
and now waits 30 seconds.

**Done: an icon and a name.** The icon is the emblem of the player's own design
(`tools/appicon/design.png`), cut out by `tools/appicon/layers.html` and rendered by
`tools/render-appicon.py` at every density. Dragons drawn in code were tried first and rejected as
looking machine-made; don't try that again. The app is called **Waypoint**: "GW2" is one of
ArenaNet's marks, and "Tyria Companion" belongs to an iOS app. Only names people read changed — the
package id `com.mpsoft.gw2companion`, the data folders, the settings keys and the Blish module's
namespace did not, since changing any of them loses data or the module.


### On the phone, 2026-10-06: a pack list, and a test plan

**Done: marker packs chosen from a list.** Settings › Data › BROWSE PACKS lists what a repository
offers, Blish HUD's `mp-repo.blishhud.com/repo.json` by default (45 packs with https addresses, one
more over plain http and left out, and the screen says so): name, author, size, last update, maps
covered, categories and description, searchable, ADD on each, ADDED where the pack is here already.
Adding goes through `MarkerStore.AddAsync` like a pasted address, so nothing about "downloaded, not
shipped" changes; the paste box stays for packs no list has. The list's address is a setting on the
same screen, and TacO's `MarkerPacks.json` format is read too. Kept as `packrepo.json`, fetched at
most daily, last copy used offline. "Covers maps you play" was considered and left out: the app keeps
no record of the maps a character has been on.

**Done: Tekkit's All-In-One imports.** TacO's list gives an address that answers 403; Blish HUD's
gives `tekkitsworkshop.net/download?download=1:tw-all-in-one`, which works, so it was added from
the list. It first imported nothing, for two reasons. Every path starts `tw_guides`, and "guide" is
one of `MarkerKinds`' exclusions, so the pack now has its own reading by last segment (a puzzle's
`startsign`, a leaf with "chest" in it that is not a trail). And `MarkerPack.Read` spent its
20,000-marker ceiling before filtering, on a pack of 56,368 POIs; it now counts only kept markers.
On the phone: 1900 markers on 72 maps (1837 chests, 63 puzzles), named `tw_ALL_IN_ONE` from the
server's `Content-Disposition` rather than "download" off the address. The other three packs were
measured unchanged by the ceiling fix, so `ImportFormat` stayed 4.

**Done: a test plan in the debug build.** A menu entry, under `#if DEBUG`, listing what was built but
not yet tried in the game; `TestEvidence.Saw` ticks the checks the app can see happen. See
`Services/TestPlan.cs`.

### On the phone, 2026-10-05: Today, reminders and the timetable

One long day's work, kept together. Every entry was seen on the phone unless it says otherwise.

**Done: Today names who is in the game and where** — "Imothep · Ranger · in Queensdale" under the
reset line, brass, read from `/game` on each refresh, and a tap on it opens that character. `PlayingLine` speaks only for a live report
with a character: MumbleLink keeps last night's character and map after the game closes, and that
line above today's list would read as now. Asked beside the API, without a subnet sweep and with
five seconds before it counts as no line, since Today opens with the app.

**Done: a recap of the last session.** Today ends with *Last session · 1 h 00 min · last seen
playing Mo 14:09 · Sweet Child O Night · 1 h 00 min · level 79 → 80 · +2g 00s 00c*, and a
notification says the same once a session has ended. The app keeps a ledger (`ledger.db3`, a
fortnight): at each half-hourly poll and each Today refresh, never closer than five minutes, each
character's playtime and level from `/characters/:id/core` and the whole wallet — one request for the
names, one per character, one for the wallet. `SessionRecap` finds the last run of points between
which playtime rose — the API's own counter, so the duration is what the game recorded — and
measures from the point before it to the first quiet point after, which catches the last minutes
before logging out. The wallet covers gold, karma, Astral Acclaim and the rest; losses are said too.
The notification is keyed by the session, so it comes once whichever of the poll or Today saw it
end, and not for a session more than two hours gone. Wiped with the key, like the activity store.
Seen on the phone with ledger points written around a real one, then removed; a real evening is
still to come — see "Waiting on hardware, or on the game".

**Done: map meta timers.** The timetable lists every map's meta under the world bosses, what is on
now first and then by how soon — *Bjora Marches · Drakkar and Spirits of the Wild · 15 min · 13:05*
— and the "Here:" line names the current map's: *Challenges on now · Battle in Tarir (Octovine) in
under a minute*, seen in Auric Basin at 10:59 UTC. The schedule is the Guild Wars 2 Wiki's own
`Widget:Event timer/data.json`, fetched (`MetaSource`, a week between fetches, the last copy kept
for offline) rather than typed in or shipped, so new metas arrive with the wiki's edits; Visions of
Eternity's are in it. `MetaSchedule` reads it as the widget's `fullPatternGenerator` does: the
`partial` sequence once from 00:00 UTC, then the `pattern` for the rest of the day. Left out: the
timeline row, the world bosses (the timetable has them, with kills), and festivals, which the data
lists all year whether or not one is on. A segment with no name is a lull and is never announced.

Segments are never joined, even with the same name either side: Dragon's Stand repeats one two-hour
segment all day, and joined it showed as one meta "until 02:00". And "(continued)" at the start of a
day is the previous day's last run carried over midnight, so it takes that run's name.

**Meta reminders, done the same day.** Per event, not per map: Auric Basin has four named stretches
and the data does not say which is the meta, so a reminder for each would be four a cycle. Tapping a
map on the timetable unfolds its events, each with REMIND; a chosen one shows under the closed row.
`MetaWatcher` needs no key and announces a chosen event inside the world bosses' 35-minute window,
once per occurrence. Seen on the phone: Forged with Fire in the Domain of Vabbi produced its alert,
which the user's quiet hours then held back — and "Check now" used to answer "nothing waiting" in
exactly that case. It now says how many were held back and why.

**Done: the Wizard's Vault shop.** The vault screen has two tabs now, Objectives and Shop. The shop
lists what Astral Acclaim buys, in the game's three places (Featured, Rewards, Legacy vault),
cheapest first, with the cost and the limit, and a brass spine on what the balance covers now.

**What is left of a limit cannot be known**, and the first version got that wrong. The wiki records
`purchased` answering 0 even after buying; the player's account showed it both ways at once — the
"/bless" Emote Tome reported as bought, two Wizard's Ascended Armor Chests bought and reported as 0
of 3. The first version believed every 0 once any purchase was reported, and said 3 of 3 chests were
left. Now a reported purchase is a floor ("bought out", "at most 2 of 3 left") and a 0 says nothing:
the reward shows "limit 3". The per-section total of what is left is gone with it — it added up the
wrong zeros. The screen says why above the list.

**Done: pinned goals on Today.** PIN AS GOAL on any item's page puts it in a Goals section after
the trackers: *Deldrimor Steel Ingot 231 / 271 · Short 20 Lump of Primordium, 10 Lump of Coal and 1
more · about 31s 20c to buy*, the same shortfall the item page's "all the way down" gives. Counted
in units of raw material (`GoalProgress`, over `CraftTree`): crude — 250 ore weigh as much as 250 of
anything — but what the account can see, where weighting by price would make every untradable
component weightless. Recipes come from the downloaded catalogue (`Gw2Cache.RecipeShapeAsync`), not
the forty API lookups the item page may spend behind a tap; holdings are counted without being
priced (`StorageService.CountAsync`). The API has no Mystic Forge recipes, so a legendary counts its
forge components as things to get, and the cost says "some of it cannot be bought" rather than
leaving them out of a total. Goals live in their own `goals.db3`, like the watch list, one per
item. Coming back to Today after pinning or unpinning re-reads the goals alone.

Then, the same day: **a quantity** — `− × 2 +` on a pinned item's page, and Today counts the whole
chain for that many (*Deldrimor Steel Ingot × 2 · 462 / 542 · Short 40 Lump of Primordium…*); the
stepper has a row of its own, because in the wrapping row it jumped a line as "× 1" became "× 2" and
the next tap landed on nothing. **A "ready" reminder** — `GoalWatcher`, once per goal and quantity,
"Everything for … is in hand", tested in `GoalAlertsTests` but not seen arrive. And **WATCH PRICE**
on any tradable item's page, which opens the price watch with the item chosen and sellers' current
ask said, so a goal's short material is one tap from its own page to a watch.

**Done: Today says what is left on that map** — "Here: map chest waiting · Shadow Behemoth in 1h
55m", under the playing line. `HereService` reads it after Today's trackers, from the lists they have
just stored (anything stored before the last reset is yesterday's and not used), and `HereLine` words
it. The map's bonus chest is found by name: the API publishes the chests as ids only, and an id is
the map's name with `_heros_choice_chest` on the end, so no table is typed in. World bosses come from
the timetable, those fought on this map, due within two hours and not killed today. Every part is
said only when it was read, and "nothing left" only when something was read and all of it is done.
Hero points were in it and are not: the endpoint is dead; see the settled limits. Seen on the phone:
Auric Basin with its chest, Queensdale with Shadow Behemoth.

**Done: a reminder in the hour before the daily reset** when Wizard's Vault dailies or time-gated
crafts are still open — "Daily reset in 42 min · 1 Wizard's Vault daily left · 3 time-gated crafts
not done." `ResetWatcher` asks the API only inside that hour, keys the alert by the day so the
half-hourly poll sends it once, and leaves out a vault that still shows yesterday's list (it rolls
over only at login), the same check Today makes. Quiet hours apply as to every reminder. Tested in
`ResetReminderTests`; not yet seen arrive on the phone: the hour is 01:00–02:00 in Germany, outside
the player's quiet hours on weekdays.

**Done: alerts the game already gives are held back while a character is in the game.** The roadmap
first said "suppress alerts while the game has focus", which contradicts quiet hours: that window
is *when you can play*, so playing is exactly when reminders are wanted. What playing does make
redundant is news the game tells you itself — a trading post sale, a finished vault objective.
Those two watchers set `GameSaysItToo`, and `WhilePlaying` drops their alerts when the bridge names
a character in the game; world bosses and price watches still come through. Held back, not
deferred, as with quiet hours. The bridge is asked only when such an alert is waiting, without a
subnet sweep and with ten seconds before the answer counts as unknown, which lets everything
through. "Check now" in Settings says how many were held back and why.

**The compass list is measured again each time it opens.**
`TargetRow` took its distance once, when the map's rows were loaded, and they are rebuilt only on
a map change — so after a port within one map the list went on saying how far everything was from
where the character had been, nearest-first by the wrong point (seen as 448,000 away beside a dial
that was right). Opening the list now re-measures every row from the live position and re-sorts
it with `TargetRow.Ranked`, world bosses last as before; not while it is open, so no row moves
out from under the finger. On the phone: Camp Resolve Waypoint first at Camp Resolve, then
Charnel Grounds · 0 away first once the simulator stood there, and a tap still picked the row
under it.


### One app, on Avalonia: Android, the desktop and Ubuntu Touch

The phone app was MAUI, and MAUI has no Linux target, which rules out Ubuntu Touch, where the
app is meant to run natively one day. So it moved to Avalonia, which the bridge already used.
Done in three steps over 2026-10-01 and 02; the MAUI app was retired at the end of it.

**Core first.** `GuildWarsCompanion.Core` is plain `net10.0`: the GW2 client, the caches, the
services, the view models, and now the map and compass drawing. What only a platform can do goes
through the interfaces in `Host\` — `INavigator` with Shell-shaped route strings, `ISettings`,
`ISecrets`, `IMainThread`, `IImageDecoder`, `IAccelerometer`, `ISystemActions`,
`AppEnvironment` — and `AddCompanionCore()` is the one service list every head shares. Core
keeps `Microsoft.Maui.Graphics` for its primitives, colours, rectangles and `IImage`, and nothing
else of MAUI.

**Then the app.** `GuildWarsCompanion.Avalonia` holds the views and the shell;
`.Avalonia.Android` and `.Avalonia.Desktop` (Windows and Linux) start it and add what only they
can do. The namespace is `GuildWarsCompanion.Ava`, because one ending in `.Avalonia` shadows
Avalonia's own inside the project. Every MAUI screen was ported except the SignalR spike. On
Android: a native CameraX scanner, the Keystore for the key, reminders, both widgets and the
share sheet. The package is `com.mpsoft.gw2companion`.

**Retiring MAUI** moved what the new app still linked from the old project into it — the Wi-Fi,
reminder and widget code, the fonts, the images, the icon's SVGs — and deleted the rest, with
the spike's server. The Android classes kept their namespace,
`GuildWarsCompanion.Platforms.Android.*`: WorkManager stores a worker's Java name with each
scheduled job, a placed widget is bound to its receiver's, and both names derive from it.

What the port taught, each item a debugging session:

- **Avalonia's Android pinch recognizer raises PinchEnded for every lifted finger.** Treat it as
  the end of a pinch only after a real one, or taps die.
- **`InvalidateVisual` throws off the UI thread**, and tile loads raise `Changed` from the pool,
  so the drawable view posts its invalidation.
- **`SKImage.FromBitmap`, cached per bitmap, did not draw 256-pixel tiles on the Android GPU**,
  though 32-pixel icons were fine. `DrawBitmap` does.
- **Barlow's three files all call their style "Regular"**, so a folder-wide font family drew
  every weight as one. Each weight is a resource naming its own file.
- **Avalonia's Android activity is AppCompat**: a theme not derived from `Theme.AppCompat`
  crashes at start.
- **CameraX's default 640x480 analysis frames could not read the bridge's pairing QR.** At
  1600x1200, decoding only under the reticle and without ZXing's AutoRotate, it reads in about
  three seconds where it took fourteen.
- **Nothing subscribed to the tile service's arrival event**, so on a phone with no bridge, where
  nothing else asks for frames, a fetched tile stayed invisible until the next zoom. A paired
  phone redraws on every bridge poll, which is why it went unseen. Behind it: a tile that failed
  was only asked for again on the next redraw. It now waits two seconds, doubling to a minute,
  and asks for the frame itself (`Gw2Tiles.RetryAfter`).
- **`Environment.GetFolderPath` returns an empty string for a folder that does not exist yet**,
  on Linux, and the data then went to a relative path. The desktop head creates the folder.

**Linux and Ubuntu Touch.** The desktop head runs under X11 and natively on Wayland. Bridge
discovery became portable on the way (`SocketBridgeDiscovery`), so pairing works off Android
too. The click is built but has not been on a device; see Next.

---

### The trail in the game world

The compass and the map follow a trail to the objective; now the Blish HUD module draws that same
stretch in the game itself — a flat brass ribbon along the ground the pack's author walked, and a
column of light on the objective — while the character is on that map.

**Nothing is converted on the way, on purpose.** A `.trl` point is MumbleLink metres, x, up, z, and
Blish HUD's world is those metres with the last two swapped, exactly as its own `ToXnaVector3` does
for the character. So the phone keeps each route's raw metres beside its continent points
(`RouteLine.Metres`), sends the stretch in those, and the module draws through the numbers the
author recorded. Only the objective needs a conversion — the API places a point of interest on the
continent and nowhere else — so `MapSpace.ToMetres` is the reverse of the import's two steps, with a
test that goes there and back. Its height is borrowed from the trail beside it.

**One format, three hops.** `GuideWire` is the whole of it: `guide 1`, `map`, `target`, then one
`p x up z` per line, or `clear`. The phone posts it to `/guide`; the bridge checks it — pairing code
as for a paste, and a body that is not exactly a trail stops there — and hands it to the module
unchanged between a `trail` line and an `end`, one point per line so the link's 512-character limit
never bites. The bridge keeps the last one and gives it to any module that connects later, so
starting Blish HUD after choosing the objective still draws the line.

**Sent once per objective and trail, not once per screen.** The compass and the map both plan a
guide, from slightly different positions at slightly different moments. `TrailShare` keys on the
objective and the trail's name, so the second says nothing new. It also decides when the line comes
down: putting the objective away does, and so does choosing a different one with no trail; looking
at another map on the phone does not.

**Not drawn when the trail has no heights.** A route stored before format 4 has none, and a ribbon
that drops to height zero at one point runs through the floor of the world. `ForGame` answers null
and the line is cleared rather than drawn wrong.

**What an overlay cannot do.** Blish HUD has no access to the game's depth buffer, so the ribbon is
drawn over terrain rather than behind it — Pathing lives with the same limit. For a chamber behind
rock that is arguably the point.

**Seen drawn, without the game.** Blish HUD only draws its world when it is attached to a process
with a window and MumbleLink's tick is fresh, and MumbleSim writes no camera. A scratch window app
stood in: it wrote MumbleLink with the character at the start of the real Camp Resolve → Picaroon
Scratch trail (52 points, from Lady Elyssa's Guides through the app's own importer) and a camera
behind them, and Blish HUD 1.3.0 was started with `-p` on it and `--module` on the bundled `.bhm`.
The bridge took the trail with `204`, the module logged `Trail on map 1015, 52 points.`, and the
screenshot showed the ribbon in perspective, starting underfoot and running off into the distance.

Two things that screenshot changed. The ribbon started at 45% strength and was a line the eye had
to look for against a dark floor; it starts at 70% now. And the column on the objective was not
there at all: the objective was 340 metres away, where a column a metre wide is three pixels. It
now widens and grows with its distance from the camera. That second version was not seen drawn —
the PC's screen locked before the camera could be pointed at it — so it is the one part of this
still unverified by eye, alongside all of it in the real game.

The same run also confirmed two things from earlier entries against the real Blish HUD: a module
reconnecting was handed the trail again at once, and ending the stand-in game had the bridge close
Blish HUD by itself — 12 seconds after the game went the first time, 0.3 the second.

**And from the phone.** With MumbleSim at Camp Resolve and the bridge starting Blish HUD on its own, choosing Picaroon Scratch on the compass sent `/guide` from the handset and the module logged `Trail on map 1015, 52 points.` — the same 52 the scratch run produced from the raw pack. CLEAR OBJECTIVE sent the clear, and the module logged `Trail cleared.`

### Following a trail to the objective

Asked for after a session in the Silverwastes: a point of interest in a hidden chamber, a compass
pointing confidently at a rock face, and no way to learn where the entrance was. The compass and
the map now follow a trail somebody walked, where an installed pack has one that passes the
objective, instead of the straight line.

**Not pathfinding, and it cannot be.** A route planner needs to know where the ground is — walls,
cliffs, cave mouths — and nothing ArenaNet publishes says. The API gives a point of interest as one
coordinate, MumbleLink gives the character's position, and the straight line between them is the
wrong answer in exactly the case that prompted this. A marker pack's trails were recorded by
players walking there. Where one passes the objective, it is a way known to work.

**Measured before it was built.** Against the Silverwastes' 18 points of interest: Lady Elyssa's
Guides passes within 25 continent units of every one of them, and on the last stretch to Blue Oasis
walks 301 units to cover a straight line of 186. Teh's Trails, the pack used until then, has no
trail on that map at all. So the feature is only as good as the packs installed, and a guide that
finds nothing says nothing: the compass is what it was before.

**`TrailGuide` is the whole rule.** A trail counts if it passes within 40 units of the objective —
every Silverwastes point had one within 28, the next nearest were hundreds away. Of several, the
one that makes the whole way shortest wins, and getting onto a trail is counted at four times its
straight-line length. That weight was found by its test, not in the field: at face value a trail
starting 390 away across a wall beat one starting underfoot and running 600 round it, which is the
straight line winning again by another route. The needle then aims 40 units ahead along the trail,
back to the trail when the character is more than 60 off it, and at the objective itself for the
last 40. The readout is what is left along the trail and how far up or down it goes; the map draws
the stretch still to walk over the rest, in ink under full brass.

**Heights, which the app used to throw away.** A `.trl` point is x, y, z with y up, and routes were
stored flat because the map is flat. "134 m lower" is the most useful thing the guide says about a
chamber, so heights are now read, thinned with their points, and stored as a third number per point
(`routes 3`; 1 and 2 still read, without heights). Under three metres is said as nothing — a
trail's heights wobble that much over flat ground.

**Most breaks in a trail are not gaps.** A zero triple lifts the pen, and routes used to be cut at
every one. Measured on Lady Elyssa's Silverwastes map completion: 76 breaks, 62 under five metres,
median 1.3 — the author pausing the recorder. The rest are glides and drops at 18 to 38 metres, and
three waypoint ports at 490 and 798. Kept as breaks, one walk became 249 pieces and a guide handed a
piece 26 units long was a straight line again. Breaks within 40 metres are now joined over: the
Silverwastes went from 249 runs to 37, and the whole pack from 20,000 — the ceiling, so it had been
silently truncated — to 6,232.

**The per-trail point ceiling was cutting routes short too.** `TrailPack.Read` stopped at 4,000 raw
points. Lady Elyssa's Silverwastes chest run is 7,778, and it lost the last three places it visits.
Now 20,000 before thinning, which takes that run to a few hundred.

**Both changes needed every pack fetched again**, since an unchanged pack answers 304 and would
never yield what the importer had learned to read. `MarkerStore.ImportFormat` is 4 for that.

**Porting onto the trail, done 2026-10-05.** Where the trail to an objective only begins after its
author ported, the guide could only say how far it is to the trail in a straight line — Charnel
Grounds from Camp Resolve is "925 to the trail". `PortRoute.ToTrail` now picks the waypoint that
makes the rest of the way cheapest, onto the trail and along it, weighting the straight line four to
one as `TrailGuide.Find` does; and while a trail is followed, the port advice is that one rather
than the waypoint nearest the objective, which sent the character back to the rock face. Whether it
beats walking is judged on the same weighted measure, because 925 across ground nobody walked is not
a distance to take at its word. On the phone, at Camp Resolve with Lady Elyssa's Guides: *back to
the trail, bear left · 925 to the trail · via Drydock Grotto Waypoint · then 273 to the trail, 26
along it*, on the compass and the map alike. The compass readout was 96 units tall for six lines
that need about 105, so the route line had always been cut in half; it is 108 now.

**Verified against the real pack**, read by the app's own importer outside the app: from Camp Resolve
Waypoint the guide stays on a trail for four of the six northern points — Blue Oasis 1,975 along
the trail against 1,302 straight, SS Topsy-Turvy 1,917 and 134 m lower, Picaroon Scratch 1,283 and
43 m lower, Remnant's Retreat 1,060 — at about a microsecond per dial frame.

**Then on the phone**, with the simulator standing at Camp Resolve Waypoint and Lady Elyssa's
Guides added in the setting (3,430 markers, 65 maps, 6,240 routes). UPDATE ALL answered "3 updated
· 1 already up to date": the three packs imported before format 4 were fetched in full, and joining
shows in their counts — Teh's Trails 1,334 routes to 1,180. With Picaroon Scratch chosen, the
compass read *hard right · 1283 along the trail · following LadyElyssa · Barefoot · Trail Segment
· One · 43 m lower*, the number the check outside the app had produced.

**The dial had no needle, which the first screenshot showed.** The only direction it drew to the
objective was a faint line straight at it — so the words followed the trail while the picture
still pointed at the rock face. The stretch still to walk is now projected onto the dial like the
walked path, in solid brass from the character outwards, and the straight line is left off while a
trail is drawn.

**And the map chose a different trail from the compass.** It loads before the first frame, so its
first choice was made with no position: the trail passing closest, the mounted run at 1,927 along,
while the compass followed the walking run at 1,283. A choice made blind is now made again when the
first position arrives, and both screens read 1,283.

### Blish HUD, with the game

The bridge can start Blish HUD when Guild Wars 2 starts and close it when the game closes, keep its
own copy of Blish HUD downloaded and current, and — through a small Blish HUD module of its own —
do a tapped emote in the game instead of putting it on the clipboard. One checkbox, off by default,
Windows only.

**Started, never hosted.** Blish HUD is net472 and MonoGame, and its modules are built against its
own runtime; none of it loads into the bridge's net10 process, and a fork would have the same
problem plus a Blish HUD to keep merging. Starting its executable is the whole integration, which
keeps official Blish HUD and every module working exactly as they do when started by hand.

**Downloaded from GitHub, and only with a checksum.** Blish HUD ships as a zip and nothing else. The
bridge reads `/releases/latest`, downloads `Blish.HUD*.zip` from `github.com` over HTTPS, hashes it
while it streams and compares against the SHA-256 digest GitHub publishes for the asset, before a
single file is unpacked. A release without a digest is refused — releases before mid-2025 have none,
v1.3.0 has one. Each version gets its own folder under `%LOCALAPPDATA%\GuildWarsCompanion\BlishHud`,
because an update can arrive while the old one is running and Windows will not replace an executable
in use; old folders go once nothing runs from them. Blish HUD keeps settings and modules in
`Documents\Guild Wars 2\addons\blishhud` unless a `Settings` folder sits beside the exe, and a fresh
release has none, so deleting a version folder never deletes anybody's setup. Checked at most every
six hours; an offline evening plays on the version already there.

**A session is one game process, not one stretch of a moving tick.** The tick also stops at
character select and on long loading screens, so "live again" would start Blish HUD after every
relog, and "stalled" would close it every time somebody swapped character. MumbleLink's context
carries the game's process id at offset 80; Blish HUD starts once per id, and is closed when
Windows says that process has gone — checked by name as well as id, because ids are reused. Only
Blish HUD running out of the bridge's folder is closed, and it is looked for for thirty seconds
afterwards, because Blish HUD set to stay in the tray restarts itself as a new process when the
game exits. Measured: closed 12 seconds after the stand-in game was killed. Real Blish HUD ignores
`CloseMainWindow`, so in practice it is ended after a five-second grace.

**Emotes done in the game — the keystroke refusal, revisited on 2026-09-28.** The entry under
"Copy chat links" said a bridge must never type into the game. That is still true of the bridge.
What changed is the finding that Blish HUD itself carries this as a service —
`GameService.GameIntegration.Chat`, clipboard in, Enter, Ctrl+V, Enter, clipboard restored — and
that its official module repository at `pkgs.blishhud.com` carries modules built on it: EmoteTome,
Emotes (which calls exactly `Chat.Send`), Chat Shorts, LT Messages. The rule they share is one
click, one chat line, which is the rule usually cited for Guild Wars 2 macros. A tap on the phone
producing one emote is the same shape. ArenaNet's policy text itself was not re-read for this.

So the bridge does not type; a Blish HUD module does, and only this:

- The phone posts to `/emote`, not `/paste`. A bridge from before `/emote` answers 400 and the phone
  asks again on `/paste`, so an old bridge still copies the emote.
- The bridge hands it on only if the text is **exactly** a command or alias from `Emotes` — the
  same table the phone shows, compiled into the bridge. `/wave hello`, a chat link, or `/gg` (which
  forfeits a PvP match) are never passed on, whatever the phone sent; they go to the clipboard.
- The module is reached over loopback only, with a token the bridge writes to
  `%APPDATA%\GuildWarsCompanion\blish-module.txt` fresh each run. `ModuleWire.cs` is the whole
  conversation — hello, ok, emote, done, busy — compiled into both ends.
- The module does it only while Blish HUD says the game is in front and in game, no text box is
  focused (otherwise the emote lands in the middle of what the player is typing), and the map is
  closed. Otherwise it answers busy with the reason and the bridge copies instead.
- 200 means done in game, 204 means copied, and the phone says which: "/wave · done in game on
  Desktop".

The module is loaded with Blish HUD's `--module` switch when the bridge starts Blish HUD, which
enables it immediately and installs nothing into Blish HUD's folders. Blish HUD started by hand does
not have it, and emotes are copied as before.

**Not verified against the game.** No Guild Wars 2 on the machine this was written on. What was run:
the download and update against GitHub; start and close against a stand-in game (MumbleSim, now
writing its own process id); the whole `/emote` path over HTTP against a stand-in module speaking
the real wire, including a wrong token, busy, disconnected, `/wave hello` and `/gg`. The module
itself compiles against the BlishHUD 1.3.0 package and has not yet typed into a real chat box. The
first real session should confirm three things: Blish HUD appears when the game does, a tapped
emote plays, and Blish HUD goes when the game does.

**Loaded and connected in real Blish HUD, by accident.** During the trail-guide test on 2026-09-28
the simulator counted as a newly started game, the start-with-game box was still ticked, and the
bridge started Blish HUD 1.3.0 exactly as designed. Its log: launched `with args "--module
…\GuildWarsCompanion.Blish.bhm"`, then `GuildWarsCompanion.Blish.CompanionModule | Connected to
the GW2 Companion bridge.`, and nothing from the module after that. So the `--module` switch, the
module's load and the loopback handshake are proven against the real thing; typing into a real
chat box still is not. The run also showed the one way the close-with-game promise can fail: the
bridge was ended before the game was, and a Blish HUD started by a bridge that no longer runs is
nobody's to close.

### MumbleLink — the PC half

The bridge now reads what Guild Wars 2 publishes for Mumble: which character is logged in, their
profession, race, specialization and commander tag, which map they are on — by name — what they
are riding, whether they are in combat, and whether the game window has focus. `GET /game`
answers with it, behind the pairing code.

**The map is a number, and the API is the only thing that knows what it is called.** MumbleLink
gives 28; `/v2/maps/28` says Wayfarer Foothills. `MapNames` decides what to ask about and what
never to ask about again, `Gw2Maps` makes the request, and `GameReport.MapName` carries the
answer to the window and the phone alike — with `MapLabel` reading "Map 28" until it lands,
because nothing is allowed to make the poll wait on a socket. The name therefore appears on the
following poll, two seconds later, and the id shows for one line first.

The endpoint is public, which is what makes it allowable at all: no key, no account, nothing
about the person playing leaves the PC. One request per id for the life of the process — map
names are static data, so a second could only repeat the first — which is a handful of requests
an evening against an allowance of 600 a minute, and the reason there is no token bucket here
unlike the phone's `Gw2Client`. A 404 is an answer and is kept; a failed request is not an
answer and is tried again a minute later.

Found by running it against the live client: awaiting a lookup answered "not yet" about a name
that was a moment away, because the poll had already dropped a request for that id and the
second caller saw one in flight and walked off. It now joins the outstanding request, and a test
that fails on the old behaviour keeps it that way. Verified with the game running: *Sweet Child
O Night · Thief — Wayfarer Foothills · playing*, and `"MapName":"Wayfarer Foothills"` in the
`/game` JSON.

**This is not a hook and not injection.** The game writes a shared memory block every frame
specifically so third-party tools can read it — it is how TacO and BlishHUD work. Nothing here
writes to the game or sends it anything. The one thing that does — an emote, done in the game —
goes through Blish HUD's own chat service in a Blish HUD module, not through the bridge; see
"Blish HUD, with the game".

**The trap is that the block outlives the game.** MumbleLink keeps its last values after Guild
Wars 2 closes, so the character who logged out last night is still sitting there, in the map they
left, apparently in combat. The only signal is `uiTick`, which the game advances every frame and a
closed game does not — so `Liveness` tracks when the tick last moved and calls it dead after six
seconds. Six rather than one because a loading screen or an alt-tabbed client stalls the tick
briefly, and flickering "closed" at somebody who is still playing is worse than a slow answer.

That the tick is the way to tell is *not* documented; the wiki says only "all values are updated
every frame". It is what every tool does and it follows from that one guarantee.

Caught for real during testing, in the best possible way: the fake publisher exited, and the block
stayed alive — because the bridge's own open handle keeps the mapping alive. Tick frozen at 1120,
`IsLive` false, state still shown but labelled. Exactly the case the design exists for, arrived at
by accident.

**Verified without the game installed.** `MumbleLinkTests` builds a block by walking the documented
struct field by field, accumulating offsets from field *sizes* rather than restating the reader's
constants — a fixture that agreed with the reader by construction would prove nothing. Twelve tests
covering every field, the UI bitmask, a zero tick, a short block, a frame caught mid-write, ids
outside the tables, and the liveness window. The Windows `OpenFileMapping` path, which no unit test
can reach, was exercised by publishing a real named mapping from PowerShell and watching the bridge
read it: *Imothep · Ranger, Norn, commander, map 1155, on a Raptor, `Ui: 72` = focus + combat*.

**Offsets are written out, not expressed as a struct.** A managed struct's padding is a negotiation
with the runtime; this is a fixed wire format.

**Linux is the trivial half of a hard problem.** Reading is `File.Open("/dev/shm/MumbleLink")` and
nothing more. Getting the block there is the work: GW2 runs under Proton and the mapping lives
inside the Wine prefix, so a helper has to run *in the prefix* and copy it out — `jokolink` is the
reference implementation. The code path is written and cannot be tested here.

**Android routes LAN addresses out of the mobile interface**, and that is the finding that cost
the most. A phone whose Wi-Fi has no internet — a PC hotspot on a network that blocks NAT — makes
mobile data the default network, and Android then sends *every unbound socket* there, including
one addressed to 192.168.137.1:

```
192.168.137.1 via 192.0.0.1 dev rmnet_data1
```

The LAN is one hop away over an associated Wi-Fi interface and completely unreachable. There is no
fix inside HttpClient; the request has to be bound to a chosen network, which is a platform
capability. `WifiBridgeTransport` uses `Network.OpenConnection`, Android's own answer, and falls
back to the ordinary client when there is no Wi-Fi — so nothing changes for the common case where
Wi-Fi is already the default.

Worth having beyond the hotspot: it makes "Wi-Fi for the LAN, mobile data for the internet" a
supported configuration rather than an accident. Demonstrated exactly that way — with mobile data
as the default network, `curl` from the phone's own shell got nothing from the bridge while the
app, on the same phone at the same moment, paired successfully.

The trail to it is worth remembering, because every step looked like the previous one was wrong.
The phone's saved Wi-Fi carried `acceptUnvalidated`, set by answering "stay connected?" to
Android's no-internet prompt — that pinned the default to a hotspot with no internet. Clearing it
restored the internet and broke the bridge. Only then was the actual problem visible.

**The phone half** now reads `/game` in four places: the roster's "playing now", Today's playing
and "Here:" lines, and holding back alerts the game already gives. See the 2026-10-05 entry
under Done.

**Done: the roster names the character being played.** `PlayedNow` holds the precedence — the game
naming a character beats a playtime delta beats a stored detection — and the ordering is the whole
point. Playtime keeps rising for a minute or two after a logout, so without it the character just
left still claims "playing now" beside the one that actually is; a test asserts only one row can
ever say it. A null live reading is ignorance and falls through to the old inference, so nothing
changes for anyone who never sets up a bridge. The brass bead beside the name is reserved for the
fact: a row the guess called "playing now" gets the words and no mark.

Verified on the device against the live roster: *Imothep The First · playing now* with the bead,
while *Sweet Child O Night* kept "played just now" from its own history and stayed unmarked.

---

### The PC bridge

`GuildWarsCompanion.Bridge` — an Avalonia desktop app that listens on the local network and puts
what it receives on the PC's clipboard. Tapping an emote on the phone now lands it next to the
game, ready for Ctrl+V in the chat box. This is the feature "Copy chat links" was dropped for
lacking, three entries down.

**Avalonia because MAUI has no Linux target at all.** One `net10.0` project, no platform TFM:
Avalonia.Desktop carries the Win32 and X11 backends in one assembly and picks at runtime. The
Wayland backend graduated from preview in 12.1 but is experimental and opt-in, so a Wayland
session runs this on XWayland.

**The clipboard is the reason a tray utility is the right shape, not a convenience.** On Windows
and X11 the clipboard holds a reference to the *owning process*, not a copy — Avalonia's own docs
say to call `FlushAsync()` before termination or the content does not persist. So the tempting
design, a console tool that sets the clipboard and exits, loses its content the moment it exits.
A process that stays running is what the platform requires. The flush happens immediately after
each set rather than at exit, so a kill loses nothing.

**Three things were found by running it rather than by reading it:**

- **Windows lets one process hold the clipboard open at a time**, and plenty of things take it
  for a moment. `OpenClipboard` then fails with `CLIPBRD_E_CANT_OPEN`; Avalonia surfaces that
  rather than retrying, so the retry lives in `MainViewModel.CopyAsync` — six attempts backing
  off to 300ms. The very first paste this app ever performed failed for exactly that reason.
- **A null clipboard was being skipped silently while the paste was still logged as accepted.**
  The worst of both: the phone is told it worked, the window agrees, and Ctrl+V produces whatever
  was there before. `CopyAsync` now returns whether it got there and the log follows it.
- **`RemoteViews`-style surprises have a desktop cousin**: `{StaticResource InterFont}` in
  App.axaml compiles and throws at startup, because `WithInterFont()` registers an embedded font
  asset, not a resource key.

**A raw `TcpListener`, not `HttpListener`.** On Windows HttpListener sits on http.sys, and binding
anything but localhost needs a `netsh http add urlacl` registration or an elevated process. Asking
someone to run a command as administrator before their clipboard works is a worse first five
minutes than parsing one request line by hand.

That reasoning rules out `HttpListener` and nothing else. It was written as though it had ruled
out ASP.NET Core too, and it had not — ASP.NET Core was never weighed. Kestrel is a managed socket
server rather than an http.sys front end, and it binds `0.0.0.0` from an ordinary unelevated
account: measured on 2026-09-14, `curl http://127.0.0.1:47625/spike` against a Kestrel host
started by `dotnet run`. The real cost of ASP.NET Core here is deployment, not privilege — see
"SignalR for the compass, HTTP for everything else" below.

**The bridge knows nothing about Guild Wars 2.** It receives text and pastes it — no chat-link
encoding, no item ids, no API key on that machine. The phone already generates whatever it sends,
so the bridge never needs updating when the app learns to send something new. `BridgeProtocol.cs`
is compiled into all three projects by `<Compile Include>`: two copies of a header name drift into
a bug that looks exactly like a mistyped pairing code.

**Security is one shared code**, eight Crockford symbols (no I, L, O or U, because it is read off
a screen and typed on a phone), compared in fixed time, case and dashes ignored. An unpaired
bridge refuses everything rather than waving it through. The window lists every arrival including
the refusals, which is the audit trail: anything on the LAN can knock. It is plain HTTP — the
payload is a chat command, but the code does travel in a header, so this is a home-network tool
and not more.

**Verified end to end**, phone to PC over a Windows hotspot: tapping /dance shows "Copied /dance ·
sent to PC" and the PC's clipboard changes to `/dance`. `BridgeProtocolTests` covers the refusals,
and the wrong-code path was confirmed against the running bridge (403, red-spined in the window's
log).

**Android blocks cleartext HTTP, and a LAN bridge is cleartext by nature.** Pairing failed with
"Cleartext HTTP traffic to 192.168.137.1 not permitted" — which reads exactly like a firewall
problem and is not one. `Platforms/Android/Resources/xml/network_security_config.xml` permits it
in the base config and then pins `guildwars2.com` back to HTTPS explicitly, so the one connection
carrying an API key can never silently downgrade. A build-time domain allowlist cannot cover the
bridge, because its address is only known once somebody types it in.

That failure was invisible at first because `BridgeClient` swallowed the exception and reported
"the PC did not answer" — a guess, and the wrong one for every failure where the request never
left the phone. It keeps `LastError` now and the pairing message appends it. Same lesson as the
bridge's own silent clipboard skip, learned twice in one afternoon on opposite sides of the wire.

**The address list is a snapshot, not a subscription.** A hotspot brought up after the bridge
started does not appear, which is exactly when someone is pairing a phone; hence "Recheck
network". Worth watching whether that button turns out to be the common path rather than the
recovery one.

**Minimise to tray, with a way back.** Closing the window hides it and leaves the bridge
listening; the tray icon restores it and its menu quits. Three pieces make that safe rather than
a trap:

- `ShutdownMode.OnExplicitShutdown`, because hiding the only window otherwise ends the process —
  which is the exact opposite of what the setting promises.
- **The window is hidden, never disposed.** The clipboard is a property of a `TopLevel` and on
  Windows and X11 its contents belong to this process, so tearing the window down to a bare tray
  icon would leave the bridge listening with nowhere to put what arrives. Verified: a paste
  from the phone still lands while the window is hidden.
- **Launching the app again raises the running copy** rather than starting a second one, via a
  loopback-only `/show`. That is the recovery path, and it matters most where the tray is least
  reliable: TrayIcon needs StatusNotifierItem on Wayland, which KDE implements and GNOME does
  not support at all. Without it, a GNOME user who turned this on would have no way back but to
  kill the process.

The setting is `bool?`, not `bool`. Null means nobody has said, which is not the same as saying
no: a config written before the setting existed still gets the platform's answer — on for
Windows, off elsewhere — instead of a silent "off".

Next: MumbleLink. Two rows of the settled-limits table below — the currently played character and
the current map — exist only because MumbleLink is PC-only, which stopped being a constraint the
moment there was a PC-side process. It also carries an `InCombat` and a `GameHasFocus` bit, which
answer directly what `QuietHours` currently approximates with a clock. The Linux cost is real: GW2
runs under Proton and MumbleLink is a Windows shared-memory object inside the Wine prefix, so a
native app needs a helper in the prefix copying it to `/dev/shm` (see `jokolink`).

---

### SignalR for the compass, HTTP for everything else

A compass rose pointing at the next objective needs the player's position and heading several
times a second. The existing `/game` endpoint cannot carry that: it is one TCP connection per
poll with `Connection: close`, so ten frames a second is ten handshakes a second, each one going
through `Network.OpenConnection` and a pool thread. The screen needs a connection that stays open,
which is a different shape from the four request/response endpoints and does not replace them.

**The direction that was missing is PC to phone, and only that.** The compass needs three things:
the player's position, which `GameState` already carries; the player's heading, which MumbleLink
publishes and the bridge does not yet read — `fAvatarFront` at offset 20, with `AvatarAt = 8`
sitting declared and unused in `MumbleLink.cs`; and the objective's position, which comes from the
Guild Wars 2 API. The phone holds the API key and already talks to that API, so it resolves the
objective and computes the bearing itself. The bridge never learns what an objective is, which
keeps the rule that the bridge knows nothing about Guild Wars 2 intact.

**The real question was whether SignalR can reach the PC at all**, and it could not be answered by
reading anything. `WifiBridgeTransport` exists because Android routes every unbound socket to the
default network, so on a phone whose Wi-Fi has no internet a request to `192.168.137.1` leaves
through the mobile interface. Android's own answer is `Network.OpenConnection`, which returns a
`Java.Net.HttpURLConnection` — and SignalR is built on `HttpClient` and `ClientWebSocket`, neither
of which can express "use this network". So SignalR inherited exactly the problem that class was
written to solve.

A throwaway rig settled it on 2026-09-14: a Kestrel hub on the PC, and a matrix on the phone of
three binding strategies across the three transports. The phone was on LTE as its default network
with the PC's hotspot associated but unvalidated — the failing configuration, confirmed before the
app ran at all: `curl` to `192.168.137.1:47625` from the phone's shell timed out with `rc=28`, and
so did `curl --interface 192.168.137.191`, and so did `ping`.

```
FAIL  None            WebSockets        no connection
FAIL  None            ServerSentEvents  no connection
FAIL  None            LongPolling       no connection
PASS  ProcessBound    WebSockets        connected in 201ms, 10 frames, pong
FAIL  ProcessBound    ServerSentEvents  no connection
FAIL  ProcessBound    LongPolling       no connection
FAIL  ConnectCallback WebSockets        no connection
PASS  ConnectCallback ServerSentEvents  connected in  98ms, 10 frames, pong
PASS  ConnectCallback LongPolling       connected in  95ms, 10 frames, pong
```

Four things came out of that, and three of them were not guessable:

- **A source-address bind is not enough.** Android routes on the socket's fwmark, not on its
  source address, so `--interface 192.168.137.191` fails exactly as an unbound socket does. Only
  `Network.bindSocket`, which sets the mark, changes anything. That is the sharp version of what
  `BridgeTransport.cs` describes in prose.
- **`ParcelFileDescriptor.FromFd` carries the binding across.** .NET Android's `Network.BindSocket`
  wants a Java socket or a Java file descriptor and a .NET `Socket` is neither; `FromFd` dups the
  descriptor into a Java one, the dup shares the underlying file description, and the mark lands
  on the socket .NET goes on to use. This was the step most likely to fail and it did not.
- **SignalR does not route its WebSocket upgrade through `HttpMessageHandlerFactory`.**
  `ConnectCallback/WebSockets` never reached the server — no log line at all. `ClientWebSocket`
  bypasses the custom handler, so a `ConnectCallback` binding cannot reach a WebSocket transport.
- **One connection carries both directions.** Every passing cell answered a client-to-server
  `Ping` on the same connection it was being streamed down. No second socket, no second binding.

**Server-sent events over `ConnectCallback`, not WebSockets over `ProcessBound`.** WebSockets was
the faster row on paper and is the wrong choice. `BindProcessToNetwork` is process-wide: for the
201ms it is held, every socket the app opens goes to Wi-Fi, including the Guild Wars 2 API calls
that need the internet. It is a real race with nothing forcing it closed, it has to be re-applied
around every reconnect, and SignalR's own reconnect would not know to do that. `ConnectCallback`
touches only SignalR's sockets, is attached to the connection so reconnect inherits it, and fits
the traffic: the compass is ten frames a second downward and almost nothing upward, which is what
SSE is. Client-to-server messages go as separate POSTs through the same bound handler, which is
why `Ping` answered.

`ProcessBound/WebSockets` stays written down as the fallback if per-message POST latency ever
matters. `ConnectCallback/WebSockets` is measured dead and is not a fallback.

**Two cells failed in a way that is not explained.** `ProcessBound` with SSE and with long polling
both reached the server — it logged `CONNECTED` and then a clean disconnect — and the client-side
handshake then failed inside its ten-second budget. The cause was not established. It does not
change the decision, and it is recorded here rather than guessed at.

**Kestrel runs beside the `TcpListener`, not instead of it.** The four existing endpoints work,
are covered by `BridgeProtocolTests`, and reach the phone through the one transport that provably
beats Android's routing table. Moving them would buy nothing and risk all of that. What ASP.NET
Core does cost is deployment: a framework-dependent publish now needs the ASP.NET Core runtime
rather than just the desktop one, which is a second install instruction on a bridge whose whole
pitch is "run this exe" — and it lands hardest on the Linux target, which is already published
self-contained from Windows because the test container only has .NET 8.

---

### The compass

A live screen: where the character is standing, where the objective is, and which way to turn to
face it. The first screen in the app that is driven by a held connection rather than a refresh,
and the only one that needs two sources that cannot be combined anywhere else — MumbleLink on the
PC for position and facing, the Guild Wars 2 API for where anything is. The phone is the only
device that has both.

**The needle is relative to the character, not to north.** Top of the dial is straight ahead, so
the reading is "bear left" rather than "the objective is at 47 degrees and you are facing 92".
The ring's cardinal letters turn with the character, which is what makes it read as a compass
instead of an arrow: without them a needle that moves because the player turned looks identical
to one that moves because the objective did.

**Orientation is the window's shape, not a sensor.** `CompassPage.Arrange` stacks the readout
under the dial when the window is taller than it is wide and puts it beside the dial otherwise,
off `SizeChanged`. No rotation-vector sensor, no permission, no battery cost on a screen that is
by definition left open. It was tempting to counter-rotate the rose as the phone turns in the
hand — a real compass does — but the game world has no relation to the real one, so the only
thing that reading would stabilise is the letters, and it would cost a sensor subscription on an
always-on screen to do it.

**Two kinds of target, and the difference is visible.** Hearts, waypoints, landmarks and vistas
come from `/v2/continents/{c}/floors/{f}/regions/{r}/maps/{id}` with real coordinates, and the
needle is filled brass. Anything the app cannot place better than "somewhere on that map" gets the
middle of the map, a hollow ember needle, and a sentence saying that is what it is.

For world bosses that was every one of them, for a long time, and the reasoning below is kept
because it is why nothing was invented in the meantime — **see `### Boss arenas`, which retires
it.** Hardcoding eleven arena positions from memory was the obvious alternative and is exactly the
thing this file exists to argue against. Matching arena sectors by name against the API found
only two of eleven — Godslost Swamp and Svanir's Dome — so the other nine would have been recall
presented as data. The middle of Frostgorge Sound is a true statement about where Frostgorge
Sound is; a spawn point with a decimal place would not be. The hollow needle still exists and is
still correct; it is just down to one boss.

**Coordinates are continent coordinates throughout.** MumbleLink's context publishes playerX and
playerY in the same space `/v2/continents` uses, which is the only reason a bearing is computable
at all — otherwise the character's position and the objective's would be two different kinds of
number. `Bearing` does the arithmetic and nothing else, so the sign conventions are pinned by
tests at each cardinal rather than discovered on a phone.

**A simulator, because the screen cannot be built without a character walking about.**
`GuildWarsCompanion.MumbleSim` writes the block the game writes — same mapping, same offsets,
compiled from the same `MumbleLink.cs` — so a character walks a circle in Queensdale and
everything downstream treats it as a live game. It defaults to the middle of Queensdale's
`continent_rect` so the landmarks around it are real ones.

What the simulator explicitly does **not** settle is whether the game's axis conventions match
what `GameState.HeadingRadians` assumes. A simulator written from the same assumption as the
reader agrees with it by construction and proves nothing. Standing in Tyria and comparing against
the in-game compass is the only check that counts.

**That check has now been made, informally.** Reported on 2026-09-18 from a live session: the
position and the facing both looked right against the game's own compass. It is an eyeball
comparison rather than a measurement, so be precise about what it does and does not buy. The
failure modes actually at risk here were gross ones — a mirrored axis, a quarter turn, a sign
flipped on Y — and every one of those would have been glaring while walking about. A small
constant offset would not have been. So: the convention is confirmed for sign and quarter, and
unverified to the degree. Nothing downstream assumes better than that; `Bearing.Describe` bands
the answer into four words precisely because the last few degrees were never the point.

**A radar warning receiver, not a single needle.** Every landmark in range is on the dial at once:
angle is bearing, distance from the middle is range, the centre is the character. That instrument
already solves this exact problem — several things at once, each needing a direction and a rough
range, readable in the half second somebody can spare mid-fight. On a real set the radial axis is
signal strength rather than distance, but a strong emitter is a near one, so the instinct carries
over unchanged.

The radial scale is logarithmic, which is not a flourish. A map is thousands of continent units
across while the things worth walking to are tens or hundreds away; on a linear dial the whole
neighbourhood collapses into the centre pip and only the far corners are legible, which is exactly
backwards. `RadarScale` compresses it and draws labelled rings at 125, 500, 2000 and 8000 so the
axis is a measurement rather than a mood. Marks past the outer ring are pinned to the rim and
dimmed: the direction is still true and the distance has stopped meaning anything.

**The symbols are the game's own.** `/v2/files` publishes the map marker art — `map_heart_empty`,
`map_waypoint`, `map_poi`, `map_vista` — and `MapIcons` fetches them once and caches them under
`FileSystem.CacheDirectory`. A player already knows what a heart and a waypoint look like from the
in-game map, so the recognition is free; anything invented here would have to be learnt. The cache
is not about bandwidth — five small PNGs — it is so the symbols still draw on a phone whose Wi-Fi
reaches the PC and nothing else, which is precisely the network this feature is used on. A missing
icon falls back to a dot rather than blocking the dial.

A world boss borrows `map_special_event`, because the game ships no world boss marker: in the game
a boss is an event on a map rather than a place.

**A map-centre target is suppressed on its own map**, and that was found by looking at the dial
rather than by reasoning about it. Shadow Behemoth spawns in Queensdale, so for a character
standing in Queensdale its map-centre fallback sat four hundred units away among the local
waypoints, wearing the same art as a real nearby target and looking entirely plausible. The
arithmetic was right and the answer was noise — "walk towards the middle of the map you are
already in". `CompassTarget.CanPointFrom` is the rule, and it has its own test, because the next
person to meet this will meet it as a screenshot that looks fine.

**The dial turns against the phone; the window never does.** The app is portrait-locked at the
activity, deliberately, because every screen in it is a column. The compass is not an exception
to that — instead `DeviceTilt` reads the accelerometer and `CompassRose` counter-rotates the
drawing, so a phone held sideways or propped on a desk shows an upright instrument inside an
unchanged portrait window. Nothing reflows, no layout has two cases, and the dial is a circle so
turning it can never push anything outside the area it was given.

Two details earn their keep. Gravity lying almost entirely through the screen — a phone flat on a
table — makes the in-plane angle pure noise, so below a threshold the last good angle is held
rather than followed. And the angle is eased the short way round, or the dial spins a full turn
every time somebody crosses the boundary at upside-down.

**Only two kinds of target can be hidden once finished, and that is the API's limit rather than a
choice.** Hero points come from `/v2/characters/{name}/heropoints`, keyed by the same ids the
floor endpoint uses; world bosses from `/v2/account/worldbosses`, which resets daily. **Renown
hearts cannot be filtered at all** — there is no endpoint. The character routes are backstory,
buildtabs, core, crafting, dungeons, equipment, equipmenttabs, heropoints, inventory, quests,
recipes, sab, skills, specializations and training, and map completion is in none of them; nor is
it on the account side. Waypoints, landmarks and vistas are in the same position. The screen says
so in a line under the dial rather than letting a player conclude the filter is broken.

`CompassTargets.Note` exists because the two ways this can come back empty — a key without the
scope, and an account with genuinely nothing finished — produce an identical empty set. Without
it the first run looked like a working filter with nothing to do, and there was no way to tell.
The journal line settled it: `Imothep on map 15: 0 hero points done; 0 bosses done today`.

**A subnet sweep on a reconnect loop killed the app, and it took three wrong guesses to find.**
Left open beside a stopped bridge, the process went from 39 threads to 1834 in three minutes and
died with `SIGABRT` — `shadow stack ... mprotect failed: Out of memory` on a `.NET TP Worker`,
which is a thread stack that could no longer be allocated. 1799 of those threads were named
`OkHttp Connecti`.

The chain: `BridgeFinder`'s last-resort step probes every host on the /24, each probe an
`HttpURLConnection` through `Network.OpenConnection`, and each of those leaves an OkHttp
connection thread alive for its pool's keep-alive. Behind a tap that is harmless — it happens
once and drains. The compass is the first caller that asks *in a loop*, so it is the first to
turn a survivable cost into a fatal one. `ResolveAsync` now takes `sweep`, and the stream passes
false.

Worth recording how badly the first attempts missed, because each looked convincing:

- **A leaked `HubConnection`.** Real — the failure path never disposed it — and fixed, but not the
  cause. Threads still climbed to 1665.
- **Re-searching on every failure.** Also real, also fixed, also not it: with the PC down there is
  never a last-good address to reuse, so every attempt fell through to a full search anyway.
- **Rationing searches to one a minute.** Cut the rate and nothing else. The leak was per search,
  not per attempt.

What finally settled it was not reasoning: it was `/proc/<pid>/task/*/comm` grouped by name, and
the journal line `Also tried every address on 192.168.137 directly` still appearing after the
sweep was supposedly off — because `sweep` was being dropped on the floor at
`return Current() ?? await SearchAsync(ct);`, which never forwarded it. Measure the threads, read
the log the code actually wrote.

After the fix: flat at 35-45 threads over four minutes with the bridge down, and it reconnects
within a minute of the bridge coming back.

**The screen follows the phone in two pieces, and neither of them rotates a layout.** Turning a
container was tried three times and does not work: MAUI applies `Rotation` after measuring, so a
turned container keeps the shape of the box it was given — the dial ended up squashed into a
corner with the text strung down the middle. Correcting it by swapping `WidthRequest` and
`HeightRequest` failed too, because every source for the "true" size fed back on itself once the
request was applied: the container reported its own swapped size, and a `Grid` host reported its
child's.

What works instead:

- **The dial and the readout are one canvas.** A canvas has no measured box, so the whole
  composition — rings, marks, trail, and the character, map and objective lines beneath — is drawn
  inside a single rotated frame and stays upright together. This is the only part of the rotation
  problem that never fought back, which is why the readout moved out of XAML and into
  `CompassRose.Readout`.
- **The rail never moves at all.** It is the bottom strip of the screen, which is the handset's
  USB edge, so it is a fixed place in the room: below the dial when the phone is upright, off to
  one side once it is turned. An earlier version walked it round all four edges following gravity,
  and that was worse — the controls appeared to wander off on their own, and upside down they
  climbed to the top.

  What changes inside the strip is the arrangement, so nothing ever runs past the edge: two rows
  of two while the buttons are wide, all of them abreast once they have been turned on their side
  and gone narrow. Each button and the readout sit inside a wrapper sized to the footprint they
  take up *after* rotation, because a rotated view still occupies its original box.

- **The readout lives in the rail, not under the dial.** It was drawn into the canvas first, which
  worked upright and hid the chosen objective the moment the phone was turned — below the dial is
  off the side of the screen in exactly the position somebody props a phone up in.

`SnappedOrientation` decides the quarter, with hysteresis at sixty degrees rather than
forty-five so a phone resting against a mug does not flip the rail back and forth.

The sign of the tilt took two wrong versions to settle, both invisible in a screenshot because
upright reads as zero under conventions that disagree about everything else. `DeviceTiltTests`
asserts all four quarters for exactly that reason.

**Verified on the phone against the simulator**, over the hotspot with LTE as the default
network: the screen reads "Imothep · Ranger" in Queensdale, lists Queensdale's own hearts and
waypoints nearest-first with distances, and the world bosses after them with countdowns. Picking
Scaver Waypoint drew a filled needle and "turn around, 673 away"; picking Claw of Jormag drew a
hollow one, "bear left, 10773 away", and the sentence about Frostgorge Sound.

---

### Port-then-walk

The compass answers "which way, and how far". Past a few hundred units that answer is true and
useless: nobody walks four thousand units across Queensdale. They open the map, click the nearest
waypoint to where they are going, and walk the last stretch. The dial's outer ring is eight
thousand, so the instrument was at its least useful exactly where it had the most to say.

So when the objective is far enough away, the readout carries one more line:

    Righteous Hoofmoot
    turn left
    3565 away
    via Godslost Waypoint · then 735

`PortRoute` is the whole of it, and it is pure arithmetic over coordinates that are already
loaded — a map's waypoints arrive in the same floor response as its hearts and vistas. No new
endpoint, no new request, no cache.

**Nearest to the objective, not to the character.** Porting costs the same loading screen from
anywhere on the map, so where the player happens to be standing cannot change which waypoint is
the right one to arrive at. A route computed from their position would also change under them as
they walked, which is the opposite of what a route is for.

**What the line refuses to claim, and why that is the design rather than a gap.** The API
publishes neither a waypoint's cost nor whether it is contested, and both decide whether the
advice can actually be taken. The claim is therefore kept strictly geometric: this is the closest
waypoint to the objective, and that much is left to walk from it. A player who ports and finds it
contested knows exactly what to do about it; a screen that had implied it was open would have
lied about the one thing it had no way to know. Same rule as the world boss needle two sections
up — say the true, smaller thing.

**Two numbers decide when the line appears, and both are needed.**

- **2000 units before porting is worth suggesting.** The dial's third labelled ring. Inside it the
  whole neighbourhood is walkable and a loading screen costs more than the walk it saves.
- **The waypoint must leave at most half the walk.** Without this the advice degrades into "port,
  then walk almost as far", which is worse than silence. A ratio rather than a second distance,
  because it also disposes of a case that would otherwise need a rule of its own: a character
  already standing at the nearest waypoint, where the remaining walk *is* the whole walk, the
  ratio fails, and nothing is said.

**Four cases where the honest answer is to say nothing**, each with its own test, because each is
a line the screen would otherwise show that reads as true and is not:

- **The objective is itself a waypoint.** "Port to the waypoint you picked" is not advice.
- **The objective is a map centre.** A world boss is placed at the middle of its map because the
  API has no arena coordinate. The nearest waypoint to that point is a precise answer about an
  imprecise one, and it would read as though the spawn position were known after all. See
  `CompassTarget.Precise`.
- **The waypoint is on another map.** A waypoint under the character's feet says nothing about a
  walk that begins two maps away, and the distance between the two would be arithmetic across a
  continent nobody walks.
- **The map has no waypoints in hand at all.** Which is, today, every map except the one the
  character is standing on — see below.

**Built once, shown per frame.** The waypoint and the walk from it are fixed by the objective;
only the decision to show them moves as the character walks. So the words are composed in
`Choose` and the frame loop does nothing but pick between that string and the empty one. This is
not premature: the dial repaints sixty times a second, and the memory that cost is already
written up under `MostBlips` — a string interpolated in that loop is a string allocated sixty
times a second.

**The waypoint is read from `AllRows`, not `Rows`.** A waypoint the player has hidden from the
dial is still the right place to port to. Hiding is a statement about clutter on the instrument,
not about where the map's waypoints are.

**The objective survives a map border, and that is where this earns the most.** Only the current
map's floor is loaded, so the obvious next step looked like fetching the objective's own floor to
get its waypoints. It is not, and the reason is worth writing down: every cross-map target on this
screen today is a world boss, every world boss sits at a map centre, and `PortRoute` refuses a map
centre on purpose. Fetching a floor for those would produce a precise-looking waypoint for an
imprecise point — the exact claim two paragraphs up exists to refuse. The request would have had
no honest consumer.

What does have one is the case that was being thrown away. The objective used to be cleared when
the character walked into a different map. That is precisely when porting advice becomes the only
useful thing the screen can say, and the data for it was already in hand: continent coordinates
are continent-wide, so the bearing and the range across a border are the same arithmetic as within
one, and the hop was worked out while the objective's own map was loaded. So nothing is dropped
any more.

Two consequences had to be handled where they arise rather than avoided by forgetting the
objective:

- **No row holds it.** The picker lists this map, and the dial is plotted from that list. `Plot`
  adds the objective to the ranking on its own when no row carries it, which is the same exemption
  the cut already made for a chosen target that ranks past twelfth.
- **A bare name is unplaceable.** "Righteous Hoofmoot" means nothing once you are two maps away,
  so `NameObjective` appends the map — "Righteous Hoofmoot · Queensdale" — and only when it
  differs from the one underfoot. Assigned on the two events that can move it, not in the frame
  loop.

The way to put a cross-map objective down is CLEAR OBJECTIVE, the same way as on its own map.

**Verified on the phone by moving the simulator between maps.** Standing in Queensdale: *Righteous
Hoofmoot · turn left · 3699 away · via Godslost Waypoint · then 735*. The same objective from
Wayfarer Foothills, ten thousand units away: *Righteous Hoofmoot · Queensdale · hard right · 9472
away · via Godslost Waypoint · then 735*, with the mark pinned and dimmed at the rim where the
range has stopped meaning anything. Before this it read "No objective chosen".

**Since closed:** this said a boss's arena had no coordinate anywhere in the API and that marker
packs were the only known source. Both halves were wrong — see `### Boss arenas`. Twelve of the
thirteen scheduled bosses now have a real position, so they take port-then-walk advice like
anything else.

**Verified on the phone against the simulator**, walking near Queensdale's north-west corner: the
readout shown at the top of this entry is the screen. The map centre was tried first and showed
nothing, correctly — its furthest objective is 1867 units away, inside the ring where walking is
the right answer. `PortRouteTests` pins the two thresholds, the four silences, and the wording.

---

### Boss arenas

The compass pointed at the middle of a boss's map for months, drew the needle hollow, and said so
in words — because `/v2/worldbosses` is a list of names and nothing under /v2 says where a boss is
fought. Two sections above, this file argues at length that the middle of Frostgorge Sound is a
true statement and an invented spawn point would not be. All of that was right, and it was
answering the wrong question.

**`/v1/event_details.json` is alive.** The v1 API is deprecated, frozen and largely forgotten, and
it still answers: 1.38 MB, 5417 events, no key, and every one of them carries a
`location.center`. It is ArenaNet's own data in ArenaNet's own API — so there is nothing here that
was recalled, guessed, or lifted from somebody else's marker pack, which is what the plan had been
five minutes earlier.

It was found by asking whether anything *other* than a marker pack had ever published this, before
writing a parser for a pack nobody had the licence to redistribute. The lesson is the cheaper one:
the deprecated half of an API is still an API.

**Two coordinate spaces, and one flip between them.** Event centres are in *map* coordinates —
local to one map, y growing north. Everything else here is in *continent* coordinates — one space
for the world, y growing south. `/v2/maps/{id}` publishes both rectangles, `map_rect` and
`continent_rect`, which is the whole of what makes this convertible: a linear fit of one rectangle
onto the other, and a flip. `MapSpace` does it and nothing else. The flip is the part worth
staring at, for the same reason `Bearing`'s is — getting it wrong mirrors every boss
north-to-south without making any single number look wrong.

**Checked against something independent, not against the arithmetic that produced it.** Shadow
Behemoth's centre converts to (44826, 30045). That is 207 units from Deathroot Shack and 422 from
the Temple of the Ages, both read from the floor endpoint: the Godslost Swamp, which is where it
spawns. A mirrored y would have put it in the fields north of Beetletun and looked exactly as
plausible. The test asserts the distance to both landmarks rather than the coordinate, so it is
checking a claim about the world rather than restating the formula.

**Twelve of thirteen.** Every scheduled boss has one unambiguous named event except two edge
cases, and both are recorded rather than smoothed over:

- **Claw of Jormag** has three events by that exact name, two of them in Bjora Marches. The row
  carries its map id and is refused for any other, so the schedule's Frostgorge Sound is what
  answers.
- **Karka Queen** has four, all on Southsun Cove, all with different centres — she is fought at
  whichever settlement she attacks, and which one is not known until she does. Picking one would
  be a precise answer to a question that has four, so she is **deliberately absent** and keeps the
  map-centre fallback. The hollow needle earns its keep on exactly one boss now.

**Baked, not fetched.** v1 is frozen, so a request per session would spend a megabyte and a half
re-reading numbers that cannot change. The table is thirteen lines with the event id against every
row, and the endpoint is written down, so any of it can be checked again.

**What it changes on the screen.** A boss is now `Precise`, which is not cosmetic: it earns a
filled needle, a distance that means the arena rather than the middle of a map, port-then-walk
advice — which `PortRoute` refuses for a map centre on purpose — and it stops being suppressed on
its own map by `CompassTarget.CanPointFrom`. That last one is the visible one: standing in
Queensdale, Shadow Behemoth used to answer "you are on this map" and draw nothing.

**Verified on the phone**, standing in Queensdale: *Shadow Behemoth · hard right · 1098 away*,
with the mark ringed in brass on the dial and a filled needle to it. No port advice, correctly —
1098 is well inside the ring where walking is the answer.

**The map says what the objective is, not just which dot it is.** Ringing the chosen marker and
stopping there meant picking a chest on the map and then switching to the dial to find out how far
away it was — two screens sharing an `Objective` and only one of them answering for it. The panel
now carries the distance, the direction and the port-then-walk line, from the same `PortRoute` and
`Bearing` the compass uses.

**A point of the compass, not "bear left".** The dial says which way to turn because it is read
while walking and the target cannot be seen; the map is north-up with the marker on it, so
`Bearing.Cardinal` names the direction — "3507 away · south-east" — and the eye can check it
against the screen. Eight points, because nobody navigates by north-north-east off a phone. It
needs no heading, which is the other reason it suits a map: the dial's reading depends on which
way the character faces and this does not.

Verified on the phone with the character moved to Lowland Shore's north-west corner: *Chest ·
3507 away · south-east · via Astral Ward Moon Camp Waypoint · then 546*. The direction was checked
through the quarter turn as well — the marker sits up-and-right on a rotated framebuffer, which is
south-east in the map's own space.

**Still absent, and now the only thing a marker pack would add:** chests and jumping puzzles. They
are not events, so v1 has nothing to say about them, and there is no endpoint anywhere. That is a
pack, a parser, and a licence question per pack — and it needs a pack file that this machine does
not have.

---

### Marker packs

Chests and jumping puzzles are the two things left that no endpoint publishes. They are not
events, so the v1 data that answered the boss arenas has nothing to say about them, and nothing
under /v2 ever did. A community marker pack is the only published source, so the app reads one.

**Downloaded, not shipped, and that is a licence decision before it is a size one.** A pack is
somebody else's work published on their own terms. Fetching the file a player points at is
ordinary use of a published URL; putting a copy inside the APK is redistribution, and would need
a per-pack licence this repo has no way to check. So the address is a setting, nothing is bundled,
and an install with no pack behaves exactly as it did before — which is also the state every
install starts in.

**What is kept is not the pack.** A `.taco` is a zip of XML and a great deal of texture, and runs
to hundreds of megabytes. It is streamed to disk, read once, and deleted. What survives is the
chests and jumping puzzles got out of it, as tab-separated lines — five fields, four of them
numbers, a few hundred kilobytes, and readable in a text editor when somebody asks why a chest is
in the sea. In `FileSystem.AppDataDirectory` rather than the cache: the cache is the directory
Android empties when it likes, and re-spending a hundred megabytes of somebody's mobile data
because of that would be rude.

**Stored raw, converted late.** A marker is kept in the pack's own metres against its map id, and
turned into continent coordinates only when that map is the one being looked at. Converting at
import would need `map_rect` for every map a pack mentions, which is a request per map for a pack
that may name two hundred; the map being stood in has already been fetched, so the conversion is
free at the point it is wanted.

**Read leniently, because packs are not written to a schema.** There isn't one. Attribute casing
varies between files written by the same tool — `MapID`, `mapid` — so attributes are matched
case-insensitively; a document that will not parse costs itself and not the other twenty-nine in
the pack; and every number is read in the invariant culture, because a pack writes `-12.5` and a
phone set to German reads that as minus one hundred and twenty-five, which is a marker a continent
away from a file that was perfectly correct.

**A category means what its words say, and nothing else is carried.** There is no registry saying
which category is a chest — only the words the author chose, in a dotted path like
`tw.chests.hot.chestone`. So the whole path is matched, not the last segment, and `jp` is matched
only as a whole segment because two letters turn up inside words that are not it. Trails, routes,
solutions and openers are excluded by name: the way to a chest is not a chest, and without that
they arrive as chests standing in the middle of a wall. **Anything this app cannot place is
dropped** — hero points, vistas and waypoints because the API publishes those properly, and the
rest because a target whose kind is a guess is worse than no target at all.

**One assumption, reasoned out, implemented backwards, and then measured.** A pack stores avatar
space: metres, x east, y up, z south. Map coordinates are inches, x east, y north. So map y is
minus z — which is what the code did, and it is wrong. The flip from north-growing to
south-growing already lives in `MapSpace.ToContinent`, where the boss arenas proved it; applying
it a second time on the way in cancelled it, and put every marker on the wrong side of its map.

**The check that was supposed to catch it does not.** The plan was that a mirrored map would throw
its markers outside the map rectangle, where an import-time test would notice. Measured: 329 of
Queensdale's 334 markers stay inside when north and south are swapped, because a map rectangle is
roughly symmetrical about its middle and markers cluster centrally. The check is still worth
having — it catches junk, markers in the wrong unit, a pack's own mistakes — but it is not a check
on the sign, and the comment claiming it was has been corrected.

**What settled it was distance to published landmarks.** Teh's Trails, a real pack, 9264 markers,
against the coordinates the floor endpoint publishes for the same maps:

| | markers within 40 units of a landmark | median distance |
|---|---|---|
| `+z` | Queensdale 114/334, Metrica 161/340 | 72, 41 |
| `-z` | Queensdale 9/334, Metrica 18/340 | 177, 119 |

Scattering the same markers at random over Queensdale would give a median of about 230, so `-z`
was indistinguishable from noise and `+z` is plainly the answer. A trail marker is not *at* a
landmark, which is why the test is a distribution rather than a single distance — but a 2 to 3
times difference across four maps is not ambiguous.

This is the second time in this file that a coordinate convention has been settled by measuring
rather than by reasoning, and the second time the reasoning was confident and wrong. The
conventions are documented; how a given file applies them is not.

**Verified end to end on the phone.** Metal Marker Myriad, 7.1 MB, pasted into the setting and
downloaded on the handset: *106 markers across 17 maps* — which is exactly what an independent
reading of the same file in Python produced, marker for marker. With the simulator moved to
Lowland Shore, the compass lists six jumping puzzles at 64 to 349 units and a chest, and picking
one reads *Lowland Shore · Jumping puzzle · bear left · 311 away* with the mark ringed on the dial.

The parser had already been run against a second real pack — Teh's Trails, 4.27 MB, 803 entries,
2 XML documents, 9264 markers — which is what settled the coordinate question above. It carries
trails rather than chests, so it imports nothing, which is itself the right answer.

**Chests and jumping puzzles are dots, deliberately.** /v2/files publishes 116 assets and none of
them is either thing. The closest candidate is `map_adventure`, and borrowing it would be worse
than a dot: an adventure is a different game feature with its own marker in game, so the icon
would be saying something untrue. A world boss borrows `map_special_event` because a boss
genuinely is an event; there is no honest substitute here. So they are told apart by colour —
`TargetDot`, shared by the dial and the map so the two cannot disagree — and the row in the list
carries the word.

**Several packs, and one update button.**

No single pack covers the ground, which is not a guess: Metal Marker Myriad and Hero's Marker Pack
both carry chests on Lowland Shore and Janthir Syntri, and each has ones the other has not. TacO
assumes a handful are enabled at once. So the setting is a list — add by URL, and each row carries
UPDATE and REMOVE.

Each pack gets **its own file**, named by a hash of its URL. Removing one costs deleting one file
rather than rebuilding what the others contributed, a failed download cannot damage a pack that
already worked, and adding the same address twice replaces it instead of accumulating copies. A
hash rather than a counter because it has to be the same name on the next run.

**Updating is free when nothing changed.** The server's `ETag` and `Last-Modified` are kept from
the last fetch and sent back as `If-None-Match` and `If-Modified-Since`; an unchanged pack answers
`304 Not Modified` with no body. Checked against a real pack URL before any of this was written —
both validators answer 304, zero bytes — so checking a seven-megabyte pack costs one request.

This is deliberately *not* what TacO does. Its own `MarkerPacks.json` carries `versionurl`,
`versionsearchstring` and `versionterminator` per pack: it scrapes a version string out of a web
page. That needs configuration for every pack and breaks whenever a site is redesigned. HTTP
validators need none and work for any host that sets them. A host that sets neither simply
re-downloads, which is correct and merely not free.

**Two packs describing the same chest is the normal case, so the threshold was measured.** Where
Metal and Hero mark the same in-game object they agree to within a unit — the closest pairs are
0.1 to 0.7 apart, because both authors stood on it. Then a gap: on Lowland Shore 8 pairs fall
within 10 units, 9 within 25, and still only 9 at 100. Past about 200 the count climbs again, and
that climb is distinct chests in the same farming spot being matched to each other — at 400 units
it would merge 21 of 32 markers, which is data loss wearing deduplication's clothes.

Twenty-five units, in the flat part of that curve. Only within a kind, because a chest and a
jumping puzzle at one spot are two things worth saying and a farming route often has both. First
pack added wins, so which copy survives is at least predictable.

**A note on the published list.** `MarkerPacks.json` in `BoyC/GW2TacO` is the curated index TacO
itself updates from, and it is the right place to look for a pack. Two of its entries do not
answer a plain HTTPS GET: `tekkitsworkshop.net` returns 403, and the `[fast]` entry returns the
site's HTML rather than a file. The GitHub-hosted packs answer normally. A download inside the app
meets the same walls, so the setting's failure message is whatever the server said rather than a
guess.

**Tekkit's 403 is not about the user agent, and the app does not pretend otherwise.** It is the
pack with the best chest and jumping-puzzle coverage, so it was worth an experiment: it answers
403 to a request with no agent, to `GuildWarsCompanion/1.0`, to a full Chrome string, and to one
carrying its own download page as the referer. Whatever the block is — hotlink protection, an edge
rule — it is not something an honest client can talk its way past. The app now sends its own name,
because some hosts refuse a request with no agent at all and naming yourself is ordinary manners
when fetching somebody's file; it does not send a browser's, which would buy nothing except a lie
in a header.

**Updating every pack is one button**, because a check that costs one request each is not worth
making the player do three times. Sequential rather than parallel — these are other people's
servers and the answer is usually 304 in a moment. The result is said plainly rather than as a
single number: "1 updated · 1 already up to date" rather than "2 packs", because the second phrasing
hides a failure.

---

### Routes

The other half of a marker pack, and the half this app had been ignoring. A `.taco` carries `POI`
elements — chests, jumping puzzles, the things `### Marker packs` imports — and `Trail` elements,
each pointing at a binary `.trl` beside it in the zip. Teh's Trails is almost entirely the second
kind: 490 trails and not one chest.

**The format was measured, not looked up.** All 526 `.trl` entries in Teh's Trails begin `int32`
version 0, then `int32` map id, then a whole number of 12-byte groups of three little-endian
`float32` — x, y, z in the same avatar-space metres the markers use. Not one had a byte left over.
54338 points across 52 maps, a median of 45 per trail and 926 at the longest. A (0,0,0) triple is
the pen lifting rather than a point: the first trail read had one, and drawn as a position it would
run every route through the north-west corner of the map. Height is dropped, as it is for markers.

**Routes are their own kind, not markers.** A marker is a place to point at; a route is a shape to
follow, has no single position, and a compass needle aimed at one would mean nothing. They share
`MapSpace` and nothing else. Two packs routing the same map are also *not* deduplicated, unlike two
packs marking the same chest — those are two opinions about how to walk it, and merging them by
proximity would draw a line neither author intended.

**Off until asked for, which is the opposite of the markers.** Teh's Trails files 22 categories and
several cover the same zone; all of them at once is a thicket over the artwork the map exists to
show. Nothing is drawn until a category is chosen, the choice is remembered, and the chooser lists
only the categories this map actually has — a list of twenty rows where two do anything is a list
that wastes the tap it takes to read it.

**The pack's own words, from the tail of the path.** A trail is filed under `tt.mc.mt.th.t`, which
says nothing; the pack's `MarkerCategory` elements carry a `DisplayName` per segment, so the path
can be read back in the author's language. Written out in full on the phone, every row began
"Teh's Trails - Map Completion · Map Completion ·" and the six categories differed only past the
point where the line truncates. The last two segments are what tell them apart. Separator
categories — Teh's Trails has one displayed as `|=====Core Tyria=====|` — are dropped rather than
shown.

**Thinned at fifteen metres, because a route is drawn at walking resolution.** 50339 raw points
became 29983 with the same shape; at the zoom where a whole map fits, a metre is far under a pixel.
A plain distance filter rather than Douglas-Peucker: a walking route has no long straight runs for
a real simplifier to collapse, so the cheap rule keeps the same line for a fraction of the code.
The last point is always kept — a route that stops short lies about where it goes.

**Verified on the phone against an independent count.** Teh's Trails, 4.27 MB, added in the
setting: *1334 routes*, which is exactly what a separate reading of the same file in Python
produced, run for run. Queensdale offered six categories — 5, 19, 10, 2, 1 and 1 runs — and turning
on Normal Trail drew the map-completion route across the zone, confirmed by diffing the screen
against the same frame with nothing chosen.

That diff was worth running twice. Heart Zones and Hero Point Trails looked like nothing had
happened, and both were in fact drawn correctly: a heart zone is a loop a few metres across and a
hero point trail is a hop from the nearest waypoint, neither of which is visible at the zoom that
fits a whole map. The feature looked broken and was not, which is a thing worth knowing before
changing code to fix it.

**A trails-only pack used to report "0 markers · 0 maps"**, which reads as a failed import of
exactly the thing it succeeded at. Both halves are named now, and only the halves that exist.

**The drawing was measured too, and the measurement corrected itself.** Routes are the first thing
on this screen that builds geometry per frame, and per-frame work has killed this app once — see
the compass. Fifteen minutes of panning and zooming with two categories drawn: native heap +105 KB
a minute against +285 with no routes at all, PSS steady at 375-382 MB, `Graphics` flat at about
62 MB, and a 94 MB release of `GL mtrack` at minute three that was reclamation rather than a leak.
Memory says routes cost nothing it can see.

Memory is the wrong instrument for per-frame work, though, so `dumpsys gfxinfo` was asked next. It
first reported 32.65% janky frames with twelve routes drawn against 23.97% with them off — which
bought the obvious fix: build each path once in tile-grid coordinates, which do not move under a
pan, and translate the canvas instead. Only a zoom rebuilds anything.

**Then three runs of each condition said something more honest.** Routes on: 22.40%, 24.73%,
26.65%. Routes off: 27.49%, 30.38%, 30.92%. The 90th percentile is 16-18ms either way. This
handset's jank figure moves about four points between identical runs, so the single run that
started this was weaker evidence than it looked, and the claim has been narrowed in the source to
match: the cache costs nothing measurable and removes the per-frame allocation of up to fifty paths
of several hundred points each. It is kept on that basis rather than on a number that did not
survive being repeated.

A useful reminder that one run is an anecdote — the same lesson the map's own memory soak taught,
arrived at from the other direction.

**The pack's own colours, and two packs that disagree about everything.** `<Trail>` may carry a
`color`, and `MarkerCategory` may carry `color` and `alpha` which everything under them inherits.
Measured: Metal Marker Myriad colours 251 trails in nine values — the Okabe-Ito colourblind-safe
palette — and four categories; Teh's Trails colours nothing at all and instead fades seven
categories with `alpha`. So a route takes its trail's colour, else the nearest category on its path
that states one, with the nearest stated alpha applied, else this screen's brass. The inheritance
is recovered from the dotted path rather than from the tree, the same trick the display names use.

Read leniently, because the wild is already broken: Metal carries `#fffffff`, seven hex digits,
which is not a colour in any reading. Unparseable means unstated — the route is still worth drawing
— and an `alpha` outside 0 to 1 is refused rather than clamped, because a pack writing 255 means
something this reader does not understand.

**Three traps, all the same shape, all found by doing it.** A pack that has not changed answers
`304 Not Modified`, so anything the app learns to read *later* never arrives:

1. Bumping the stored route file to `routes 2` for the colour column would have emptied every
   pack already on the device, with no way back but removing and re-adding each one. Both versions
   are read; a missing column is a missing colour.
2. Packs imported before routes existed — Metal among them — would have answered 304 forever and
   never grown routes at all. `MarkerPackRecord` now records which import version read a pack, and
   the validators are withheld when it is behind, forcing one full fetch.
3. Raising the route ceiling changed what an import collects, so the same number had to move
   again. It has now earned itself twice in one sitting.

**The ceiling was found by the count looking wrong.** Metal imported "4000 routes", which is the
shape of a truncation rather than a measurement — it carries 5409 trails and a trail splits into
several runs. At about 880 bytes a route the ceiling was costing 3.5 MB to hide the rest, so it is
20000 now, matching the markers', and Metal reads 5299 routes in 4.96 MB. A count that lands
exactly on the limit now says "the most this app keeps" rather than passing itself off as a total.

**Verified on the phone.** Lowland Shore, whose 30 trails include 14 coloured ones: sector
boundaries drawn in the pack's gold, mastery-insight trails in its blue, against the brass this
screen uses when a pack says nothing.

**The panel was rebuilt after the screenshots showed what it had become.** Three faults, all mine,
all from adding controls one at a time to a layout that assumed a fixed set.

The buttons were cut into rows by hand, and they appear and disappear with the state — FOLLOW
CHARACTER only with a pinned map, ROUTES only where a pack has any, CLEAR OBJECTIVE only with an
objective. So the rows re-cut themselves underneath the player: a button on the second row a
moment ago is on the first now, under a thumb already moving. They are one wrapping flow in a
fixed order now, which also cannot push anything off the edge. The automation driving this screen
mis-tapped repeatedly for exactly this reason, which is as good a usability finding as a person
complaining.

The route chooser had no height bound, and a pack files thirty categories on one map — the list
grew the panel until it covered the map entirely. A chooser that hides the thing being chosen for
is not a chooser. It is 240 units and scrolls; the map stays the larger half.

CLEAR OBJECTIVE sat there with nothing to clear, which teaches somebody that a control does
nothing.

**And shortening the category names to their last two segments introduced an ambiguity that the
screenshot caught.** Two branches of one pack ending in the same two segments gave two rows
reading "Main Trail · Normal Trail (Disable if using trail highlighting)", 16 runs and 12 —
identical, so whichever was tapped was wrong half the time. `RouteNames.Distinguish` lengthens only
the names that clash, and only as far as it takes: those two now read "Lanterns (BHUD Only) · …"
and "Map Completion (BHUD Only) · …" while every other row keeps the short form. Where two packs
still collide the pack's own name goes in front, and only when more than one pack has routes on
that map — otherwise the prefix repeats on every row and says nothing.

**The picker's search box keeps its term and selects it.** Keeping it is right — coming back to
refine "gorge" beats retyping it — but a filter somebody has forgotten about is a list that lies
about how many maps exist, and typing appended to it: "snowden" into a box still holding "gorge"
searched for "gorgesnowden" and answered, correctly and uselessly, that no map is called that. The
whole term is selected when the box is tapped, so the first keystroke replaces it while the text
stays visible as a reminder. The selection has to be dispatched rather than set inside the focus
event: Android moves the caret to the tap position as part of focusing, and undoes anything set
before it. Verified on the phone — "gorge", open Frostgorge, back, type "snowden", and the box
reads snowden.

---

### Boss reminders on the PC

The phone already says a world boss is close. During a session that is the wrong screen: the phone
is propped up showing the compass and the player is looking at the monitor. The bridge runs on that
monitor, already knows the time, and now says it there too.

**It deliberately says less than the phone's does.** `BossAlerts` only mentions bosses the account
has not killed today, because the app holds an API key and can ask. The bridge holds no key and no
account — by design; it is a thing that reads shared memory and answers a socket — so it cannot
filter by what is done. It says what the timetable says and does not pretend to know more.

**Ten minutes, not the phone's thirty-five.** That window is sized around a background poll
Android runs when it feels like it, so it has to be generous or an appearance falls between two
polls and is never mentioned at all. A desktop process checking every twenty seconds can afford to
be exact, and ten minutes is the useful number in game: long enough to finish what you are doing
and get there, short enough to still be true when you arrive.

**One at a time.** The phone allows two because a notification shade stacks them; a panel on top of
a fight does not, and two of those would be worse than missing one.

**Only while the game is live.** The whole argument for saying this on the PC is that the player is
looking at the PC. If Guild Wars 2 is not running they are not, the phone's own reminder is the
right one, and a panel over somebody's spreadsheet would be the app being rude about their
afternoon.

**A window of our own rather than a system notification, and that is portability rather than
taste.** Avalonia's `TrayIcon` has a menu and a tooltip and no balloon; a real Windows toast wants
a packaged identity this app does not have; neither exists on Linux, which the bridge also targets.
One borderless topmost window works on both and adds no dependency. It sets `ShowActivated="False"`
and stays out of the taskbar, because the one thing worse than missing a boss is swallowing a
dodge — the panel must never take a keystroke from the game. Twelve seconds, or a click.

**The timetable is compiled from the app's copy, not duplicated.** `EventSchedule` is hardcoded
prose with no dependencies, so the bridge links the same file the phone and the tests use. Two
copies of a timetable is two things to edit and one of them to forget.

**And a switch to turn it off**, in the window beside the tray setting: *Tell me when a world boss
is ten minutes away*. Defaulted on through a nullable — a settings file written before this
existed has nothing to say about it, and the answer for somebody who has never been asked is the
feature working. Off is a deliberate act, so it is written to `bridge.json` and survives a restart;
turning it off mid-fight should not have to be done again tomorrow. Both states go in the journal,
because "it stopped telling me" and "I turned it off" look identical a week later.

The phone's own reminder is untouched by it. They are separate promises to separate screens, and a
player who mutes the panel over the game has not asked to stop being told on a train.

**Verified with the simulator running**, which is what makes the game look live:
`boss  Shadow Behemoth in 10 minutes — Queensdale`, one line in the journal at the moment the panel
appeared, and nothing repeated on the twenty-second checks that followed.

---

### The map

The game's own map, on the phone, with the character on it. The compass answers "which way, and
how far" in the half second somebody can spare mid-fight; a dial cannot answer "what is around
here, and where am I in it", because a dial has no shape. This is that screen.

`tiles.guildwars2.com/{continent}/{floor}/{zoom}/{x}/{y}.jpg` serves the artwork the in-game map
is drawn from, as 256-pixel squares. No key, nothing about the account, so it stands exactly where
`/v2/files` and the marker icons stand — and like them it is the game's own art, so a player
recognises the map they are standing in without learning anything. `continent_id`,
`default_floor` and `region_id` already arrive on `/v2/maps/{id}`, which the compass fetches
anyway, so the whole screen costs one new request: `/v2/continents/{id}`, for two numbers.

**Its own screen, not part of the compass, and that was decided before a line was written.** The
dial is an instrument on a held stream repainting at display rate. A map is panned, looked at, and
left alone. One page holding both would put a tile cache next to the redraw loop that has already
cost this app a process — see the `MostBlips` note above — and the two have no reason to share a
lifetime. They share an objective instead: `Objective` is a singleton both screens read and write,
so tapping a marker on the map is the same act as picking a row on the dial. Verified on the phone
in both directions.

**Continent coordinates are pixels — at the reference zoom, which is not `max_zoom`.** This is the
one piece of arithmetic the screen rests on, and the first version of it was wrong in the most
expensive way available.

`/v2/continents/1` says Tyria is 81920 x 114688 with `max_zoom` 8. Reading 8 as the level at which
a continent coordinate equals a pixel puts the middle of Queensdale — (44416, 29248) — on tile
`1/1/6/43/28`. **That tile exists.** It is 7697 bytes of perfectly good JPEG, it downloads without
a warning, and it is the open sea. Every index the screen asked for was one zoom level out, so
nothing errored, nothing retried, and the phone drew a screen of empty ocean with every marker in
exactly the right place on top of it.

Seven is the reference, and it was established by fetching six tiles and looking at them: the same
coordinates give `2/5/3` — which is Divinity's Reach, and a round walled city is not somewhere
else — then `3/10/7`, `4/21/14`, `5/43/28`, `6/86/57` and `7/173/114`, all Kryta, all land. Zoom 8
over this map answers `NoSuchKey`, so `max_zoom` is the deepest level the service will ever serve
rather than the level coordinates are quoted in. `MapViewModel` passes `max_zoom - 1`;
`TileGeometryTests` pins all six indices and the off-by-one itself, because the failure mode here
is a screen that looks plausible rather than an exception.

**Two coordinate systems, kept apart on purpose.** The tile service speaks pixels; MAUI speaks
device-independent units, of which this phone has about three per pixel. Everything the view model
holds is in tile pixels and `MapCanvas` divides by the display density exactly where it draws.
Drawing a 256-pixel tile as 256 MAUI units would blow the map up threefold and blur every label on
it.

**Tiles are stamped, not drawn.** `ICanvas.DrawImage` was measured as the whole of the compass's
memory leak, and a map holds forty images rather than a dozen — so tiles go through the same
`SymbolStamp` path the dial's marks do. It lands each tile on exactly 256 device pixels, which is
also what keeps the artwork sharp instead of resampled.

**The memory ceiling is a design decision, not a hope.** A tile is seven kilobytes on the wire and
a quarter of a megabyte decoded, and this screen holds around forty at once. So `Gw2Tiles` is told
after every frame which tiles were actually used and drops the rest, the page lets go of all of
them on `OnDisappearing`, and there is a hard cap of ninety-six on top for the frame where a zoom
change asks for a new grid before the old one is released. Disk caching is separate and permanent:
a tile is immutable, so a second fetch could only repeat the first, and a phone whose Wi-Fi reaches
the PC and nothing else still draws a map it has seen.

A missing tile is an ordinary answer, not a fault. Not every index inside the grid is rendered, so
a 403 or a 404 is remembered for the run and the background is left showing through; anything else
— a timeout, no network — is not an answer and is asked for again.

**The first eviction policy was correct and horrible.** It dropped everything the last frame had
not drawn, which is the most obvious way to bound a cache and the worst way to pan: moving a tile's
width threw away the row that had just left the screen, so coming back re-decoded it, and the map
visibly tore itself down and rebuilt along its edges as the finger moved. Reported from the phone
as "the area which is disposed or streamed in when panning is very visible", which is exactly what
it was.

It is least-recently-used now. The screen says which tiles it *used* rather than which it wants
kept, that stamps them, and a tile is only dropped when there is no room for a new one. Nothing is
thrown away while there is space to hold it, and the ceiling is unchanged — `MostHeld`, ninety-six,
about 24 MB decoded. That number now has a second job: it has to be comfortably more than one
frame asks for, which is about seventy at the deepest zoom on this handset, or the cache would
evict tiles it is about to be asked for again and thrash.

**And a ring of tiles past the edge of the screen, fetched but not drawn.** A tile was only ever
requested once it was already needed, so panning revealed blank squares that filled in a moment
later. One tile of lead time in every direction is the difference between a map that slides and a
map that is assembled in front of the player, and it costs one row and one column of a
ninety-six-tile cache. The ring is kept as well as fetched — telling the cache a tile just off the
edge is unused is the same mistake as dropping it.

**The controls sit on their own plate rather than over the map.** They were transparent over the
tiles, and the tiles are painted artwork: brass text on Queensdale's grass is legible, on its
cliffs is not, and which one it is changes as the player pans. A control whose readability depends
on where the map has been scrolled to is not a control. Obsidian and a brass rule, the same two
things every other panel in the app is separated by, so the map ends where the map ends.

**Two bugs found by looking at the phone rather than by reasoning**, both of which a test would
have been unlikely to catch:

- **The zoom never fitted.** `Fit` runs from the page's layout, which measures before the map has
  loaded, and nothing called it again afterwards — so the screen sat at whatever the field was
  initialised to. The viewport size is now kept, and whichever of the two arrives second calls the
  other.
- **A tap on a marker did nothing.** The hit radius was thirty pixels against a marker drawn about
  sixty across, so hitting a heart squarely missed it. Sized against what is actually on screen
  now, rather than against a feeling about fingers.

**Verified on the phone against the simulator**, in Queensdale: the map draws with Divinity's Reach
at the top, the markers sit on the roads and villages they belong to, the character's ring tracks
the walk, and the whole map fits the screen at zoom 5. Tapping a heart set the objective to "Help
Lexi Price protect the trade route", and the compass — switched to afterwards — read exactly that,
with "bear left, 426 away". Picking "Assist the Seraph at Shaemoor Garrison" on the dial and going
back to the map showed it there.

**It turns with the phone, a quarter at a time.** The window stays portrait — the app is locked
that way at the activity — so a sideways phone is handled by turning the canvas underneath an
unchanged window, the same trick the dial uses. What is different is that the map **snaps** rather
than easing. A needle is read while it moves, so it has to follow continuously; a map is read while
it is still, and a map at twenty-three degrees is a map whose labels cannot be read. Quarters also
keep the tile grid axis-aligned, so nothing is resampled. `SnappedOrientation` is shared with the
compass rail, hysteresis and all, and the tilt is polled five times a second rather than thirty —
the only thing it can produce is a quarter turn, and that happens when somebody picks the phone up.

A turn re-fits as well as redrawing: a portrait map and a landscape one hold different amounts of
the world, so the zoom that fitted one need not fit the other.

**The turn is one piece of arithmetic in `QuarterTurn` because three places have to agree about
it** — the canvas turns, a tap turns back, and a drag turns back — and none of them can see the
others. When they disagree the map looks perfect and answers the wrong marker.

They disagreed. **The screen's y grows downwards, and quarters 1 and 3 were inverted because of
it.** `ICanvas.Rotate` applies the ordinary rotation matrix, which in a y-down space turns
clockwise: a canvas turned by -90 sends a map point (x, y) to the screen at (y, -x), so the inverse
is (-y, x) — not the (y, -x) that falls out of a y-up derivation. Backwards, it composes with the
real turn into a half turn, which in the hand is a map that pans away from the finger. Found by the
user on the phone within a minute of trying it, which is the only place it is visible: upright it
is exactly right, and the tests that existed all passed. There is now one that walks a drag down
the screen through all four quarters.

**Pinch zoom, in whole levels.** The service renders discrete zooms and anything between them has
to be scaled from one of them — a blurred map, which defeats the point of using the game's own
artwork. So a pinch accumulates its scale, multiplied rather than added because a level is a
doubling, and spends a level each time it has earned one. A fast pinch can cross two levels between
two reports, so it is a loop rather than an `if`.

The zoom is taken **about the fingers, not the middle of the screen**, or the map feels like it is
fighting the hand. `TileGeometry.Recentre` moves the centre so that whatever was under the fingers
is still under them at the new scale, and its tests state that as the property rather than
asserting a computed number — which would be the formula written twice.

Bounded at both ends: the fit is the floor, because past it the map is a stamp in an empty screen,
and `Reference` is the ceiling, because above it the service has nothing and every tile comes back
NoSuchKey.

**A pad of buttons, because the pinch was right and still not what anybody reaches for.** Tried on
the phone and reported back: it works, and it is not intuitive. That is not a bug in the gesture —
it is that a map being read one-handed, with a game running on the other screen, wants a thing to
press. The game's own map steps from a pair of buttons, and steps are what this screen has anyway:
the tile service renders discrete levels, so a continuous pinch was always being quantised behind
the player's back. The pinch stays, because it costs nothing now that it exists and some people
will reach for it.

So: plus, the level, minus, stacked against the map's right edge. Two things about it are worth
recording.

**It is drawn into the canvas rather than laid out as buttons, and that is what makes it turn.** A
view would sit in the page's layout, which never rotates — so a sideways phone would have the
controls running down what the player sees as the bottom edge, which is exactly the problem the
map's own rotation exists to solve. Inside the canvas it is subject to the same quarter turn as
everything else, and `ZoomPad` hands the same rectangles to the drawing and to the hit test, for
the reason `QuarterTurn` is shared: two files that cannot see each other must not be able to
disagree about where a button is.

**The level counts from the whole map, not from the service's zoom.** "Zoom 7" means nothing to
anybody; "2/3" says how far in you are and how much further you can go. The pad reads the limits
from the same clamp the zoom uses, so a button at the end of its range dims rather than
disappearing — a control that vanishes takes the other one with it in the player's head, and then
they stop looking for either. The level gets the same backing plate as the buttons, because brass
on bare terrain is unreadable over half of Tyria and it is the one part of the pad that is read
rather than aimed at.

A button zooms about the **middle** of the screen where a pinch zooms about the fingers. A corner
button has no point it is holding still, and holding the corner still would throw whatever the
player was looking at off the far side of the screen.

**Verified on the phone**, sideways: the pad draws turned with the map, minus dimmed at the fit
because there is nowhere further out to go, plus stepping to `2/3` with the map redrawn about its
middle, and minus stepping back to `1/3` and dimming again.

**The screen stays awake**, the same window flag the compass sets and for the same reason: it is
propped against something while the player has both hands on the game, so the ordinary rule that a
screen sleeps when nobody touches it is precisely wrong. The window's own flag, not a device
setting, so it lasts exactly as long as the page is on top.

**Verified on the phone.** The map draws turned a quarter with the handset lying sideways on the
desk — Divinity's Reach on the left rather than at the top — at the zoom that fits the map to the
now-landscape viewport. A drag down the screen moves the map down the screen, checked by measuring
Divinity's Reach before and after, with no movement on the other axis. The wake lock is visible in
`dumpsys power`: `SCREEN_BRIGHT_WAKE_LOCK 'WindowManager/displayId:0'` against this package. The
pinch itself is the one thing the tooling cannot drive — `adb shell input` has no multi-touch — so
its arithmetic is pinned by tests and the gesture needs two real fingers.

**It needed the bridge, and no longer does.** That was the honest limit for as long as the screen
only drew where the character was standing. It now opens any map by name — see
`### The map without a PC` — and the bridge is what adds the character to a map, rather than what
makes there be a map at all.

**The map walks with the character, and only when it has to.** Following is on until the map is
dragged: a screen propped against the monitor while somebody plays should keep showing where they
are without being touched. A pan says "this is the part I am looking at", so it stops, and
RECENTRE — the button that means "show me the character" — takes it back up.

The rule is a dead zone rather than a centring, and the reason is the report rate. The bridge
sends ten times a second; a map pinned to the character slides under the eye continuously while
they walk and re-fetches a ring of tiles for every step. So the character roams the middle half of
the viewport freely and the map only moves when they leave it — `FollowBox`, pure and tested,
measured against the viewport the map actually has so a sideways phone gets a sideways box.
Most reports therefore cost two subtractions and a comparison.

A pinned map does not follow. It is a place being read, not a character being watched, and a
character who walked into it would otherwise start dragging the view around underneath somebody.

**Verified on the phone** at the deepest zoom, where the simulator's 400-unit circle is wider than
the box: the tiles moved to a different part of Queensdale while the dot stayed near the middle. A
pan then put the character off screen and the view was identical twenty-five seconds later —
following had stopped. RECENTRE brought the character back and the map was following again.

**The memory was measured rather than reasoned about, and it holds.** The compass died once with
`Scudo ERROR: internal map failure (error desc=Out of memory)`, and that was found by sampling, so
the heaviest screen in the app was not allowed to rest on an argument about eviction policy.
Queensdale, open on the phone for forty-five minutes against the simulator, sampled once a minute
with `dumpsys meminfo`: thirty minutes of scripted use — four pans, a zoom in, two more pans, a
zoom out, every minute — and then fifteen minutes left entirely alone.

Total PSS is flat across the whole hour of it: 333 to 371 MB, slope -341 KB a minute, which is
noise with a sign. `EGL mtrack` never moved from 41104 KB and `Views` never moved from 392, so
nothing is leaking surfaces or view trees.

The one number that climbs is the native heap, and only while the map is being used: 127 MB over
the first ten minutes, 130 over the second, 132 over the third, about 285 KB a minute. Left alone
it climbs at 118 KB a minute instead — and at minute nine of the idle run it *fell* seven
megabytes on its own, 137 MB to 130, with the tile bytes going 64512 KB to 60064 KB in the same
sample. That is the thing worth knowing: the growth is decode churn that gets trimmed, not a
retention. A leak does not hand memory back.

So the LRU cache does what it claims — `MostHeld` at ninety-six tiles bounds what is decoded, and
the rest is Skia's arena breathing. The screen is safe to build on.

**Measured against the simulator over `adb reverse`, not over the LAN.** `adb reverse tcp:47623
tcp:47623` puts the bridge on the phone's own loopback, so a soak needs no hotspot and no Wi-Fi at
all — worth remembering, because the phone had routed itself out of the mobile interface again.

---

### The map without a PC

The map drew where the character was standing and nothing else, so it was blank on a train —
which is exactly when somebody wants to look at Frostgorge and work out where they are going. It
now opens any map by name, and everything it needs is kept on disk.

**A pin, not a mode.** `MapViewModel.Pin` records the map somebody asked for, and from then on the
bridge is still listened to — the character's dot appears the moment they walk into the map being
looked at — but it is no longer allowed to change which map is drawn. Walking through a border
while reading Frostgorge must not drag the screen somewhere else, and neither must logging in.
`FOLLOW CHARACTER` lets go of the pin, and appears only while one is held.

**Browse the open world, search everything, and that split was made by counting.** `/v2/maps` has
1086 maps: 123 `Public`, 919 `Instance`, and the rest matches and scaffolding. Listing all of them
opened the picker on an alphabetical run of story steps — *A Bold Strategy: Alliance Camp* — which
is a list nobody scrolls. Leaving the instances out would make Ascalonian Catacombs unfindable. So
the empty box lists the open world and one keystroke reaches the rest, with the line under the box
saying so. Five maps the API names `???` or nothing at all are dropped: a row nobody can read is a
row nobody can choose on purpose.

Contains, not starts-with, because "gorge" is how a person looks for Frostgorge Sound. A name that
does start with the term still sorts first.

**The offline half is three tables, and the tiles were already there.** `Gw2Cache` now keeps
`maps`, `map_detail` and `continents` beside the currencies — static data, dropped when the build
moves. `CompassTargets` reads a map and its landmarks from memory, then disk, then the API, which
means the compass got the same thing for free. Without the continent row a phone with every tile
of Frostgorge cached still draws nothing, because the zoom reference and the grid size live there.

**The bug this produced is the one worth writing down.** The picker asked the cache "are there any
map rows?", and there were: `CompassTargets` had just written the one map the character was
standing in. The screen offered a list of every map in the game containing Queensdale. A count
cannot tell "the whole list" from "one map somebody happened to visit", so `maps_all` is a meta
flag written only by the `ids=all` save, inside the same transaction as the rows it describes.

**Verified on the phone**, against the simulator standing in Queensdale: the picker opened on 122
open-world maps; "gorge" found Frostgorge Sound alone; opening it drew Frostgorge with its hearts,
waypoints and hero points while the game said Queensdale, with no dot on the map and
`FOLLOW CHARACTER` offered. A boss row on the timetable opens the map it is fought on the same
way — Admiral Taidha Covington drew Bloodtide Coast. The store was then read off the device:
1086 rows in `maps`, `maps_all = yes`, `map_detail` for both maps opened, and continent 1.

**Then proven with the radios off**, which is the run that found the bug worth having. Airplane
mode, force-stop, cold start: the picker listed all 122 open-world maps from the cache, "gorge"
found Frostgorge Sound, and it drew with every heart, waypoint, vista and hero point. No bridge,
no API, no DNS.

**The bug: the screen named one map and drew another.** Opening Snowden Drifts — never visited,
so nothing of it stored — put "Snowden Drifts" in the line underneath Frostgorge's tiles and
Frostgorge's markers. `LoadAsync` took the new map's identity on its first line and published the
drawing on its last, and the landmark fetch in between threw. Everything up to it had come from
the cache and succeeded, so the failure landed exactly in the gap.

Fixed in the two places it can happen. Markers are now the part a map may open without: the map is
published regardless and a missing set is said in words, because a blank map called Snowden Drifts
is a true statement about what the phone has and a labelled lie is not. A failure earlier than
that — the map's own record, or its continent — reverts the identity to the map still on screen,
so the line can never name something that is not being drawn.

**And the tile cache is more generous than expected.** Snowden Drifts drew its own terrain offline
despite never having been opened, because tiles are keyed by continent grid rather than by map:
those squares were already on disk from panning around Frostgorge next door. Neighbours come free.

---

### Home screen widgets

Two of them, both 4x2: the next world boss with a live countdown, and the Wizard's Vault's two
tracks. They are the only two things about the account that change while nobody is looking and
that you would otherwise open the app to check.

**Widgets are native Android, and MAUI does not abstract them.** Each is an `AppWidgetProvider`
(a `[BroadcastReceiver]`) with its layout in `Platforms/Android/Resources/layout` — no XAML, no
bindings, no MAUI controls. Jetpack Glance, the Compose way of writing these, is Kotlin-only and
not available to us.

Two constraints follow, and both leave marks:

- **RemoteViews inflates a fixed whitelist of view classes, and plain `View` is not on it.** A
  layout containing one is refused whole, and the launcher shows "Widget could not be added"
  where the widget should be. Both hairline rules are therefore `FrameLayout`s. Nothing warns
  about this at build time — it compiles, installs, and fails in the widget picker.
- **Custom fonts do not cross the RemoteViews boundary.** Marcellus and Barlow are gone; the
  carved capitals become letter-spaced sans, and the palette carries the family resemblance
  alone. `Resources/values/widget_colors.xml` restates the four colours the widgets use, because
  a RemoteViews tree cannot reach a MAUI resource dictionary.

**The countdown is a `Chronometer`, not a string.** `updatePeriodMillis` has a 30-minute floor,
which is useless for a boss rotation, and rewriting the text every second would keep the app
awake. A Chronometer set to count down ticks in the launcher's own process, so the seconds stay
live while this app sleeps and the redraw only has to happen when the boss changes.

**The trap was the completions.** `/v2/account/worldbosses` empties at reset, so a set read
yesterday evening still lists yesterday's kills — believing it hides bosses you have not killed.
`WidgetContent.Bosses` therefore takes the time the set was read and refuses to trust anything
older than `Resets.LastDaily`, falling back to marking nothing rather than marking it wrongly.
Same rule as the emote screen and the unread Today tracker: absence is not a "no".

**A widget must not fetch when it is drawn.** The launcher redraws on unlock, on rotation, on a
theme change, and would either block or show a spinner on the home screen. So `WidgetService`
splits in two: `RefreshAsync` talks to the API from a WorkManager job every 15 minutes — the beat
the bosses actually keep — and `ReadAsync` only ever reads the store. Its keys are its own
(`widget/…`) rather than the screens', because the screens' snapshots are written when somebody
opens a page and these are written whether the app is opened or not.

That job is separate from the alerts poll on purpose: someone can want a widget without wanting
notifications, and switching reminders off must not silently freeze their home screen. A one-shot
run is enqueued alongside the periodic one, because periodic work is not promised a first run for
a full period and a widget spending its first quarter hour showing em dashes reads as broken.

**The redraw's beat is not the fetch's beat**, and shipping them as one was the mistake a look at
`dumpsys batterystats` caught. The quarter-hour cadence exists because that is when a boss
changes — and working that out costs nothing, since the timetable is a local table. The only
things a request can change are the vault figures and which bosses have been killed, and both
move only while the player is in the game, at a PC, not looking at their phone. So the redraw
stays at fifteen minutes and `RefreshAsync` returns early unless the stored figures are older
than 55 minutes: four radio wakeups an hour became one. Mobile radio was already the app's
largest single draw at that point (5.45 mAh of its 11.5), which is what made the waste visible.

**Drawn to survive being given less room than it asked for.** A landscape home screen hands a 4x2
widget well under its stated minimum, and a LinearLayout answers by clipping its last children —
which cut the countdown in half while the two lines below it kept their space. The lower block of
each widget is now weighted, so it collapses to nothing first and the headline always survives.
Found by placing them, not by reading the XML.

`WidgetContentTests` covers both panels: the stale-completions rule, the account that has killed
everything, a track never read reading as an em dash rather than 0/0, and the countdown wording.

---

### Emotes

A flyout screen of every emote in the game: the command, what the chat log says it does, whether
it loops, whether it works in a chair, and — where the API will say — whether this account has
unlocked it. A tap puts the command on the clipboard. Asked for because finding an emote mid-play
means alt-tabbing to a wiki table.

**The API covers a fifth of the subject, and the obvious implementation is wrong.**
`/v2/emotes` lists 14 emotes. The game has 68: 25 free on every account and 43 unlockable, so the
endpoint knows 14 of the 43 and none of the 25. It omits `/dance`. A screen built on it alone
would list a fifth of the emotes in the game and none of the ones everybody uses.

So `Services/Emotes.cs` carries the whole table, hardcoded for the same reason `EventSchedule`
is, sourced from the wiki's emote table (which agrees with the game's own `/emotelist`). A new
tome means a new row.

That leaves the trap. `/v2/account/emotes` answers confidently about its 14 and is silent about
the other 29 unlockables — every gem-store emote tome. Treating absence as "locked" shows a
player their own `/breakdance` as locked, so `EmoteState` has a fourth value: `Untracked`, said
on screen as "not tracked", with the darkest edge of the three. `null` unlocks (no key, failed
read) mean untracked too; an empty list from the API is a real verdict and means locked. The
header states all three figures separately — *"26 you can use · 13 locked · 29 the API cannot
see"* — because folding the last into either of the others is a lie in one direction or the
other. Same rule as the hidden hero-points tile and the unread Today tracker.

**Search is ranked, not filtered.** Alphabetically, "dance" lists `/bloodstoneboogie`,
`/breakdance`, `/crabdance` and `/happydance` before `/dance`. Exact command first, then prefix,
then substring, then the chat text — so "grill" still finds `/barbecue` through its alias and
"coffee" finds `/sipcoffee` through its description. The leading slash is optional both ways.
Filtering runs against the local catalogue on every keystroke, the `StorageService` shape: one
read, many searches.

**With nothing typed there is no relevance to rank by**, so the list leads with what can be used,
then the tomes nobody can check, then the ones the API has positively denied — a maybe must not
rank below a no. A search is left alone: typing "rock" means /rock whether or not you own it.

The row spine spans a star row rather than the text, so an emote with nothing on its third line
gets a full-height edge instead of a stub, and the third line stays in the layout when it is
empty — an empty label still holds its line, which keeps rows level at any font size where a
fixed height would only have agreed with one phone.

`EmotesTests` covers the catalogue's integrity, the four states and the ranking.

---

### A tracker that fails no longer disappears

Found by running the Release build: the Today screen read "4 of 4 unfinished" where it should
have said 5. Map bonus chests had been rate-limited away after the catalogue fill, and
`TodayService` dropped any row with `Total == 0` — so the tracker simply was not there. A
shorter list, no error, nothing to notice.

Failed trackers now stay, marked and quiet: "Could not be read just now — pull down to try
again", with an em dash instead of "0 / 0" and a banked spine instead of brass. The section
summary counts only what it could read and appends "· 1 unread", because an unreadable tracker
is neither finished nor outstanding — nobody knows what it is.

This was the last place in the app still presenting absence as fact, which the hidden
hero-points tile, "not counted" versus zero holdings, and the vault staleness warning all
exist to avoid.

`Services/TodayRows.cs` now holds the row and the summary, free of MAUI — the spine colour
moved into a XAML `DataTrigger`, the same lesson `CheckFinding` taught. `TodayRowsTests`
covers it, and restoring the silent drop breaks one of them.

Note the interaction with offline resilience: a lost connection no longer reaches this state
at all, because the snapshot serves the row. The unread state is now for failures the cache
cannot cover — a rate limit, a missing scope, or a first run with nothing stored.

---

### Offline resilience

Built, and now covering every read-only screen: wallet, materials, roster, **Today, the
Wizard's Vault, the trading post and Find**.

`Data/SnapshotStore.cs` keeps the last successful *payload* per endpoint — never a view model,
which carries colours and formatted strings shaped for a screen rather than for keeping.
`Services/OfflineReader.cs` has two policies: `ReadAsync` rethrows when nothing is stored,
because a wallet with no figures is not a wallet, and `ReadOptionalAsync` degrades to null for
data a screen can manage without. Both fall back only on `Network`/`ServiceUnavailable`, so a
revoked key still surfaces rather than hiding behind yesterday's data.

Four things learned doing it, each of which cost a rebuild:

- **The session had to survive offline first.** Validating a key needs `/v2/tokeninfo`, so
  offline every screen told the user to add an API key they already had. The accepted token is
  now remembered in `SecureStorage` beside the key it belongs to — not in the snapshot
  database, because it must die with the key.
- **A screen's auxiliary lookups break it, not its main payload.** The roster kept failing
  with its `characters` snapshot present and readable, because professions, specializations
  and mastery points were each fetched separately. Find every `await client.` in a method, not
  just the obvious one.
- **`Notice` was invisible on three pages**, surfacing only through `EmptyMessage`, which needs
  an empty list — exactly not the offline case.
- **Prices are deliberately never kept.** A day-old price presented as the market is worse than
  no price, so the storage total and the trading post say values need a connection rather than
  showing a stale figure — and never "0c", which reads as a robbery rather than an absence.

Not kept offline: anything the app cannot honestly show stale — live prices, and the catalogue
download itself.

---

### Achievement progress

Built. An **Achievements** area listing what is nearest to finishing:

```
CLOSEST TO FINISHING
25 nearest of 171 in progress · 59 finished
  Toxic Alliance Slayer     94 of 100   6 to go     +1 AP
  Tinkerer                  28 of 30    2 to go     +5 AP
  Rift Warden               13 of 15    2 to go    +50 AP
```

`Services/AchievementProgress.cs` holds the ranking, covered by `AchievementProgressTests`.

- **Rank first, then fetch names.** There are 8,227 achievements and an account has touched
  thousands. `/v2/account/achievements` is one request carrying only ids and counts, so the
  ranking happens on that and names are fetched for the two dozen shown — one more request
  instead of forty.
- **Untouched achievements are excluded.** Most of the eight thousand sit at zero, and one
  needing a single kill would otherwise outrank everything actually being worked on. "Closest
  to finishing" means started.
- **A tie on proportion goes to the smaller job.** Two kills is an evening; two hundred is a
  project, and both are 90%.
- **Counts, not percentages.** "9 of 10 · 1 to go" says how much work is left; "90%" does not.
- **The tier being worked on is the one whose count matches the reported target**, which is
  what makes "+50 AP" correct rather than the achievement's lifetime total. An unmatched
  target — which happens when ArenaNet retunes one — shows nothing rather than a guess.

---

### World boss reminders

Built. The background poll now says, of a boss you have not killed today:

```
Admiral Taidha Covington in 13 minutes
Bloodtide Coast — you have not done this one today.
```

No new API surface: the timetable is the hardcoded `EventSchedule`, what is already killed
comes from `/v2/account/worldbosses`, and `AlertService` already had the poll, the channel and
the seen-key store. `Services/BossAlerts.cs` holds the selection and wording;
`BossAlertsTests` covers it.

Two numbers that are reasoned, not picked:

- **A 35-minute window**, because the background poll runs every 30. Anything shorter drops
  appearances that fall in the gap between two polls and never mentions them at all. The cost
  is that some notice arrives half an hour ahead rather than fifteen minutes, which is the
  right way round to be wrong.
- **At most two at once.** Early in the day nothing has been killed and half the timetable is
  inside the window; three notifications arriving together teach the user to swipe without
  reading.

The alert key is the *appearance*, `boss:{id}:{spawn time}`, not the boss — so the same spawn
seen by two consecutive polls is announced once, and the same boss tomorrow is news again.
Verified on device: two notifications on the first check, "nothing waiting that you have not
already seen" on the next.

---

### Crafting on the item page

Built. Tapping any item shows what makes it, measured against what the account is holding:

```
CRAFTING                        Refinement · Armorsmith
Short 9 Copper Ore.
  Copper Ore      10 needed              1 held
  Lump of Tin      1 needed              1 held
Buying the rest costs about 2s 70c.
Counted from everywhere you keep things.
```

`Services/CraftPlan.cs` holds the arithmetic and is covered by `CraftPlanTests`. The API knows
what a recipe needs and, separately, what the account holds; it never joins the two, and that
join is the whole feature.

Decisions worth keeping:

- **Null holdings are not zero.** "You have none" and "the app has not counted yet" are
  different answers, and reporting the second as the first sends a player shopping for
  materials they already own. `CraftPlan.Batches` returns null if any line is unknown.
- **Held counts name their source.** The full storage snapshot when the Find screen has been
  opened, otherwise one request for material storage alone — where crafting ingredients almost
  always live. The screen says which it used rather than implying completeness.
- **The shortfall is priced at what sellers are asking**, because buying now is the decision
  being made. If any missing ingredient is untradable the total is withheld rather than
  understated.
- **Ingredients are items**, so tapping one opens its own page — which lets a recipe be walked
  down to raw materials one tap at a time.

**The full chain.** `ALL THE WAY DOWN` expands past the intermediates you would have to craft
on the way, and is shown only when it says something the ingredient list above it does not —
an ingot made straight from ore expands to its own ingredients, and repeating them would be
noise. `Services/CraftTree.cs` holds the walk; `CraftTreeTests` pins it, and deliberate
mutations of the two subtle rules each break exactly one test.

Three rules, all easy to get subtly wrong:

- **What you already hold stops the descent.** Fifty Bronze Ingots means you do not need five
  hundred Copper Ore. Only the shortfall is expanded.
- **Held materials are spent, not reused.** The same ore feeding two branches is counted once,
  so the walk carries a budget and draws it down.
- **Batches round up.** A recipe yielding five when six are wanted means crafting twice.

Verified on device against a four-level chain — Weighted Spearhead → Deldrimor Steel Ingot →
Iron/Steel/Darksteel Ingots and Lump of Mithrillium → ores. The app asked for 20 Lump of
Primordium, 10 Lump of Coal and 10 Thermocatalytic Reagent, and correctly omitted Mithril Ore
(120 needed, 139 held), Iron Ore (90/250), Platinum Ore (40/139) and Ectoplasm (1/19). Every
figure checked by hand against the live recipes.

**Reaching an item you do not own.** The Find screen now shows a second group, "not in your
storage", from the catalogue — otherwise the item screen is only reachable for things already
in your possession, which makes "what would it take to craft this?" unanswerable for anything
you have not got yet.

Not done: the reverse direction. `/v2/recipes/search?input=` would answer "what can I make
with this", which is a different question and a wider one — an ore appears in dozens of
recipes.

---

### Crafting guide

Built. A **Crafting** area showing where each discipline stands and what to make next:

```
DISCIPLINES
  Jeweler     Sweet Child O Night    36 · 39 to the next tier
  Artificer   Imothep The First      20 · 55 to the next tier

WHAT TO MAKE NEXT — Artificer · 20
  Green Wood Dowel          no requirement   none from stock      34c
  Green Wood Plank          no requirement   32 from stock        51c
```

Crafting ratings were never visible before — `character.crafting` was in the response and
simply unparsed.

**Two rules from the wiki, in `Services/CraftingLevels.cs`** and covered by
`CraftingLevelsTests`:

- Recipes go grey. Experience stops once your rating is far enough above a recipe's
  requirement, and how far depends on what is being made: **25** levels for refinements and
  components, **40** for equipment and food, **250** for legendary components. The API's
  recipe `type` carries exactly that distinction. Without this the app would recommend
  recipes that teach nothing.
- Tiers sit at 75/150/225/300/400/500, which is where "39 to the next tier" comes from.

Ranking is by ingredient cost at what sellers are asking — the only ordering that is a fact
rather than an opinion.

**`/v2/account/recipes` is not the levelling path.** It reports only recipes *learned from an
item* — bought or discovered. This account, with three disciplines under way, returned eight,
all of them rating 400. Every auto-learned recipe is invisible to it. Discovering that is why
the guide reads the local recipe catalogue instead: auto-learned means every account has them
at rating, so catalogue plus rating is the whole answer.

That made the recipe catalogue a prerequisite rather than an optimisation — see below.

---

### The full item catalogue

Built. **Settings → Item catalogue → Download all items** fills the local catalogue from the
API: 74,057 items, 371 requests, about 90 seconds.

`Services/ItemCatalogue.cs` runs the fill; `Services/CataloguePlan.cs` holds the pure part
(what is missing, and in what order), covered by `CataloguePlanTests`.

Four decisions worth keeping:

- **Tradable items are fetched first.** `/v2/commerce/prices` with no ids returns the ~28,000
  ids the market actually trades, in one request. Fetching those before the rest means an
  interrupted fill still leaves the trading post search working — which was the whole point of
  the feature. This proved itself during development: two fills were cut short and the search
  was fully usable anyway.
- **Paced at one request per 200 ms**, matching the 5-per-second refill. Going flat out drains
  the 300-token burst and then fights the limiter for the remaining 71 requests.
- **The item table is never wiped.** `Gw2Cache.InvalidateIfStaleAsync` now clears only
  currencies. Wiping 74,000 rows on every game build would discard the catalogue and refetch it
  every week or two, forever.
- **The price-watch picker only offers tradable items.** A watch on a soulbound item can never
  fire, so offering one is worse than not finding it — the user would set it and wait for a
  notification that cannot arrive. Verified on device: searching "dragonite" offers
  *Recycler: Dragonite* and correctly withholds the account-bound *Dragonite Ore*.

Search is also ranked now: with 74,000 rows, a name that starts with what was typed beats one
that merely contains it. Searching "dawn" returns *Dawn* above *Dawn Dye*.

---

### Raids and dungeons on the Today screen

Built. **Dungeon paths** (0 / 33) sits under *Today*; **Raid encounters** (0 / 26) sits under
*This week*.

This entry previously claimed both were "the same shape — account-scoped, `progression`, a
flat array of completed ids". That was wrong, and it is the kind of wrong that produces a
screen full of plausible nonsense:

- `/v2/worldbosses` and `/v2/account/worldbosses` do share an id space. **Raids and dungeons
  do not.** `/v2/raids` lists six raids while `/v2/account/raids` reports individual
  encounters; `/v2/dungeons` lists eight dungeons while `/v2/account/dungeons` reports
  individual paths. The static side has to be expanded before either can be compared —
  `?ids=all` does it in one request each.
- **Raids are weekly.** Encounters clear at Monday 07:30 UTC, not the daily 00:00 reset.
- **Four of the thirty raid events are checkpoints**, not fights — Spirit Woods, Twisted
  Castle, Gate, Camp — and the account endpoint reports those as cleared too. Counting them
  gives 30 encounters instead of 26, and lets a week's progress read *30 of 26*.

That last one is why `Services/ResetProgress.cs` exists: the denominator is what is tracked,
and nothing outside it can move the numerator. All five trackers now count through it, and
`ResetProgressTests` pins the behaviour — a deliberate mutation to trust the completed list's
length breaks two of them.

Dungeon captions name the *dungeon*, not the path, because the path ids are internal
("ac_story", "hodgins") and "Ac Story" is not something a player would recognise.

---

### Find an item across storage

Built. A **Find** tab in the Account area answers "where is my X?" across material storage,
the bank, shared slots, every character's bags, and what each character is wearing.

`Services/StorageService.cs` reads every place and inverts it into holdings per item;
`Services/StorageTally.cs` holds the pure part — collapsing slot-by-slot readings, ordering
places, and phrasing the answer — and is covered by `StorageTallyTests`.

Two things worth knowing about the design:

- **Search covers what you own, not what the app has seen.** Every held item's name is
  resolved on load, so a miss means you do not have it. The older cache-index search (still
  used by the price-watch item picker) could only find items the app had looked at before.
- **Only worn gear counts as equipped.** The `equipment` array also carries every equipment
  template's contents, so a legendary drawn from the armory appears once per tab.
  `EquipmentPiece.IsWorn` filters it, otherwise the app invents copies.

**This closed the valuation gap.** The material-storage-only estimate read 40g; the whole
account reads 61g 94s. The breakdown sits under the search on the same screen, since reading
every place is what makes the search possible.

---

### Collection checklists

Achievements of type `ItemSet` carry a `bits` array naming every entry, so a collection can say
*what is still missing* instead of "40 of 51". Tapping such a row on Achievements opens
`collection?id=`; rows that only count something stay put, and say so with a "tap for the
checklist" hint on the ones that do lead somewhere.

Two traps, both confirmed against the live API and on the phone:

- **The account reports done entries as indices into the bit array, not as ids.** Position is
  identity. Read as ids they would all miss and every collection would read as untouched —
  entirely plausible-looking and entirely wrong. `Services/Collection.cs` exists to hold that
  pairing in one place.
- **The array carries blank placeholders.** Maguuma Explorer lists 170 entries where the
  achievement asks for 167; the difference is three entries with no text and no id. They are
  dropped *after* the indices are matched, because the account's indices count them. Sifting
  first would shift every later entry up and tick its neighbour instead.

Entries come as `Item`, `Skin`, `Minipet` or `Text`, resolved from three different id spaces
plus the text itself. A lookup that fails degrades to "Skin 208" rather than taking the screen
down. The kind is only printed when a collection actually mixes kinds — "Item" under all fifty
rows says nothing.

Entries carry their icon and open where the answer is: an **item** goes to its page in the app
(what it is, where yours are, what it crafts into, and its own wiki button), while a **skin**,
**miniature** or **map step** — none of which have a page here — goes straight to the wiki.

**The map prefix has to come off before searching.** Exploration entries read "Sparkfly Fen:
Toade's Head", and the wiki indexes the area under its own name; searching the whole string
lands on "Explorer". `Wiki.SearchTerm` drops everything up to the last ": ", which was checked
against the wiki's search API and then on the phone — "Brisban Wildlands: Aurora's Remains"
opens the article, *Getting there* section and all.

**What the API still will not say:** where any of it comes from. The screen says so rather than
implying the list is a route.

---

### Undercut check on open orders

The Orders screen already flagged "undercut". A flag is not advice: undercut by one copper with
three units ahead of you is nothing, and undercut by one copper with eight thousand ahead means
the listing will not sell this month. The screen now says which.

Per order, from `/v2/commerce/listings`:

- **the gap** — "undercut by 50c", rather than leaving you to subtract two coin strings
- **the queue** — "8,000 ahead of you", the units certain to be filled first
- **the tie** — at the best price, "78,407 more at your price". This is the answer to *why has
  this not sold when I am the cheapest*, and nothing else in the app could give it.

Beaten orders now sort to the top of their section, and the heading carries the count
("7 · 1g 20s · 3 undercut"). Sorting by price alone buried a beaten order mid-list.

**Two traps in the maths, both silent if wrong:**

- **Your own listing is in the book.** The tie has to subtract your quantity or an order with
  no competition reports its own size as rivals. Verified live: Seal of the Soldier's 106c step
  holds 78,408 units, of which one is yours.
- **Same price is a tie, not a place.** Orders at one price fill oldest first and the API never
  says when the others were placed, so only strictly-better units count as "ahead". Counting
  ties there would be a guess dressed as a number.

**Why books are rationed.** `/v2/commerce/listings` is far heavier than `prices` — Glob of
Ectoplasm alone runs to ~2,700 price steps, and forty mixed items weigh half a megabyte. Books
are fetched only for orders that are contested (beaten, or sitting at the best price) and capped
at 25 per screen, largest stakes first. An order asking well above the market is not queued
behind anything, so depth would say nothing about it. When the cap bites, the heading says so.

---

### Equipment templates (and what `equipment_tabs` actually holds)

Probed on the device rather than guessed. Two findings decided the shape of this:

- **`equipment_tabs` is already inside `/v2/characters?ids=all`**, which the app fetches for
  the roster anyway — alongside `active_equipment_tab` and `equipment_tabs_unlocked`. The
  feature costs **no extra request at all**.
- Each tab holds `tab`, `name` (an empty string, not null, when unnamed), `is_active`,
  `equipment`, `equipment_pvp`. Pieces carry `id, slot, binding, bound_to, location, skin,
  dyes, upgrades`.

**The Gear tab now has a template strip.** A character keeps several loadouts and wears one;
everything before came from the top-level `equipment` array, which is every template run
together — the reason it needed `IsWorn` filtering, and the reason a second set of gear was
invisible. Switching reloads the whole tab, because the doll, the stat prefix and the gear list
are all read off the same pieces and half of them would go stale otherwise.

`is_active` is the authority for what's worn, never the tab number — the numbers are positions
and nothing promises the worn one is first.

**A wrong message this uncovered.** Switching to an unused template said *"No equipment
returned. The API key needs the 'builds' permission"* — while the same key had just loaded the
other template. Two different stories, one of them nobody's fault: `Loadouts.EmptyNotice` now
separates an empty template from a key that genuinely cannot read equipment.

**PvP gear: shape confirmed, never seen populated.** `equipment_pvp` is `{amulet, rune,
sigils[4]}` and on this account every field is null on every template — the character has never
set a PvP build. Amulets are their own id space (`/v2/pvp/amulets`, verified separately), runes
and sigils are ordinary items. The screen says "No PvP gear set on this template" rather than
showing six empty rows. **The populated path is written from the API's own field names and has
not been seen with real data.**

### Dyes

The colours a character is actually wearing, under the doll: swatch, dye name, and how many
pieces wear it. A dye on eleven pieces is the character's colour; one used once is a trim, so
the palette leads with the most-worn rather than with whatever piece happened to arrive first.

**A dye is not one colour.** `/v2/colors` gives every dye a separate rgb for cloth, leather and
metal, and which applies depends on the wearer's armour weight — so the profession decides what
a dye looks like. Getting this wrong is silent and pretty: every swatch would be a real colour
from the real dye, just not the one on the character. Light is Elementalist/Mesmer/Necromancer,
medium is Engineer/Ranger/Thief, and heavy is the default, because the three heavy professions
are the ones left over and an unrecognised profession is more likely new than light.

Unused channels come back as nulls inside the array rather than as a shorter array, so they are
filtered rather than counted. A dye the colour endpoint does not return is dropped instead of
shown as a blank swatch, which would read as a bug in the screen.

Decoration, so it degrades quietly: no connection means no strip, never an error over swatches.

---

### Net worth in one number

One figure on the wallet screen, broken into where the money actually is — largest part first,
with its share. The pieces all existed; nobody added them up, so "how am I doing" was spread
over four screens.

The total is written in the same three metals as the coin card above it, one size down. Flat
gold made it read as a different kind of number sitting next to a coin figure — so the service
hands over a `CoinPurse` rather than a formatted string, and the screen writes it the way this
app writes every other amount of money.

**Six inputs, three kinds of money:**

- coin in the wallet
- everything in bags, bank and material storage (the storage scan already values every stack)
- items listed for sale, at **buy-order value**, not at the price you are asking
- coin committed to buy orders, which is recoverable in full — cancelling refunds it
- the delivery box, coin and goods, as one line

**Listed items are valued like any other stack.** Seal of the Soldier is listed at 1s 06c and
its buy side is literally zero — nobody is bidding — so it contributes nothing, and the "Listed"
row is absent rather than showing 90c of imaginary money. Valuing listings at the asking price
would flatter every optimistic seller, which is the same reason the storage screen has always
used buy orders.

**Asked for, not done on arrival.** Reading every character's bags is the expensive half, and a
wallet that took ten seconds to show coin would be a worse wallet. The button reuses a storage
snapshot if one is already in memory, so opening it after the Find screen is nearly free.

**What the number does not include, said on the screen:** account-bound items, karma, and gems.
Gems are the awkward one — they convert at a rate that moves, so counting them would make the
total drift on its own. A place the key cannot read is named, so a missing permission reads as a
gap rather than as a loss.

---

### What the account owns

An OWNED section on the Account screen: how the game is held, and which expansions are on it.

**It cost nothing to add.** The `access` array has arrived with every `/v2/account` read since
the app was written — the same endpoint that already gives fractal level and WvW rank — and was
being discarded. No new request, no new permission.

**One flag is not the whole truth — found on the real account.** The screen listed one
expansion where the account page showed two. From the API's own documentation: an account that
received Heart of Thorns *by buying Path of Fire* *"will not have this flag set"*, and
`PathOfFire` *"also implies that the account has access to Heart of Thorns"*. So Path of Fire
now brings Heart of Thorns with it, slotted in front where its release belongs, and captioned
"included with Path of Fire" — ownership that is inferred says so, and can be checked. Nothing
else implies it: End of Dragons and everything after were sold standalone, and inventing
ownership would be the same failure pointing the other way.

The same paragraph fixed `IsFree`. `PlayForFree` alone is not the test — the documentation says
it *"also appears on accounts who have purchased the base game or any of the expansions"*, and
only means a free account when it stands without any other flag.

**Each expansion carries its own emblem.** The API has artwork for professions and none for
expansions. The first version was hand-drawn in the vein of the body silhouettes; it is now the
game's own marks, `*_icon_small.png` from the Guild Wars 2 Wiki at 128×128 — the compact
emblems, not the wide wordmarks, which would be illegible in a 26px row.

They are **downloaded once and shipped with the app**, not hotlinked: the wiki is not a CDN, and
an emblem that needs a connection is an emblem missing on a train. They are ArenaNet's artwork,
so the screen showing them carries the credit.

An expansion released after this build has no file to ship, so it keeps the hand-drawn plain
mark — never a blank space, which reads as a picture that failed to load. The wiki's naming is
consistent (`HoT`, `PoF`, `EoD`, `SotO`, `JW`, `VoE`), so adding the next one is a download and
a line in the table.

**The rule that matters is never dropping a token.** Expansions keep coming, and a list that
quietly omits one it does not recognise tells someone they do not own what they bought. Known
tokens get their proper names, because splitting them gives "Heart Of Thorns" and "Secrets Of
The Obscure"; anything unrecognised is split on its capitals and shown anyway, so a new
expansion appears the day it ships.

Base-game tokens (`GuildWars2`, `PlayForFree`, `None`) are not expansions and are summarised on
their own line instead. A free account is one that has `PlayForFree` *and not* `GuildWars2` —
accounts that upgrade keep both tokens — and saying so plainly explains a trading post that
would otherwise look broken, since free accounts cannot use it.

---

### The home instance

Nodes and cats on the Account screen, missing first. The cheapest checklist in the app: both
catalogues are small — 57 nodes, 35 cats — and the ids are **self-describing slugs**
(`elder_wood_node`, `garden_plot_01`), so nothing needs a name lookup at all. Four short
requests, and the catalogues answer offline once seen.

**Two sides, on purpose.** What is missing is a shopping list, since almost every node is
bought; what is present is a daily gathering round. One count would serve neither.

**The cats have no names.** The API gives only a `hint` — `grilled_chicken`, `spicy_flank` —
which is the food you leave out to lure it. The hint *is* the answer, not a clue to one, so it
is shown as the row's name.

Node ids drop their trailing "node": under a heading that already says nodes, "Elder Wood Node"
stutters. Numbered plots read as `Garden Plot 1`, not `01`.

**Not knowing is not the same as owning none.** The account half can be missing two ways — a key
without `progression` and `unlocks`, or a request that failed — and both leave the list looking
untouched. `Summarise` takes a nullable count for exactly this: unknown prints "57 nodes in the
game", never "0 of 57", which would report an empty home instance to someone whose instance is
full. The note says which of the three states the screen is in.

**Not the Homestead.** `/v2/account/homestead/decorations` and `/glyphs` are the Janthir Wilds
feature and a different thing entirely; they would be empty on an account without that
expansion.

---

### Quiet hours for notifications

Stated as **when you can play**, not as two blocks of quiet. Someone at work all day and asleep
at night has one window in between, so it is one setting — "only between 17:00 and 22:30" — plus
an "all day at weekends" exception. Two quiet blocks would have been two settings that must meet
exactly in the middle to leave no gap.

**Outside the window the check still runs; only the notification is withheld.** Skipping the
poll would leave its bookkeeping behind and every suppressed alert would arrive in one heap the
moment the window opened. Nothing is deferred either: a world boss reminder that turns up after
the boss has gone is worse than silence, and everything else is still on its own screen in the
app. One place enforces it — `CheckAndNotifyAsync`, the only route to the shade.

**Three traps, all silent:**

- **A window crossing midnight** (20:00–02:00) is the normal shape for an evening player and
  reads backwards to a naive comparison, which would allow exactly the hours it should silence.
- **Two identical times** are easy to set with two pickers. Read as a zero-length window that
  would silence the app for good, with nothing on screen to explain it — so equal times mean no
  restriction.
- **Withholding is invisible by nature.** Every ambiguous case resolves towards letting a
  notification through, and the whole feature is off by default: an app that sends at a bad hour
  is at least visibly wrong.

The setting reads back as a sentence, because four controls do not say what they add up to.

**Settings is grouped now.** It had become one scroll of four unrelated things — a key, a
background poll, an 11 MB download and an account summary — and the quiet-hours controls were
the point where scrolling past three of them to reach the fourth stopped being reasonable. Three
tabs: **Key** (with the connected account beside the key it describes, rather than at the far
bottom), **Reminders**, **Data**. It reuses the character page's own `PageTab` strip, since a
settings page is not a Shell area either. The rules that separated the sections came out: under
a tab strip, a rule at the top of a group is a line with nothing above it.

---

### One navigation idiom

*Written for the MAUI app. Since 2026-10-07 the areas are reached from a bottom bar rather than a
drawer; the top strip for an area's views stands. See "On the phone, 2026-10-07".*

Two areas — Account and Trading Post — listed their views in Shell's tab bar, which Android puts
at the **bottom** of the screen. Every other multi-view page in the app (a character, and now
Settings) carries a strip at the top. Two idioms for the same idea, and the odd one out looked
like a different app.

Both areas now use the top strip. Shell's `Tabs` still define the routes — `AreaTabStrip`
navigates to them absolutely, since moving between the views of an area is a switch and a back
stack of sibling tabs is nobody's mental model — but `Shell.TabBarIsVisible="False"` hides the
bar itself.

**The list of views lives in one table** (`ViewModels/Areas`), not on the eight pages that show
it. A strip that has to agree with the Shell routes exactly is precisely the kind of thing that
drifts when it is written out twice.

Each page places the strip in the same grid row as the rule beneath the title bar, wrapped
together — a bare sibling would have landed on top of the rule rather than under it.

---

### Row edges that match their row

The coloured bar down the left of a list row was often shorter than the row it edged — full
height on a row with a second line, two thirds on a row without one. In a list where only some
entries have a caption, that reads as ragged rather than as rhythm.

**Why, measured rather than guessed.** Dumping the view bounds off the device showed the rows
were already uniform and the bars were not:

| | row | bar |
|---|---|---|
| Heart of Thorns (two lines) | 113 | 109 |
| Path of Fire (one line) | 113 | **61** |

Every row of these templates was `RowDefinitions="Auto,Auto"` with a `MinimumHeightRequest`. Auto
rows measure their content and nothing else, so the minimum stretched the *grid* while the rows
inside it stayed at content height — and a bar spanning those rows stopped where they did.

The fix is one character per template: the last row becomes `*` and absorbs the slack, so a bar
spanning the rows spans the whole grid. Thirteen templates across eleven screens had the same
shape. Verified back on the device: 136/136, 132/132, 113/113.

---

### What can I make right now

The other crafting question, and the one the API answers least: not what to make next, but what
can be made **outright, from what the account is already holding**. On this account, 20 recipes
— Jute Breeches Lining 58 times over at the top.

**It costs no requests.** Both halves were already on the phone and nobody had joined them: the
13,000-recipe catalogue in SQLite, and the storage scan that counts every stack across bags,
bank and material storage. The only fetch is names for what comes out.

**The join is SQL, not memory.** Two temp tables — what is held, and which recipes are known —
then one grouped query. The `HAVING` is the crux: a recipe qualifies only when the number of
ingredients that matched what is held equals the number it actually takes, so a recipe missing
one of five is excluded rather than counted as four-fifths there. `MIN(held / needed)` gives how
many batches the materials cover.

**`/v2/account/recipes` finally earns its place.** It reports only recipes learned from an item —
the half the catalogue cannot infer from a rating — which is exactly what is needed here
alongside the auto-learned ones. This account has 8 of them.

**Named by character, and by the right one.** A recipe within reach of the materials is useless
if nobody can make it, and the character named is the *lowest* rating that still qualifies:
crafting below your rating earns nothing, so the one still levelling does it.

**Checked against an independent recomputation.** The app's answer was reproduced from the
pulled `gw2cache.db3` and `snapshots.db3` in a separate script — 20 recipes, same rows, same
order, same batch counts. Worth doing for a screen that makes claims about someone's materials.

---

### The gem exchange

On the wallet, under net worth: *"7g 79s 70c buys 18 gems"*, and beneath it the number the game
never shows you — *"18 gems sell back for 4g 77s 59c — 61% of what went in."*

**The exchange quotes a trade, not a rate**, and it prices a small trade worse: 77,970 copper
came back at 4,331 per gem where a million came back at 4,132. So nothing here multiplies a
per-gem figure by an amount — both legs are quoted for the amounts actually involved, and the
return leg is quoted for exactly the gems the first leg would give, so the two halves are the
same size and the loss between them is real rather than an artefact of two sample sizes.

**A trade too small to make is answered with `{"text": "too few coins"}` on a 200**, not an
error. Deserialised as a quote that is an offer of zero gems, which reads as a broken screen
rather than a refusal — so an absent quantity is the check, and the message is repeated back as
a sentence.

Two public requests, no key needed. This also closes the hole net worth left open: gems are
excluded from the total because the rate moves, and this shows the rate instead of pretending
it does not exist.

---

### Masteries

A fifth view in the Account area. The Standing screen had shown a mastery point total since the
beginning and never said what it buys: now it is *"6 mastery points waiting to be spent"*, the
regions they belong to, and every track with what its next level costs — what can be trained
today first.

**Three traps, and two of them were caught only by looking at the screen.**

- **The account's level is a position, not a count.** `/v2/account/masteries` reports a
  0-indexed reference into the track's levels, so a track at level 4 has five done. Read as a
  count it understates every trained track by one.
- **The two endpoints name the same region differently, and nothing says so.** A track names its
  region by the map — `Tyria`, `Desert`, `Jade` — while the points totals name it by the
  expansion that sells it: `Central Tyria`, `Path of Fire`, `End of Dragons`. Matching the
  strings directly fails on *every* region at once, which reads as an account that can afford
  nothing anywhere. That is exactly how it looked on the phone: six Central Tyria points and not
  one track marked affordable. The pairing is by content — Tyria holds Pact Commander and
  Fractal Attunement, Desert holds the mounts, Jade holds fishing and skiffs.
- **Points do not cross regions.** Central Tyria points train no part of a Heart of Thorns track,
  so affordability is judged against the track's own pool and the shortfall names it.

**The Standing row was wrong too, quietly.** It showed points *earned* — a lifetime tally — where
the number that decides anything is what is left. This account reads 6 now, with "9 earned,
3 spent" underneath.

Self-checking, as it turns out: Pact Commander sits at 2 of 5, and its first two levels cost
1 + 2 points — exactly the 3 the API reports as spent.

---

### What an item turns into

The item page could always say what makes a thing and never what a thing makes. Bolt of Jute now
reads *"Used in 94 recipes — the 15 soonest to reach"*, each with how many it takes and who can
make it: Jute Breeches Lining (1 needed · Tailor), Box of Malign Chain Armor (8 needed ·
Armorsmith). Every row opens that item, so a material can be walked upward the same way an
ingredient list is walked down.

**No request, and it works offline.** `/v2/recipes/search?input=` answers this, but the catalogue
is already on disk with an index on the ingredient — so it is one lookup rather than a fetch.

**How many it takes is the point.** One at a time and eight at a time are different prospects,
and a list of recipe names alone does not say which. A rating of 0 prints as no rating rather
than as "Tailor 0", and a capped list says how many it is capped from — fifteen of ninety-four
reads as the whole answer otherwise.

---

### Magic find from luck

**34%** on the Account screen, with "5,790 luck consumed · 110 more for 35%" underneath.

This was refused for a long time, and the reason was right: there is no formula. The steps run
from 100 luck for the first percent to 30,000 near the cap, the API carries none of it, and
interpolating between a few published anchors produces a figure that looks authoritative and is
wrong.

**So the app carries the real table** — all three hundred rows, taken from the Guild Wars 2
Wiki's Luck article and checked on the way in rather than trusted: three hundred levels with no
gaps, strictly increasing totals, every step equal to the difference between neighbouring
totals, and a final total of **4,295,450** — which the app already held independently as
`LuckForCap`. That last check is what makes it the right table rather than a plausible one.

Read with a binary search, and the reading is where the danger is: a threshold reached exactly
counts, one short does not. 1,232,380 buys the 195th percent; the 196th wants 1,255,080.

**Base magic find only**, and the row says so — achievement points, food, boosters and guild
bonuses all stack on top and none of them is in the number.

---

### The specialization grid

The Build tab drew a specialization as a name and three trait names on a line. It now draws it
the way the game does, from a screenshot the user supplied: the line's own artwork behind, its
official emblem beside the name, then three tiers left to right — each a round minor trait next
to the column of three majors that compete for it.

**Taken traits are lit; the rest stay dim rather than absent.** Three of nine lit *is* the shape
of a build; three names in a row is not. Minor traits never dim, because they are granted rather
than chosen. Tapping any trait gives its own words, the same way skill tiles already do.

**The banners are cut before they ship.** Measured off the files: a specialization background
is 1024x256, and only its lower-left carries a picture — the top half is fully transparent, the
right quarter empty (Deadeye's is opaque black instead). Drawn whole, half the banner shows the
panel through it.

No layout fixes that. The empty/painted boundary sits exactly on the art's vertical midline, and
`AspectFill` crops from the centre — so **every** window is 50% empty whatever height the row is
given, which is worth working out on paper before trying sizes. Nor can the traits simply sit
inside the painted strip: at ~350dp wide that strip is 43dp tall and three stacked icons need
84dp. The reference screenshot works because it is ~880dp wide, where the strip is 147dp.

So each of the 81 banners is cropped once, at build time, to the region that carries a picture —
anchored left where the character stands, every one to the same 4.2:1 so every row is the same
height. As JPEG, since flattening removes the alpha channel and photographic art in PNG costs
six times as much: **1.3 MB for all of them**, 16 KB each, and no connection needed. They are
excluded from MauiImage's density expansion, which would otherwise ship five copies of each.

A specialization released after this build has no file and falls back to the API's own artwork —
half empty, but present rather than blank.

**Laid out as the game lays it out, after two corrections from the phone.** The artwork is a
1024x256 banner - four to one - and it was first stretched behind the whole panel, where
AspectFill had to magnify it about two and a half times to cover the height and threw away more
than half its width. It now fills the row, with the traits over its right two thirds — the
banners were all cut with the character at the left, so the traits start past it rather than
sitting on top of it. The line's emblem appears once, in the header, rather than again over the
art.

Its right third is also **solid black**, measured off the file: brightness falls to zero at 75%
of the width. Over a panel that is not black that reads as a filled rectangle stopping short,
which is exactly what it looked like — so a gradient washes the art into the panel before its
own black begins, and no line's art shows an edge wherever it happens to end.

**The app is portrait only.** Every screen here is a column, and landscape gives them a short
wide window that suits none of them. It also stops the window flipping when adb drives the
phone, which resets the rotation lock.

**The invariant that needed guarding.** The API hands over two flat lists — three minors and nine
majors, both in tier order — and nothing in them marks where a tier ends. A grid whose tiers slip
by one draws perfectly and shows the wrong traits under the wrong tier, so `TraitGrid.Split`
checks the shape and returns nothing rather than half a grid. A specialization with more than
three tiers keeps the three it understands: three correct beats four guessed.

The emblem and the background both come from `/v2/specializations`, which the app was already
calling — nothing extra to fetch or bundle. The grid does cost one more request per character:
every trait in the line rather than only the three taken.

**Specializations and traits are now kept.** They are reference data that changes only with a
game build, so they go through the offline reader like professions and stories, keyed per
character. A build opened once reads back with no connection.

### Several PCs, and an address nobody has to maintain

The bridge used to pair with exactly one PC, by typed IP and typed code. Both fields go stale on their
own schedule and neither says so usefully. Verified the hard way on 2026-09-07: the phone held
`10.1.17.100:47623` from another network *and* the code `PFF2-1A8F` against a bridge that had
since rotated to `H9HP-ZXJ6`. A stale code and an unreachable host present identically — "no
bridge answered" — so the diagnosis costs a round of guessing every time.

The reframe the whole design rests on: **the address is a cache, not a setting.** Identity,
address and secret were three separate things collapsed into two text boxes.

| concept | before | now |
| --- | --- | --- |
| *which* PC | nothing; the code doubled as identity | stable `Id`, plus an editable `Name` |
| *where* it is | typed, went stale | discovered, cached per network, never typed |
| *proof* it is that PC | the code, sent blind to whatever answered | bridge proves itself first |

### A short code cannot answer a challenge, and that decides the shape

`CodeAlphabet` is Crockford base32 and `NewPairingCode` uses eight significant characters:
32^8 is about 2^40. If `/hello` answered an unauthenticated nonce with `HMAC(code, nonce)`,
anything on the LAN could take one `(nonce, proof)` pair and brute-force the code **offline** in
hours. That is not a reason to skip authenticating the bridge — it is the reason pairing by QR
is load-bearing rather than a convenience. A QR carries 256 bits without anyone reading it
aloud; the typed code stays eight characters because it is read from a screen across a room.

So there are two tiers, deliberately:

- **QR-paired** — high-entropy secret, `/hello` answers a nonce with a proof, and the phone
  verifies before sending anything. Discovery is safe because an impersonator cannot forge it.
- **Typed** — identified by `Id` only, trusted on first use, and the phone asks once when a
  known bridge turns up at a new address. No weaker than today, and it keeps a path open when
  the camera or the QR fails.

### Discovery, in the order it is tried

1. **The cached address for this network.** Parallel, and ~4ms when it hits, because that is
   what `/hello` costs. Covers every case where nothing actually moved.
2. **One UDP broadcast** on `DiscoveryPort`, matched by `Id`.
3. **The gateway, over TCP.** One probe. On a Windows mobile hotspot the machine sharing the
   connection *is* the gateway, so on the setup where the address churns most the bridge sits
   somewhere the phone can read off its own link properties. Taken from the route table rather
   than guessed as `x.x.x.1`.
4. **A bounded sweep of the /24, over TCP.** 254 hosts, 32 at a time, 400ms each — about three
   seconds worst case. Undefeatable by a network that drops broadcast, because it uses the same
   transport that carries a paste.
5. **Pick.** One answer, use it. Several, prefer the one whose `/game` reports `IsLive` — you
   are sitting at the PC that is running the game — else most recently used.

**Steps 3 and 4 exist because step 2 does not work on this phone, and that took measuring to
establish.** `Network.BindSocket(DatagramSocket)` appears not to take effect: the probe is
handed to the kernel without error, leaves by the *default* network — cellular, whenever the
hotspot has no internet — and vanishes. A UDP send is fire-and-forget, so nothing anywhere
reports a failure. Ruled out first, in this order:

- The beacon answers both unicast and broadcast when probed from the PC itself.
- `Get-NetUDPEndpoint` shows it bound to `0.0.0.0:47624`, and the firewall has inbound **UDP**
  and **TCP** allow rules for `guildwarscompanion.bridge.exe` on all profiles.
- The phone was on `192.168.137.191` with the subnet read correctly, and unicast UDP straight
  to the gateway went unanswered too — while TCP to that same host one port down worked.

`Network.OpenConnection(URL)` is the API that does work, which is why every step that had to be
added is TCP. The broadcast is kept as an opportunistic fast path for ordinary LANs; nothing
depends on it.

**A discovery that cannot explain itself is one nobody can fix.** The first version swallowed
its exceptions, so a phone demonstrably on the right network reported nothing at all and there
was no way to tell "could not look" from "looked and found nothing". `IBridgeDiscovery.Problem`
now records whether the socket opened, whether the probe was sent, and to which targets — and
that message is what turned this from guesswork into three eliminations.

### Two things that look like details and are not

**The network key must be the Wi-Fi `/24`, not the SSID.** `WifiInfo.SSID` needs
`ACCESS_FINE_LOCATION` on Android 10 and up, and requesting location permission in order to
paste an emote is indefensible. The subnet comes free from the `Network`'s `LinkProperties`,
which `WifiBridgeTransport.FindWifi` already holds. Without a per-network key the same desktop
thrashes between its home address and its hotspot address.

**The pairing code belongs in `SecureStorage`.** It sits in plain `Preferences` today
(`BridgeClient.HostKey`/`CodeKey`) while the API key goes to the Android Keystore — and the code
grants clipboard-write access to somebody's PC. The per-PC rewrite is the moment to move it.

### Shape of the work, as built

- **Protocol** (shared file, both sides): `DiscoveryPort`, `NewBridgeId`, `NewPairingSecret`,
  `Proof`/`ProofsMatch` reusing the timing-safe compare, `RequestHead.Query`, and the
  `BridgeIdentity` and `BridgeCard` records. `PathIs` already strips query strings, so
  `/hello?nonce=` needs no parsing work.
- **Bridge**: `BridgeConfig` gains `Id`, `Name` (defaulting to `Environment.MachineName`) and
  `Secret`, migrated on load the way `MinimiseToTray` already is — null means nobody has said.
  `/hello` returns the identity; a new `BridgeBeacon` answers UDP probes with `Id`/`Name`/`Port`
  and never with the secret.
- **Phone**: `KnownBridges` replacing the two loose preference strings, `BridgeFinder` for the
  resolution order, QR scanning generalised out of `ScanKeyPage`.
- **UI**: Settings grows a list of PCs rather than one address; `Sent /beckon to PC` becomes
  `Sent /beckon to Desktop`, because with several paired the old sentence is not specific enough
  to trust the automatic pick.

`QRCoder` for rendering on the desktop side — pure managed, no native dependency, which is what
the Linux target needs.

---

## Why the test project is shaped the way it is

It references Core, which is plain `net10.0` and loads in any test host. Before Core existed the
app was an android project no test host could load, so pure sources were compiled in one by one
with `<Compile Include=... Link=...>`. The bridge's protocol files still are, because the bridge
is a desktop app the tests do not reference.

That old constraint is why `Coin`, `Ids`, `PriceRules`, `StorageTally`, `CraftPlan`, `CraftTree`,
`BossAlerts` and `AchievementProgress` live apart from the services that use them. It no longer
forces the split, but pure logic away from HTTP and SQLite is still the cheapest thing to test.
A service that needs the network wants a seam first, the way `Gw2Tiles` takes a message handler
and a wait.

`BuildChecker` was the long-standing exception, and it turned out to need almost nothing: its
only tie to MAUI was `CheckFinding.EdgeColor`, which always returned the same brass. That was
never a property of a finding — it is a decision belonging to the view, and moving it into the
XAML freed all fourteen game rules to be tested. `Gw2Models.cs` compiles into the test project
alongside it, being pure records.

---

## Feasible, but deliberately not planned

**WvW match standings** and **guild screens.** Both scopes are held (`wvw`, `guilds`) and both
are buildable. They are wide features with narrow daily value unless the account actually plays
WvW or runs a guild.

**PvP screens.** Buildable, and in the same place as WvW: narrow unless the account plays PvP.
The `pvp` permission reads `/v2/pvp/stats` (wins, losses, rank), `/v2/pvp/games` (recent
matches) and `/v2/pvp/standings` (the season's league). An older note here called it blocked
because PvP equipment was missing from the character response; it is not missing, only kept in
`equipment_tabs[].equipment_pvp`, and the Gear tab already shows it. See "Equipment templates"
under Done.

**Copy chat links.** Considered and dropped on 2026-08-21, not for lack of feasibility. The
API returns `chat_link` on every item, and the encoding is trivial enough to generate locally
from an item id — six bytes, base64, `[&AgEJTQAA]` — which avoids a cache column and allows
an arbitrary quantity, unlike the API's canned quantity-of-one string.

It was dropped because **Guild Wars 2 has no Android client**. The app runs on the phone and
the game runs on the PC, so copying to the phone's clipboard puts nothing in the PC's paste
buffer — it only works for someone with cross-device clipboard (Phone Link, a cloud-clipboard
keyboard). Without that it is ten characters to squint at and retype. The share sheet into
Discord or guild chat remains a genuine phone-native use if this is ever revisited.

**Asked again on 2026-09-04: does being on the same network change it?** Only if something on
the PC is listening. Windows exposes no way for a phone to set a remote clipboard, so a LAN
bridge means shipping a second artefact — a tray app in this solution holding a socket open,
pairing code, firewall exception — and the player must remember to start it. An existing
clipboard bridge (KDE Connect is LAN-only and free; Phone Link) does the same job for no code
at all, which is why the emote screen copies to the phone's clipboard and stops there. Parked,
not refused.

Note what a bridge must *not* do: send synthetic keystrokes into the game window. Typing the
line for the player is the version everyone thinks of first. Clipboard plus a human pressing
Ctrl+V is ordinary use of a computer.

Revisited on 2026-09-28: the bridge still does not type, but emotes are now done in the game
through a Blish HUD module using Blish HUD's own chat service, the way modules in its official
repository already do — see "Blish HUD, with the game" under Done. The earlier wording here said
ArenaNet actions accounts over typed lines "cosmetic or not"; that was stated without a source, and
the one-click-one-line modules in Blish HUD's own repository argue against it as a blanket rule.

**Built on 2026-10-02**, because the reason it was dropped had gone: with a bridge paired, a copy
lands on the PC's clipboard. The item page's COPY CHAT LINK sends the link over `/paste`, the
route emotes already proved, and falls back to this phone's clipboard when no PC is paired or
none answers. `ChatLink.Item` builds the code locally, tested against links the API wrote. It
takes a quantity, which the API's `chat_link` cannot, though the page links one for now.

---

## Settled API limits — do not re-litigate

**Elite specialization training is dead.** `/v2/characters/:id/training` has returned `[]` in all
cases since the End of Dragons "What Lies Beneath" release — a documented API bug, not an
account quirk. The roster blob's `training` field is empty for the same reason. So "120 hero
points to finish this spec" cannot be built, however well the profession side supports it:
`/v2/professions` still lists every track and step.

**Hero points taken are dead too, since the same release.** `/v2/characters/:id/heropoints` answers
`[]` for every character — the wiki's note on the endpoint, and measured on 2026-10-05: 0 for a
level-80 Deadeye with 111 hours played. So nothing can say which hero points a character still
needs. The compass's "hide what is done" has silently hidden no hero point since; its journal line
reads "0 hero points done", which is the bug, not the account. Today's "Here:" line treats an empty
answer as no answer and leaves hero points out, rather than claiming every one is left.

Worth recording separately, because it nearly caused a different mistake: in a training track a
step's `cost` is **cumulative, not incremental**. Daredevil's nineteen steps run 30, 40, 55 … 250,
and 250 is the whole elite specialization — the number every player knows. Summing them gives
2,535, which is wrong by an order of magnitude and looks perfectly plausible to anyone who has
not played.

Each of these was checked against the live API and found impossible. They keep coming back up
as feature ideas, so they are written down.

| Wanted | Reality |
|---|---|
| Character portrait or render | No endpoint. Nothing returns an image of a character. |
| Currently played character | Not exposed by the API. **Answered by MumbleLink** via the bridge; the `ActivityStore` inference remains the fallback with no PC. |
| Current map or position | MumbleLink only, which is PC-only — **and there is now a PC-side process**, so the bridge reads it and the phone asks the bridge. The block names the map by id; the bridge resolves that against `/v2/maps/{id}`, which is the only request it makes. |
| Event schedule | Not in the API. The world-boss timetable in `EventSchedule.cs` is hardcoded. **Where** an event happens is a different question — see the row below. |
| Boss spawn coordinates | **No longer a limit.** True of everything under /v2, and false of `/v1/event_details.json`, which is alive and publishes a centre for all 5417 events. See `### Boss arenas`. |
| Item search | No search endpoint. Hence the local `Gw2Cache` index. |
| Meta or build-quality data | No notion of *good* exists in the API. |
| Machine-readable skill drawbacks | Skill facts carry no polarity and no target. |
| Reliable stat totals | Runes and sigils describe themselves in prose; trait effects are absent. |
| Complete emote list | `/v2/emotes` lists 14 of the game's 68 — 14 of 43 unlockables and none of the 25 free ones. `/v2/account/emotes` can only answer for those 14. Hence the hardcoded table in `Emotes.cs`. |
| Hero points | `/v2/characters/:id/heropoints` returns `[]` even for completed challenges. Confirmed API bug, reported upstream. The tile is hidden rather than showing a wrong zero. |

Build ratings and gear recommendations were built despite falling in the "no notion of good"
category, because they were explicitly asked for. That is why both carry visible caveats on
screen, and why `GearAdvisor` prices only majority-rune/sigil completion instead of inventing
a target set. Keep those caveats.

---

## Other API facts worth not rediscovering

- An invalid key returns **400** with `{"text":"invalid key"}` — *not* 401. `Gw2Client` maps
  this to `Gw2ErrorKind.InvalidKey`; without that mapping it surfaces as "no connection".
- Character builds live in `build_tabs` / `active_build_tab`. There is no top-level
  `specializations` or `skills` field, whatever the wiki implies.
- Bulk id requests cap at **200** ids. `GetBulkAsync` chunks for this.
- **Rate limit: a token bucket per IP — 300 burst, refilled at 5 per second (300/minute).**
  Exceeding it returns 429. An earlier note here said "roughly 600 requests/minute", which is
  double the real allowance. `Gw2Client` now queues every request through a matching bucket,
  so no caller has to think about it.
- Commerce endpoints are cached 5 minutes server-side.
- The schema is pinned to `2024-07-20T01:00:00.000Z`. Unpinning changes response shapes.
- **The Wizard's Vault account endpoints do not reset on the reset.** `/v2/account/wizardsvault/
  {daily,weekly,special}` keep serving the previous cycle's objectives and progress until the
  player next logs into the game. This is a known, reported API defect, not something the app
  can fix — [forum](https://en-forum.guildwars2.com/topic/170383-wizards-vault-api-only-returns-correct-data-after-reset-login),
  [gw2-api#126](https://github.com/gw2-api/issues/issues/126). Nothing in the response says
  which cycle it belongs to.

  The app detects it instead: `SnapshotStore.RecordAsync` records when a payload last
  *changed* rather than when it was last fetched, and data unchanged across the last reset
  means the account has not rolled over. Both the Vault screen and the Today screen say so
  rather than presenting expired objectives as current.

  **The limitation is inherent:** detection needs an observation from before the reset. In
  steady state the 30-minute background poll provides one; on a fresh install, the first fetch
  after a reset becomes the baseline and that cycle cannot be judged.

## Release builds

`.\publish-apk.ps1` builds the sideload APK from `GuildWarsCompanion.Avalonia.Android` with the
`SideloadApk` profile (see `Properties/PublishProfiles/SideloadApk.md`): trimmed, signed with
the app's own key (kept outside the repository, refused without), about **36 MB**. Smoke-tested trimmed on 2026-10-02: start, menu, timetable,
a boss opening its map with tiles and markers, then a session against the bridge and MumbleSim.

The MAUI app's 22 MB build was verified the same way on 2026-08-24, having gone unexercised
while the app roughly tripled in size. What made that hold carries over: compiled bindings
(`AvaloniaUseCompiledBindingsByDefault`), formatting in view models, JSON through
`Gw2JsonContext`.

Worth repeating after any run of feature work: trimming breaks at runtime, not at build time.

## Device debugging

- The package is `com.mpsoft.gw2companion`; `adb shell dumpsys package com.mpsoft.gw2companion`
  prints the build's version, `1.0.<commits>+<hash>`, to compare with `git log -1`.
- Debug installs need `-p:EmbedAssembliesIntoApk=true`. A plain `-t:Install` uses Fast
  Deployment, and a stale `files/.__override__` on the device makes every managed assembly
  fail to load — the app dies on the splash screen with `open_from_bundles` warnings.
- Driving the phone over adb re-enables auto-rotate, undoing a manual portrait lock. Re-assert
  with `adb shell settings put system accelerometer_rotation 0`.
