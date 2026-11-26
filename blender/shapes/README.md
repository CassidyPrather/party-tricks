# Prop sources

Blender sources for the models in `Packages/net.wirenook.party-tricks/Models/`.
Each prop is generated from code, saved here as `.blend`, validated, exported as FBX.

| Source | Prop | Bones | Notes |
| --- | --- | --- | --- |
| `shapes.blend` | `Shapes`: a pyramid, a cube and a ball 0.5 m across, two gate posts 2.7 m tall with a lattice between them, and a sign, as one model. 1,454 triangles | `Shapes` (carries nothing); `Tri`, `Square`, `Circle` (centre of each shape) with `TriSpin`, `SquareSpin`, `CircleSpin` under them; `PostA`, `PostB`, `Sign` (each on the ground); under each post, here `PostA`: `CurtainA` (on the post's axis at the top beam, 1.75 m), `TeethA` (at the bottom rail), `CrownA` (at the brim of the crown), `LampTriA`, `LampSquareA`, `LampCircleA` (centre of the lantern); `SignTri`, `SignSquare`, `SignCircle`, `SignOpen` (centre of the board, under `Sign`). 26 in all | Unity holds `Tri`, `Square`, `Circle`, `PostA`, `PostB` and `Sign` at places in the world, so they stand in a row here only to be looked at, the posts 2.4 m apart. The `Spin` bones carry the shapes and turn. The lattice is shared between `CurtainA` and `CurtainB` (beam, upper fangs) and between `TeethA` and `TeethB` (rail, lower fangs); see below |

## The gate's lattice

Everything between the posts is weighted to two bones at once: `CurtainA`
and `CurtainB`, or `TeethA` and `TeethB`. A vertex with share `s` has weight
`1 - s` on the `A` bone and `s` on the `B` bone, which puts it that share of
the way from one post to the other wherever Unity stands them. `Span`,
`sheet`, `girder` and `fang` in `build_props.py` model with shares.

- A piece whose vertices all have one share (`Span.rigid`) keeps its shape.
  A face whose corners have different shares stretches with the gap.
- The posts do not turn to face along the gate. An offset straight up or
  down from the line between the bones looks the same whichever way the
  gate runs; an offset to the side does not. Stretching faces are therefore
  upright strips (`sheet`, drawn on both sides), and rigid pieces are
  turned about the upright.
- Scaling both bones of a pair to nothing must leave every face with no
  area, which holds as long as nothing in the lattice is weighted to any
  other bone.
- Unity bobs each of the four bones 4 cm up and down. The upper fangs stop
  13 cm above the rail and the lower fangs 15 cm below the beam.

The shared kit assumes one bone per vertex. `build_props.py` replaces the
kit's `assemble` and `_validate_skin` to write and to allow these shares;
see the Contract below.

## Rebuild

Needs Blender 5.0 and `wirenook-assets` cloned beside this repo (shared kit: `../wirenook-assets/tools/blender/wirenook_props.py`).

```sh
blender -b --python blender/shapes/build_props.py                      # all props
blender -b --python blender/shapes/build_props.py -- --only shapes   # one prop
blender -b --python blender/shapes/build_props.py -- --previews DIR     # also render a preview sheet per prop
```

Exit code 1 and no FBX written if a prop breaks the contract.

Never delete an `.fbx` to force a rebuild. The build replaces each one in a single step; a running Unity that sees a model missing deletes its `.meta`, and prefabs lose the GUID they reference.

## Editing by hand

Fine, as long as the contract holds. Edit the `.blend`, then:

```sh
blender -b --python blender/shapes/export.py -- blender/shapes/shapes.blend
```

Same validation as the build. Note `build_props.py` regenerates the `.blend` from code and overwrites hand edits; move lasting changes into the code, or stop rebuilding that prop.

## Contract

Unity depends on all of this; `wirenook_props.validate` enforces it.

- Objects: armature `<Prop>Rig`, mesh `<Prop>` parented to it, one Armature modifier, no others. Transforms applied, metres.
- Bones: root `<Prop>` at origin (hold point), children `<Prop>.<Part>` as in the table. All point along +Z, no roll.
- Axes: forward is Blender -Y, up is +Z. Exported so Unity sees forward +Z, up +Y, no rotation or scale on any object or bone.
- Skinning: every vertex weight 1.0 on exactly one bone, except the gate's lattice, whose vertices may be shared between the `A` and `B` bones of one pair (`SPANS` in `build_props.py`), the two weights summing to 1. Unity hides a prop by scaling its root bone to zero.
- Colour: one UV layer, `UVMap`. All loops of a face on one swatch centre of `wirenook-palette.png` (names in `wirenook-palette.json`).
- Materials: `wirenook` for solid parts, emissive swatches included. `wirenook-glow` only for light (beams, halos, waves), emissive swatches only.
- Triangle budget per prop: see `PropSpec` in `build_props.py`.
- `Portal.Swirl` is a circle on purpose: largest shape that can spin about forward without leaving the oval frame.
- Style: <https://wirenook.net/style/>. No flowers, no goth.
