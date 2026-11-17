"""Builds the shape-collector prop: the three shapes, the gate and the sign, as one model.

    blender -b --python blender/build_props.py [-- --only STEM] [-- --previews DIR]

The model is saved as `blender/shapes.blend`, checked against the wirenook
prop contract and exported to the package's `Models/shapes.fbx`. See
README.md beside this file.
"""

import math
import pathlib
import sys

sys.dont_write_bytecode = True

REPO = pathlib.Path(__file__).resolve().parents[1]
MODELS = REPO / "Packages" / "net.wirenook.shape-collector" / "Models"
LIBRARY = REPO.parent / "wirenook-assets" / "tools" / "blender"
if not (LIBRARY / "wirenook_props.py").is_file():
    raise SystemExit(f"The shared prop kit is missing: {LIBRARY / 'wirenook_props.py'}\n"
                     "Clone wirenook-assets beside this repository and run again.")
sys.path.insert(0, str(LIBRARY))

from mathutils import Vector

from wirenook_props import FORWARD, UP, PropSpec, chamfered_rect, main_build, ring_points

X = Vector((1.0, 0.0, 0.0))
BACK = -FORWARD

# How far a flat detail floats off the surface it is drawn on.
HAIR = 0.0008

BARS = 5

# Everything is one model with one bone per thing that moves by itself. Unity
# holds each bone at a place in the world, so where the bones stand here only
# matters for looking at the model: in a row, a metre apart.
SHAPES = PropSpec("Shapes", "shapes", triangle_budget=900, parts={
    "Tri": None, "TriSpin": "Tri",
    "Square": None, "SquareSpin": "Square",
    "Circle": None, "CircleSpin": "Circle",
    "PostA": None, "PostB": None,
    **{f"Bar{bar}": None for bar in range(1, BARS + 1)},
    "Sign": None, "SignTri": "Sign", "SignSquare": "Sign", "SignCircle": "Sign", "SignOpen": "Sign",
})

SHAPE_SIZE = 0.26
SHAPE_DEPTH = 0.07
POST_HEIGHT = 1.9
SIGN_HEIGHT = 1.45


def outline(name, centre, size):
    """The outline of a shape in the plane facing forward: a triangle, a square or a circle."""
    centre = Vector(centre)
    if name == "Tri":
        return [centre + (X * math.sin(a) + UP * math.cos(a)) * size * 1.15 for a in (0.0, math.radians(120.0), math.radians(240.0))]
    if name == "Square":
        return [centre + X * (size * x) + UP * (size * z) for x, z in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    return ring_points(centre, X, UP, size, size, 16)


def build_shapes(mesh):
    """Three shapes to collect, a gate of two posts with bars of light between them, and a sign that names the next shape.

    Each shape has two bones: one Unity holds in place, and under it one
    that turns, carrying the geometry. The gate's bars are held by Unity at
    even steps between the two posts, wherever those are, so the gate is as
    wide as the gap it is put across.
    """
    heads = {}
    row = iter(range(-5, 20))

    def stand():
        return X * (next(row) * 1.0)

    # The shapes: blue, thick enough to read from the side, hovering at chest height.
    for name in ("Tri", "Square", "Circle"):
        centre = stand() + UP * 1.1
        mesh.part(name)
        spin = mesh.part(name + "Spin")
        rings = [outline(name, centre + FORWARD * offset, SHAPE_SIZE) for offset in (SHAPE_DEPTH, -SHAPE_DEPTH)]
        spin.loft(rings, "signal-blue", cap_start="blue", cap_end="blue", smooth=name == "Circle")
        for facing, offset in ((FORWARD, SHAPE_DEPTH + HAIR), (BACK, -SHAPE_DEPTH - HAIR)):
            inner = outline(name, centre + FORWARD * offset, SHAPE_SIZE * 0.55)
            spin.polygon(inner if facing == FORWARD else inner[::-1], "glow-cyan", facing)
        heads[name] = centre
        heads[name + "Spin"] = centre

    # The gate's posts.
    for name in ("PostA", "PostB"):
        foot = stand()
        post = mesh.part(name)
        post.lathe(foot, UP, [(0.0, 0.11), (0.04, 0.11), (0.06, 0.055), (POST_HEIGHT - 0.1, 0.05), (POST_HEIGHT - 0.06, 0.085),
                              (POST_HEIGHT, 0.085), (POST_HEIGHT + 0.07, 0.0)],
                   ["ink", "violet", "violet", "magenta", "magenta", "glow-magenta"], segments=8, cap_start="ink")
        heads[name] = foot

    # The bars between them: light, and thin enough to see through.
    for bar in range(1, BARS + 1):
        foot = stand() * 0.3 + X * 3.0
        part = mesh.part(f"Bar{bar}")
        part.lathe(foot, UP, [(0.12, 0.0), (0.16, 0.022), (POST_HEIGHT - 0.2, 0.022), (POST_HEIGHT - 0.16, 0.0)],
                   "glow-magenta", segments=6)
        heads[f"Bar{bar}"] = foot

    # The sign: a board on a stake, with one bone per thing it can say.
    foot = stand() + X * 1.0
    board = foot + UP * SIGN_HEIGHT
    sign = mesh.part("Sign")
    sign.lathe(foot, UP, [(0.0, 0.09), (0.03, 0.09), (0.05, 0.03), (SIGN_HEIGHT - 0.2, 0.03)],
               ["ink", "violet", "violet"], segments=6, cap_start="ink")
    sign.loft([chamfered_rect(board + FORWARD * offset, X, UP, 0.27, 0.22, 0.03) for offset in (0.02, -0.02)],
              "ink", cap_start="panel", cap_end="panel")
    heads["Sign"] = foot

    def said(name, shapes):
        part = mesh.part(name)
        for facing, offset in ((FORWARD, 0.02 + HAIR), (BACK, -0.02 - HAIR)):
            for points, swatch in shapes(board + FORWARD * offset):
                part.polygon(points if facing == FORWARD else points[::-1], swatch, facing)
        heads[name] = board

    said("SignTri", lambda at: [(outline("Tri", at, 0.13), "signal-blue")])
    said("SignSquare", lambda at: [(outline("Square", at, 0.13), "signal-blue")])
    said("SignCircle", lambda at: [(outline("Circle", at, 0.14), "signal-blue")])
    # Open: a tick.
    tick = [(-0.15, 0.01), (-0.10, 0.06), (-0.04, 0.0), (0.11, 0.15), (0.16, 0.10), (-0.04, -0.10)]
    said("SignOpen", lambda at: [([at + X * x + UP * z for x, z in tick], "leaf")])
    return heads


PROPS = [
    (SHAPES, build_shapes),
]

if __name__ == "__main__":
    main_build(PROPS, REPO / "blender", MODELS)
