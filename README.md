# Wirenook Shape Collector

Lay out a collect-the-shapes course in any VRChat world, from your avatar,
for whoever is in the instance. Inspired by crappy VRChat games everywhere.

The game: somewhere there is a gate. To open it, a player has to walk into
a blue pyramid, then a cube, then a ball, in that order, and only the next
one is ever on show. A sign says which shape is next, as a triangle, a
square or a circle, for anyone who is stuck, and so does a lantern on top
of each gatepost. When the ball is taken the gate goes away. The gate has
no collider, so nothing stops anyone walking through it; you can pretend it
does, if that makes you happy.

Package: `net.wirenook.shape-collector`. It is one package for two
targets, and each side compiles only where what it needs is installed:

| Side | Folder | Needs |
| --- | --- | --- |
| On an avatar: the course laid out from a menu | `Editor/` (`ShapeCollectorBuild`), `Generated/`, `Models/`, `Prefabs/` | VRChat Avatars SDK 3.10.6 or newer, [Modular Avatar](https://modular-avatar.nadena.dev/), [`net.wirenook.assets`](https://github.com/CassidyPrather/wirenook-assets), [`net.wirenook.world-sync`](https://github.com/CassidyPrather/world-sync) |
| In a world: the course built into the scene | `Editor/World/` (`ShapeCourse`) | VRChat Worlds SDK 3.7 or newer, [`net.wirenook.contacts`](https://github.com/CassidyPrather/contacts) (for its skill checks) |

`package.json` therefore depends on `com.vrchat.base` alone: the Avatars
and Worlds SDKs cannot share a project, so a package for both cannot
require either. Install the row you want yourself. With neither row
installed the package imports and does nothing.

**A pilot.** This package exists as much to put the world-sync bus under
load as to be played, and none of it has run in the VRChat client. See
[What has and has not been checked](#what-has-and-has-not-been-checked).

## Using it

Drag `Prefabs/Wirenook Shape Collector.prefab` onto an avatar. It adds a
"Shape Course" menu:

| Entry | What it does |
| --- | --- |
| Place ▸ Triangle, Square, Circle | Puts that shape (the pyramid, the cube or the ball) a step in front of you at chest height, where it hovers and turns. |
| Place ▸ Gate Left, Gate Right | Puts one gatepost on the ground in front of you. Once both are down, a lattice of fangs and braces fills the gap between them, however wide. |
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

## In a world

The same game as part of a world's scene, for a world built from editor
code. `ShapeCourse.Build` (`Editor/World/ShapeCourse.cs`, namespace
`Wirenook.ShapeCollector.World.Editor`) makes it out of the world
skill-check behaviours of `net.wirenook.contacts`; the package has no world
behaviour of its own.

```csharp
var options = new ShapeCourseOptions
{
    Gate = new[] { bars },          // objects that bar the way until the last shape is taken
    StartOver = button,             // something with a collider; becomes the "start over" control
};
options.Shapes.Add(new ShapeCourseShape("Pyramid", new Vector3(-2.5f, 0f, 0f), shown => AddPyramidMesh(shown)));
options.Shapes.Add(new ShapeCourseShape("Cube", Vector3.zero, shown => AddCubeMesh(shown)));
options.Shapes.Add(new ShapeCourseShape("Ball", new Vector3(2.5f, 0f, 0f), shown => AddBallMesh(shown)));

ShapeCourseResult course = ShapeCourse.Build(root, options);
```

- **Each shape is a skill check** that completes at a touch and stays
  complete, and that needs two things: a contact carrying one of
  `options.Tags` (by default `Head`, `Torso`, `Hand`, `Foot`, `Finger`,
  the senders every avatar has, as on the avatar side), and the shape
  before it to be complete. So only the next shape counts.
- **Only the next shape is on show.** What you add in a shape's `Make` is
  shown from when the shape before is taken until it is taken itself.
- **The gate**: `options.Gate` are shown until the last shape is taken,
  `options.Open` from then on. Unlike the avatar's gate, one made of
  objects with colliders does stop people.
- **Start over**: pressing `options.StartOver` resets every shape, for
  everyone.
- **`options.MasterTags`**: contact tags that take a shape out of turn or
  put a taken one back, for whoever runs the game.
- **`options.Feedback`** is called for each shape with the object its
  check is on and the check: the place to add a sound.
- **`options.Node`** is how the course finds or makes its own objects under
  the root (per shape: an object of the shape's name holding `Check`,
  `Turn`, `Turn/Shape` and two switches). A generator that tracks what it
  made passes its own; the default finds a child by name or creates it, so
  building twice changes nothing.
- **The result**: `Taken` (the check of each shape; the last one being
  complete is the gate being open), `Spots` (each shape's object, to add a
  mark on the ground to), and `Components` (everything the course put on
  any object, for generators that remove what they did not make).

It is synced as every skill check is: one owner decides, four values are
sent when something changes, and a late joiner finds the shapes that were
taken taken. There is no sign and there are no lanterns; show progress with
a skill check's outputs on `Taken` if you want them.

The world in
[wirenook-world](https://github.com/CassidyPrather/wirenook-world) calls
it for its shape course, and its play tests cover it: a touch out of turn
takes nothing, each shape appears in turn and goes when touched, the gate
opens after the ball, and starting over puts the first shape and the gate
back.

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
seven. The mesh's bounds are the bus's whole range, so it is drawn wherever
its bones are; the object those bounds hang from rests at no size, so they
are a point until there is a course to show.

**A gate of any width.** The gate is two posts and a lattice between them:
an upper jaw of fangs hung from a beam, a lower jaw standing on a rail, and
braces and two lines of light behind. Nothing holds the lattice in place
and nothing measures the gap. Each post has two bones under it, one for
each jaw, and every vertex of the lattice is weighted partly to a bone
under one post and partly to its partner under the other, which puts it
that share of the way across wherever the posts are. A fang's vertices all
have one share, so it keeps its shape and only its place changes; a beam's
two ends have different shares, so it stretches. Scaling both bones of a
pair to nothing leaves the lattice no area, which is how it stays out of
sight until both posts are down.

The posts stand along the world's axes and do not turn to face along the
gate, so only the lattice's upright faces look the same whichever way the
gate runs. That is why the beams are flat strips with crossed plates
instead of boxes, and the fangs are turned shapes.

While the gate stands, the two jaws rise and fall a few centimetres against
each other, a quarter of a beat apart at their two ends, and the crown of
spikes on each post turns slowly and swells, one crown clockwise and the
other not. All of that is animation on bones under the posts; the posts
themselves are held by constraints and cannot be animated.

Each post's lantern shows the sign's triangle, square or circle on all four
of its sides, because a post has no front. It does not show the sign's
tick: by the time the gate is open the posts are gone.

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
| Triangles | 1,454 |
| Bones | 26 |
| Constraints | 11: six hold the bones, five are the bus |
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
  object that exists. This was done for the gate of five bars that came
  before the lattice, and has to be done again for the lattice, the
  lanterns and the crowns.

Checked in Blender only, by posing the model's bones:

- That the lattice fills the gap between the posts at 2.4 m and at 8 m, and
  with the gate running along, across and diagonally to the way it was
  modelled, and that it has no area when both bones of each pair are scaled
  to nothing.

Not checked, because only the client can:

- The solid shapes, the lattice, its movement, the crowns and the lanterns
  have not been seen in VRChat at all. In particular nothing has shown
  that Avatar Optimizer keeps the curves on the bones under the posts
  (see `SKILL.md`), or how the client shades the lattice's flat strips.
- Everything the editor check stands in for: that contacts read positions
  in metres, that constraints with two weighted sources sit at their
  weighted average, that parameter drivers copy as assumed on other
  people's clients, and that avatar parameters arrive together.
- The game itself: walking into a shape, the sound, how it feels.
- Whether the client draws a mesh with bounds a kilometre across, and what
  it costs if it does.
- The sign and the shapes face along the world's axes, not towards anyone.
  The shapes turn; the sign is drawn on both faces; the lanterns show
  their picture on all four sides.

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
