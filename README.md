# Wirenook Shape Collector

Lay out a collect-the-shapes course in any VRChat world, from your avatar,
for whoever is in the instance. Inspired by crappy VRChat games everywhere.

The game: somewhere there is a gate. To open it, a player has to walk into
a blue triangle, then a square, then a circle, in that order, and only the
next one is ever on show. A sign says which shape is next, for anyone who
is stuck. When the circle is taken the gate goes away. The gate has no
collider, so nothing stops anyone walking through it; you can pretend it
does, if that makes you happy.

Package: `net.wirenook.shape-collector`. Needs the VRChat Avatars SDK
3.10.6 or newer, [Modular Avatar](https://modular-avatar.nadena.dev/),
[`net.wirenook.assets`](https://github.com/CassidyPrather/wirenook-assets)
and [`net.wirenook.world-sync`](https://github.com/CassidyPrather/world-sync).

**A pilot.** This package exists as much to put the world-sync bus under
load as to be played, and none of it has run in the VRChat client. See
[What has and has not been checked](#what-has-and-has-not-been-checked).

## Using it

Drag `Prefabs/Wirenook Shape Collector.prefab` onto an avatar. It adds a
"Shape Course" menu:

| Entry | What it does |
| --- | --- |
| Place ▸ Triangle, Square, Circle | Puts that shape a step in front of you at chest height, where it hovers. |
| Place ▸ Gate Left, Gate Right | Puts one end of the gate on the ground in front of you. Bars of light fill the gap between the two, however wide. |
| Place ▸ Sign | Puts the sign that shows the next shape on the ground in front of you. |
| Open Course | Shows the course to the instance and starts the game. Until then only you see what you have placed. |
| Start Over | Puts the first shape back. |
| Clear All | Un-places everything. |

Walk to where a thing should go, place it, and go on to the next. Placing a
thing again moves it. Each takes about a second, during which it is being
measured; stand still for it. When the course looks right, open it.

The layout is kept with the avatar: it is still there the next time you
wear it, in the same places in the same world. In another world the same
coordinates mean somewhere else, so clear it and lay it out again.

## How it works

**Six places for the price of one.** The three shapes, the two ends of the
gate and the sign are six positions in the world that every player has to
agree on, including players who arrive halfway through. A synced anchor
from `net.wirenook.world-sync` costs 66 synced bits, and an avatar has 256,
so six of them do not fit. The course rides an anchor bus instead: one
measuring rig, one synced channel the size of one position, and the six
layouts read out over it in turn, round and round. Every other client
copies what it hears into its own copy of each slot. That is 57 bits for
any number of places, and a late arrival has the whole course after one
turn of the broadcast, about six seconds here. The world-sync README has
the details.

**One mesh.** Everything on show is a single skinned mesh with a bone for
each thing that moves by itself, each bone held on its slot by a
constraint. Things left in the world cannot be part of the avatar's own
mesh, which is culled with the avatar, and as separate meshes they would be
eleven. The gate's five bars are held at even steps between the two posts,
which is what makes the gate fit any gap. The mesh's bounds are the bus's
whole range, so it is drawn wherever its bones are; the object those bounds
hang from rests at no size, so they are a point until there is a course to
show.

**Keeping score.** The wearer's client is the referee. Each shape carries a
contact that feels the standard `Head`, `Torso`, `Hand`, `Foot` and
`Finger` contacts VRChat gives every avatar, the wearer's included. Only
the shape that is next counts. The number of shapes taken is one synced
integer, and every client shows the course from it: which shape is out,
whether the gate stands, what the sign says. The one sound, a chime, plays
on every client when that number goes up.

| | |
| --- | --- |
| Synced bits | 66: the bus's 57, one for "open", eight for the count |
| Meshes | 1 skinned, 1 material slot. A third skinned mesh ranks an avatar Medium on PC |
| Bones | 19 |
| Constraints | 16: eleven hold the bones, five are the bus |
| Contacts | 3 that feel other avatars and 4 on the bus, all local-only |
| Audio sources | 1 |

## What has and has not been checked

Checked in the Unity editor:

- The bus, by `Tools > Wirenook > Shape Collector > Verify`
  (`AnchorBusCheck` in world-sync). It runs two copies of the generated
  animator, one as the wearer's client and one as a viewer who arrives
  after everything is placed, standing in for VRChat's parameter drivers,
  contacts and network. It places all six slots, broadcasts them to the
  viewer at several update rates, with synced floats jumping and easing,
  and clears them again, for both ways VRChat might round a number.
- That the prefab builds onto an avatar and every animation arrives at an
  object that exists.

Not checked, because only the client can:

- Everything the editor check stands in for: that contacts read positions
  in metres, that constraints with two weighted sources sit at their
  weighted average, that parameter drivers copy as assumed on other
  people's clients, and that avatar parameters arrive together.
- The game itself: walking into a shape, the sound, how it feels.
- Whether the client draws a mesh with bounds a kilometre across, and what
  it costs if it does.
- The sign and the shapes face along the world's axes, not towards anyone.
  The shapes turn; the sign is drawn on both faces.

Not built: the notes for this package also ask for an "omen" only some
players can see. It is not here.

## Rebuilding the generated assets

Everything under `Generated/` and `Prefabs/` is written by
`Tools > Wirenook > Shape Collector > Rebuild`
(`Wirenook.ShapeCollector.Editor.ShapeCollectorBuild.Rebuild`). The model
comes from `blender/`; see `blender/README.md`.

## Development

Requires Unity 2022.3.22f1 (see `ProjectSettings/ProjectVersion.txt`) via a VPM
client (prefer [ALCOM](https://vrc-get.anatawa12.com/en/alcom/)), and the
[.NET SDK](https://dotnet.microsoft.com/download) 11 for the headless checks.

The Unity project at the repository root is the development harness. It
takes `net.wirenook.assets` and `net.wirenook.world-sync` from checkouts of
[wirenook-assets](https://github.com/CassidyPrather/wirenook-assets) and
[world-sync](https://github.com/CassidyPrather/world-sync) beside this one. The simplest way to get that layout is to
work in [cass-avatar](https://github.com/CassidyPrather/cass-avatar), which
mounts this repository and its siblings as submodules under `modules/`.

```bash
scripts/check-meta.sh          # every visible file has a committed .meta
scripts/check-manifests.py     # package.json sanity across Packages/
scripts/format.sh --check      # dotnet-format over the CI-visible C#
scripts/test.sh                # headless NUnit run of Runtime/Core + Tests
scripts/build-packages.sh      # VPM .zip + .unitypackage per package -> dist/
```

Pre-commit hook: `git config core.hooksPath .githooks`

Release: bump `version` in the package's `package.json`, commit and push,
then create a GitHub release tagged with that version. CI attaches the `.zip`
and `.unitypackage`; the listing at
[CassidyPrather/vpm](https://github.com/CassidyPrather/vpm) picks them up
once this repository is in its `githubRepos`.

## Legal

Copyright 2026 Cassidy Prather <cassidy@wirenook.net>

Everything in this repository henceforth is licensed under
[AGPLv3](https://www.gnu.org/licenses/agpl-3.0.html) (See `LICENSE`) unless
otherwise specified.

As a special exception, the copyright holders give permission to link this
Program with the VRChat SDK and with Unity Technologies' proprietary engine
components, and to convey the resulting work under terms of your choice,
provided that you also convey the Corresponding Source of this Program under
the terms of the GNU Affero General Public License version 3 in all other
respects. (This exception also in `LICENSE_ADDENDUM`.)

## Credits

Descended from https://github.com/vrchat-community/template-package
([VRCHAT DISTRO LICENSE FILE](https://github.com/vrchat-community/template-package/blob/d9cf13fe9f56867cbf7315a4dbbf1901bc1537ec/Packages/com.vrchat.core.bootstrap/License.md))

Third-party material is inventoried in [CREDITS.md](CREDITS.md).
