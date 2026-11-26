# Wirenook Party Tricks

Things a VRChat avatar does for the people around it: a mallet that goes
bonk, a vanishing act, a mosaic censor, a sign for when you are busy, and
a crash for when you are away.

Package: `net.wirenook.party-tricks`. Needs the VRChat Avatars SDK 3.10.6
or newer, [Modular Avatar](https://modular-avatar.nadena.dev/),
[`net.wirenook.assets`](https://github.com/CassidyPrather/wirenook-assets)
and [`net.wirenook.world-sync`](https://github.com/CassidyPrather/world-sync).

## What is in it

Drag a prefab onto an avatar; Modular Avatar adds its menu entry, parameter
and animator at build time. Each trick works on its own, and
`Wirenook Party Tricks` puts all four behind one "Tricks" menu entry.

| Prefab | What it does | Synced bits | Rank cost |
| --- | --- | --- | --- |
| `Wirenook Bonk Hammer` | A toy mallet in the right hand. Landing its head on someone's head or body bursts into stars and goes bonk. | 2 | 1 contact, 1 audio source |
| `Wirenook Vanishing Act` | You go up in a puff of smoke and a log is left standing where you were. You stay hidden, free to walk off, until you switch it off and come back in another puff. | 67 | 1 mesh, 1 material slot, 1 audio source, constraints and contacts (see `net.wirenook.world-sync`) |
| `Wirenook Censor` | A mosaic over your head or your whole body, and only over you: always, only in mirrors, only in photos, in either, or only in a mirror that is in a photo. Needs the avatar's materials to mark the stencil; see below. | 8 | 1 mesh, 1 material slot, 1 constraint; one copy of the screen per camera while it shows |
| `Wirenook Busy Sign` | A little window with three dots hovers by your head: you are looking at something nobody else can see. | 1 | none |
| `Wirenook Away` | While you are away the avatar crashes: the body locks, the mesh glitches, an error window reading AFK stands beside it, a device-unplugged cue plays and a low buzz sets in. On VRChat's own AFK or an "AFK" menu switch. Not part of the Tricks set. | 1 | 1 audio source; about 170 triangles |
| `Wirenook Away (Vanish)` | While you are away the avatar does the vanishing act, and undoes it when you are back. Needs `Wirenook Vanishing Act` (or the Tricks set) on the same avatar. Not part of the Tricks set. | 1 | none of its own |

The hammer, the smoke and the busy sign are toggled by scaling a bone, so
an optimiser such as
[Avatar Optimizer](https://vpm.anatawa12.com/avatar-optimizer/) can merge
them into the avatar's main mesh.

### Bonk hammer

The mallet's head carries a contact that fires when another avatar's
standard `Head` or `Torso` contact (VRChat gives every avatar these) enters
it at more than 0.6 m/s; resting the mallet on someone is not a bonk. Your
own head counts.

Only the wearer's client keeps score. When it sees a hit it raises one
synced bit for a quarter of a second, and everyone, the wearer included,
plays the stars and the sound from that bit. That costs a bit, and it means
nobody hears a hit you did not land or misses one you did, which each
viewer working it out from their own copy of your arm could not promise.

After placing it, fit it to the hand with
`Tools > Wirenook > Assets > Fit Hand Grips To Selected Avatar`. The handle
comes out of the thumb side of the fist and the head strikes the way the
fingers point.

### Vanishing act

The log is dropped where you stand and its place is synced (it is a synced
anchor from `net.wirenook.world-sync`), so someone who arrives halfway
through the trick sees the log where you left it rather than at your feet.

You are hidden by shrinking the hips bone to nothing, which takes the whole
skinned body with it and anything worn or held. That is the one animation
in this package that has to name part of the avatar itself: it expects the
hips at `Armature/Hips`. For a rig that keeps them elsewhere, pass the path
to `PartyTricksBuild.Vanish` and rebuild. Your nameplate stays; this is a
trick, not a way to hide.

### Censor

`Wirenook/Censor` redraws you, where you show inside a rounded volume, in
square blocks 3.5 cm across, each taking the colour at its block's centre.
The room showing past you inside the volume's outline is left alone. The
grid is anchored to the volume's centre, so it travels with you.

**Your materials have to mark the stencil.** The shader tells you from the
room by a stencil bit: your materials set it on every pixel they draw, and
the mosaic is drawn only where it is set. The bit is 128
(`ToonMaterial.AvatarStencil` in `net.wirenook.assets`), written with a
write mask of 128 so the other seven bits stay as they were. The materials
`net.wirenook.assets` generates do this already: an avatar built on
`ToonMaterial.Configure`, and every wirenook prop except the ones made of
light. On any other avatar the censor draws nothing until you do one of
two things:

- Set the stencil on each of the avatar's materials: Ref 128, Write Mask
  128, Comp Always, Pass Replace, and the same for the outline if the
  shader has a separate one. lilToon and Poiyomi both have these settings;
  `ToonMaterial.MarkStencil` sets them on a lilToon material from code.
- Or set `_Stencil` to 0 on `Generated/censor.mat`. The test then always
  passes and everything inside the volume is pixelated, room included, as
  under a broadcaster's censor bar. This is how the censor worked before.

What follows from doing it with the stencil:

- **What is drawn over you later.** The stencil records that you were
  drawn at a pixel, not that you are still what shows there. Something
  nearer than the volume hides the mosaic, because the volume is depth
  tested. Something inside the volume, drawn after you (a friend's hand in
  front of your face, anything transparent, your own beam or halo), is
  pixelated where it overlaps you and sharp where it does not. Whether an
  opaque thing counts as "after you" is up to the order the renderer picks.
- **Your outline stays sharp.** A block whose centre is off you gives your
  pixels in it the colour of whatever is behind. Blocks along your edge
  look bitten into, and thin parts (strands of hair, fingers) mostly
  disappear into the background.
- **Anything else that sets bit 128** looks like you to the shader:
  another avatar in these same materials, or one whose own stencil effect
  writes 128 to 255 with a full write mask. Where it shows inside the
  volume's outline, at any distance behind you, it is pixelated too. That
  is the old behaviour, confined to those pixels.
- **A stencil effect that reads all eight bits** (a world's portal, another
  avatar's) sees its value changed by 128 where you are drawn over it and
  will usually skip those pixels. Where such an effect draws over you and
  clears the bit, the mosaic is missing.
- **Mirrors and cameras** draw the scene again into a picture of their
  own, with their own stencil, so the same thing happens there as on
  screen, provided that picture has a stencil buffer. If VRChat renders one
  without, the test passes everywhere and the censor pixelates the whole
  volume in that view. This has not been checked in the client.

The volume follows your head or your hips, sized for a figure about 1.7 m
tall with a large head; `CensorAreas` in `Editor/PartyTricksBuild.cs` holds
the sizes. The "Whole Body" volume covers a figure standing or sitting with
its arms down, not one with its arms spread.

When it applies is read from the flags VRChat sets while it draws a mirror
(`_VRChatMirrorMode`) or a handheld camera or screenshot
(`_VRChatCameraMode`). Someone standing inside the volume sees nothing of
it, so your own first-person view is never pixelated. The stencil does
not come into this: "In Mirrors", "In Photos" and the other choices work as
they did.

**This is a look, not a privacy measure.** A viewer whose safety settings
block your shaders or your animations sees you plainly. It needs a copy of
the screen (a grab pass), which is cheap once and adds up if many avatars
in view do it.

### Busy sign

VRChat does not tell an avatar when its wearer has a desktop overlay or a
dashboard open, so the sign is one switch, `WN/Busy`, in the menu. It is an
ordinary synced parameter, which also makes it settable over
[OSC](https://docs.vrchat.com/docs/osc-avatar-parameters) at
`/avatar/parameters/WN/Busy`: a program that knows when an overlay is up
can raise the sign for you. This package does not include one.

### Away

`Wirenook Away` is what the avatar does while its wearer is not there. It
starts on either of two things: VRChat's own `AFK` (a headset taken off,
the system menu, the End key) or the "AFK" switch it adds to the menu,
`WN/Away`. Then:

- **The body locks.** Every tracked part is handed from tracking to
  animation and locomotion is switched off, so the avatar stands where it
  was left, in VRChat's standing pose held dead still, instead of slumping
  after a headset on a desk. Once every six seconds it flicks to a T-pose
  for an eighth of a second.
- **The mesh glitches.** Level slices of the figure tear sideways, the
  whole surface snaps to a coarse grid in bursts, and a vertex in a few
  hundred is thrown out as a spike, re-rolled eleven times a second: hard
  as you leave, a simmer with one jolt each time round while you are gone,
  hard again as you come back.
- **A window says why.** A desktop error box reading AFK stands 30 cm out
  from your chest on the side of whoever is looking, facing them. Each
  viewer has it on their own side, and it folds away for anyone standing
  inside the avatar, which keeps it out of your own face.
- **It sounds like a device coming unplugged**: two notes down as you go,
  a low mains buzz while you are gone, the two notes back up on return.

The glitch and the window are drawn by the avatar's own material, not by
anything this prefab adds: the shader `net.wirenook.assets` generates its
materials with (`Wirenook/lilToon`) has a glitch amount and can unfold
"signs", and the prefab animates those properties on a stand-in for every
renderer of the avatar, which the build resolves to the real ones. The
window is 170 triangles of the avatar's own mesh in palette colours, so it
costs no mesh and no material slot; folded away it is not even a speck.
The avatar's own animator may read `WN/Away` beside `AFK` to add something
of its own, as cass does (its eyes go to a small error screen).

On an avatar whose materials are not from `net.wirenook.assets` the body
still locks and the sounds still play; nothing is drawn differently.

### Away, by vanishing

`Wirenook Away (Vanish)` is another way of being away: the vanishing act,
worked by your absence instead of by its switch. It starts on the same two
things and adds the same "AFK" switch to the menu. The prefab holds that
switch and one animator layer, which on your own client sets the vanishing
act's parameter when you leave and clears it when you are back; the
parameter is synced, so everyone sees the puff of smoke, the log left
standing, and nobody there. It costs one synced bit and nothing else.

It does nothing without `Wirenook Vanishing Act` on the same avatar (alone
or in the Tricks set). The act's own switch goes on working beside it;
coming back from being away brings back an avatar that was vanished by
hand before.

An avatar should have one way of being away. Each of the three (these two
and `Wirenook Nap` in `net.wirenook.locomotion`) is marked with its flavour
(`WirenookAfk`), and an avatar whose root names one (`WirenookAfkChoice`,
`net.wirenook.assets`) is built with that one and without the others. An
avatar that names none is built with whatever was dropped onto it.

There was a `Wirenook Away (Lite)`, the crash without its window and
sounds, for avatars with no rank to spare. The nap does that job now and
the prefab is gone.

**The switch cannot mute you.** VRChat lets an avatar read whether the
microphone is muted and never change it. `tools/away_mic.py` is a small
program to run beside VRChat (with OSC enabled) that does it from outside:
it presses the voice button when the switch goes on, and again when it
goes off, but only if the mute was its own, was seen to take, and nothing
has touched the microphone since. Mute or unmute by hand while away and it
leaves the microphone as you set it. It needs "Toggle Voice" on in
VRChat's audio settings. `tools/away_mic.py --test` checks those rules
without VRChat.

### Android and iOS

In a build for Android or iOS (see `net.wirenook.assets`), the censor is
left out: it is drawn by a custom shader, which mobile avatars may not use.
The rest is kept, with VRChat's mobile shaders and without sound. Of the
away feature that leaves the locked body: the glitch and the window are
the shader's, and the window is taken out of the mesh altogether there.

## Limits and unknowns

- Not yet run in the VRChat client. Everything here was checked in the
  Unity editor only: that it builds, that the animations arrive in the
  built avatar pointing at objects that exist, and, for the censor, how the
  shader draws from a set of cameras with VRChat's flags set by hand.
- The bonk rests on contacts and their velocity threshold, which only run
  in the client. 0.6 m/s is a first guess.
- Whether shrinking the hips to exactly zero upsets anything in the client
  (inverse kinematics, PhysBones under the hips) is untried. If it does,
  a very small scale instead of zero is the usual answer.
- The censor has not been seen in a headset, in a real mirror or through a
  real camera. That it now covers only the avatar was type-checked and
  reasoned through, not rendered: the editor preview has to be run again,
  and whether VRChat's mirrors and cameras keep a stencil buffer, and how
  other avatars' stencil effects mix with it, can only be seen in the
  client.
- The away feature was rendered in the editor with its properties set by
  hand (the glitch, the window from every camera, the screen on cass's
  eyes), and its layers were read back from a built avatar. Nothing has
  seen the body lock: whether the pose holds against VRChat's own AFK
  handling when the menu switch outlives it, how the T-pose flick reads,
  and whether the lock is comfortable to come back into in a headset. How
  loud the buzz is against a room is unheard. The spikes reach about 40 cm
  from the body at their worst; whether that is too much in a crowd is a
  matter of taste, and the number is `wirenookGlitch` in the shader.
- Avatar Optimizer dropped the hammer's burst animation when the burst's
  bone was scaled only by the hit layer. Scaling it from the layer that
  toggles the mallet as well keeps it. The cause was not tracked down.

## Rebuilding the generated assets

Everything under `Generated/` and `Prefabs/` is written by
`Tools > Wirenook > Party Tricks > Rebuild`
(`Wirenook.PartyTricks.Editor.PartyTricksBuild.Rebuild`). The models
come from `blender/`; see `blender/README.md`.

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
then push a tag named for that version (`v` and the number). CI makes the GitHub release and attaches the `.zip`
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
