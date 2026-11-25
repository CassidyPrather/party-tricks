"""Builds the shape-collector prop: the three shapes, the gate and the sign, as one model.

    blender -b --python blender/build_props.py [-- --only STEM] [-- --previews DIR]

The model is saved as `blender/shapes.blend`, checked against the wirenook
prop contract and exported to the package's `Models/shapes.fbx`. See
README.md beside this file.
"""

import contextlib
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

from mathutils import Matrix, Vector

import wirenook_props
from wirenook_props import FORWARD, UP, PropSpec, chamfered_rect, main_build, ring_points

X = Vector((1.0, 0.0, 0.0))
BACK = -FORWARD

# How far a flat detail floats off the surface it is drawn on.
HAIR = 0.0008

SHAPE_NAMES = ("Tri", "Square", "Circle")
ENDS = ("A", "B")

# The gate's lattice hangs between pairs of bones, one of each pair under
# each post: `CurtainA` and `CurtainB`, `TeethA` and `TeethB`. Unity's
# ShapeCollectorBuild.Spans has the same names.
SPANS = ("Curtain", "Teeth")

# Everything is one model with one bone per thing that moves by itself. Unity
# holds each bone at a place in the world, so where the bones stand here only
# matters for looking at the model: in a row.
SHAPES = PropSpec("Shapes", "shapes", triangle_budget=1500, parts={
    **{name + suffix: (name if suffix else None) for name in SHAPE_NAMES for suffix in ("", "Spin")},
    "PostA": None, "PostB": None,
    **{part + end: "Post" + end for end in ENDS
       for part in (*SPANS, "Crown", *("Lamp" + name for name in SHAPE_NAMES))},
    "Sign": None, "SignTri": "Sign", "SignSquare": "Sign", "SignCircle": "Sign", "SignOpen": "Sign",
})

SHAPE_SIZE = 0.25
SIGN_HEIGHT = 1.45

# How far apart the posts stand in the model. Unity puts them anywhere.
GATE_REST = 2.4
# The lattice: an upper jaw hung from a beam at this height, and a lower one
# standing on a rail at this height. Unity bobs each a few centimetres, so
# the tips of one jaw stop well short of the other's beam.
BEAM_HEIGHT = 1.75
RAIL_HEIGHT = 0.09
FANGS = 21
BRACES = 4
# The lantern on top of each post, and the crown that turns above it.
LAMP_HEIGHT = 2.09
LAMP_HALF = 0.17
CROWN_HEIGHT = 2.28


def outline(name, centre, size, across=X, count=16):
    """The outline of a shape in an upright plane: a triangle, a square or a circle."""
    centre = Vector(centre)
    if name == "Tri":
        return [centre + (across * math.sin(a) + UP * math.cos(a)) * size * 1.15
                for a in (0.0, math.radians(120.0), math.radians(240.0))]
    if name == "Square":
        return [centre + across * (size * x) + UP * (size * z) for x, z in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    return ring_points(centre, across, UP, size, size, count)


def level_square(centre, half):
    centre = Vector(centre)
    return [centre + X * (half * x) + BACK * (half * y) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]


def faceted(part, corners, faces, inset=0.5):
    """A solid with flat faces.

    `faces` lists (corner numbers, swatch, accent). A face with an accent
    carries a smaller copy of itself in that swatch, floating a hair off it.
    """
    centre = sum(corners, Vector()) / len(corners)
    for numbers, swatch, accent in faces:
        points = [corners[number] for number in numbers]
        middle = sum(points, Vector()) / len(points)
        out = middle - centre
        part.polygon(points, swatch, out)
        if accent is None:
            continue
        normal = (points[1] - points[0]).cross(points[2] - points[0]).normalized()
        if normal.dot(out) < 0.0:
            normal = -normal
        part.polygon([middle + (point - middle) * inset + normal * HAIR for point in points], accent, out)


# --------------------------------------------------------------------------
# Geometry hung between two bones
# --------------------------------------------------------------------------
#
# A vertex weighted partly to one bone and partly to another sits that share
# of the way between them, wherever they are. So a lattice whose every piece
# has one share fills the gap between two posts however wide the gap is, with
# no constraint and without anything knowing the width: each piece keeps its
# shape and only its place along the gap changes. A piece whose two ends have
# different shares stretches along the gap instead.
#
# The shared kit only knows props whose vertices each follow one bone, so the
# two places where it assumes that are replaced below.


class Span:
    """Two bones, one under each post, and the line between them."""

    def __init__(self, mesh, name, feet, height):
        self.mesh = mesh
        self.part = mesh.part(name + ENDS[0])
        self.bones = tuple(mesh.spec.bone(name + end) for end in ENDS)
        self.ends = tuple(Vector(foot) + UP * height for foot in feet)

    def at(self, share, offset=(0.0, 0.0, 0.0)):
        return self.ends[0].lerp(self.ends[1], share) + Vector(offset)

    @contextlib.contextmanager
    def rigid(self, share):
        """Everything modelled inside is one piece, at this share of the way across."""
        first = len(self.mesh.points)
        yield self.at(share)
        for point in range(first, len(self.mesh.points)):
            self.mesh.hung[point] = (*self.bones, share)


def sheet(corners, swatch, facing):
    """A flat face seen from both sides, each corner hung at its own share of a span.

    `corners` lists (span, share, offset from the span's line). Only offsets
    straight up and down look the same whichever way the gate runs: the
    bones do not turn to face along it, so an offset to the side is a
    thickness for a gate that runs the way the model does and a slant for
    one that runs across it.
    """
    mesh, part = corners[0][0].mesh, corners[0][0].part
    for side in (facing, -Vector(facing)):
        first = len(mesh.points)
        part.polygon([span.at(share, offset) for span, share, offset in corners], swatch, side)
        for number, (span, share, _offset) in enumerate(corners):
            mesh.hung[first + number] = (*span.bones, share)


def girder(span, web, flange, plates):
    """A beam from one end of a span to the other, which stretches with it.

    `web` lists (bottom, top, swatch) bands of the upright strip down its
    middle. `flange` is the half-width of the level plates at each height in
    `plates`, each drawn as two crossed strips so that one of them has width
    whichever way the gate runs.
    """
    for bottom, top, swatch in web:
        sheet([(span, 0.0, (0.0, 0.0, bottom)), (span, 1.0, (0.0, 0.0, bottom)),
               (span, 1.0, (0.0, 0.0, top)), (span, 0.0, (0.0, 0.0, top))], swatch, FORWARD)
    for height, swatch in plates:
        for lift, lean in ((0.0, 1.0), (HAIR, -1.0)):
            a, b = (-flange, -flange * lean, height + lift), (flange, flange * lean, height + lift)
            sheet([(span, 0.0, a), (span, 0.0, b), (span, 1.0, b), (span, 1.0, a)], swatch, UP)


def fang(span, share, direction, length, tip):
    """A bar ending in a blade of light, pointing along `direction` from the span's line."""
    with span.rigid(share) as root:
        span.part.lathe(root, direction,
                        [(0.0, 0.024), (length - 0.36, 0.024), (length - 0.30, 0.058), (length, 0.0)],
                        ["night-1", "soot", tip], segments=4)


def _assemble(builder, heads, kit_assemble=wirenook_props.assemble):
    rig, obj = kit_assemble(builder, heads)
    hung = getattr(builder, "hung", {})
    used = sorted({point for face in builder.faces for point in face.vertices})
    for vertex, point in enumerate(used):
        if point not in hung:
            continue
        near, far, share = hung[point]
        for group in obj.vertex_groups:
            group.remove([vertex])
        for bone, weight in ((near, 1.0 - share), (far, share)):
            if weight > 0.0:
                obj.vertex_groups[bone].add([vertex], weight, "REPLACE")
    return rig, obj


def _validate_skin(spec, rig, obj):
    """The kit's check of the skin, but a vertex may be shared by the two bones of one span."""
    pairs = [{spec.bone(name + end) for end in ENDS} for name in SPANS]
    names = {group.index: group.name for group in obj.vertex_groups}
    counts = dict.fromkeys(("unweighted", "stray", "unnormalised", "shared"), 0)
    for vertex in obj.data.vertices:
        weights = {names[g.group]: g.weight for g in vertex.groups if g.weight > 0.0}
        if not weights:
            counts["unweighted"] += 1
            continue
        if any(name not in rig.data.bones for name in weights):
            counts["stray"] += 1
        if abs(sum(weights.values()) - 1.0) > wirenook_props.WEIGHT_TOLERANCE:
            counts["unnormalised"] += 1
        if len(weights) > 1 and not any(set(weights) <= pair for pair in pairs):
            counts["shared"] += 1
    messages = {
        "unweighted": "vertices have no bone weight",
        "stray": "vertices are weighted to a group that is not a bone",
        "unnormalised": "vertices have weights that do not sum to 1",
        "shared": "vertices are split between bones that are not the two ends of one span",
    }
    return [f"{count} {messages[kind]}" for kind, count in counts.items() if count]


wirenook_props.assemble = _assemble
wirenook_props._validate_skin = _validate_skin


# --------------------------------------------------------------------------
# The model
# --------------------------------------------------------------------------


def build_shapes(mesh):
    """Three shapes to collect, a gate of two posts with a lattice between them, and a sign that names the next shape.

    Each shape has two bones: one Unity holds in place, and under it one
    that turns, carrying the geometry. The gate's lattice is hung between
    bones under the two posts, so the gate is as wide as the gap it is put
    across. Each post carries a lantern that says what the sign says, on
    all four sides, because a post has no front.
    """
    mesh.hung = {}
    heads = {}

    # The shapes: blue solids with bright marks, hovering at chest height.
    for number, name in enumerate(SHAPE_NAMES):
        centre = X * (number - 5.0) + UP * 1.1
        mesh.part(name)
        spin = mesh.part(name + "Spin")
        if name == "Tri":
            # A pyramid on a square base.
            base, tip = centre - UP * 0.2, centre + UP * 0.27
            faceted(spin, [*level_square(base, SHAPE_SIZE), tip],
                    [((0, 1, 2, 3), "blue", "glow-cyan"),
                     *(((side, (side + 1) % 4, 4), "signal-blue", "glow-cyan") for side in range(4))])
        elif name == "Square":
            # A cube.
            half = SHAPE_SIZE * 0.84
            corners = [*level_square(centre - UP * half, half), *level_square(centre + UP * half, half)]
            faceted(spin, corners,
                    [((0, 1, 2, 3), "blue", "glow-cyan"), ((4, 5, 6, 7), "blue", "glow-cyan"),
                     *(((side, (side + 1) % 4, (side + 1) % 4 + 4, side + 4), "signal-blue", "glow-cyan")
                       for side in range(4))], inset=0.55)
        else:
            # A ball. Its bright belt is tipped over, or nobody could tell that it turns.
            spin.ellipsoid(centre, (SHAPE_SIZE,) * 3,
                           ["blue", "signal-blue", "signal-blue", "glow-cyan", "signal-blue", "signal-blue", "blue"],
                           segments=12, rings=7, rotation=Matrix.Rotation(math.radians(28.0), 3, "Y"))
        heads[name] = centre
        heads[name + "Spin"] = centre

    # The gate's posts: dark, taller than anyone, a lantern on top and a crown of spikes over that.
    feet = [X * -1.6, X * (GATE_REST - 1.6)]
    for end, foot in zip(ENDS, feet):
        post = mesh.part("Post" + end)
        post.lathe(foot, UP, [(0.0, 0.16), (0.05, 0.16), (0.12, 0.07), (0.16, 0.07), (0.20, 0.05), (1.60, 0.05),
                              (1.64, 0.095), (1.86, 0.095), (1.92, 0.05)],
                   ["soot", "night-0", "glow-magenta", "night-0", "ink", "night-1", "soot", "night-1"],
                   segments=8, cap_start="soot")
        heads["Post" + end] = foot

        lamp = foot + UP * LAMP_HEIGHT
        post.loft([level_square(foot + UP * height, half) for height, half in
                   ((LAMP_HEIGHT - 0.17, 0.11), (LAMP_HEIGHT - 0.14, LAMP_HALF), (LAMP_HEIGHT + 0.14, LAMP_HALF),
                    (LAMP_HEIGHT + 0.17, 0.13))],
                  ["soot", "night-1", "soot"], cap_start="soot")
        roof = foot + UP * (LAMP_HEIGHT + 0.17)
        faceted(post, [*level_square(roof, 0.13), roof + UP * 0.15],
                [((side, (side + 1) % 4, 4), "ink", None) for side in range(4)])
        for name in SHAPE_NAMES:
            icon = mesh.part("Lamp" + name + end)
            for facing in (FORWARD, X, BACK, -X):
                icon.polygon(outline(name, lamp + facing * (LAMP_HALF + HAIR), 0.105, UP.cross(facing), count=12),
                             "glow-cyan", facing)
            heads["Lamp" + name + end] = lamp

        crown = mesh.part("Crown" + end)
        brim = foot + UP * CROWN_HEIGHT
        crown.lathe(brim, UP, [(0.0, 0.19), (0.03, 0.25), (0.06, 0.19), (0.0, 0.19)], ["night-2", "indigo", "soot"],
                    segments=6)
        for spike in range(6):
            angle = math.radians(60.0 * spike)
            out = X * math.cos(angle) + BACK * math.sin(angle)
            crown.lathe(brim + out * 0.22 + UP * 0.03, UP * math.cos(0.32) + out * math.sin(0.32),
                        [(0.0, 0.036), (0.15, 0.018), (0.25, 0.0)], ["soot", "glow-magenta"], segments=4)
        crown.lathe(brim + UP * 0.27, UP, [(0.0, 0.0), (0.07, 0.05), (0.14, 0.0)], "glow-red", segments=4)
        heads["Crown" + end] = brim

    # Between the posts: two jaws of fangs that pass each other, and braces behind them.
    curtain = Span(mesh, "Curtain", feet, BEAM_HEIGHT)
    teeth = Span(mesh, "Teeth", feet, RAIL_HEIGHT)
    for span, end in ((span, end) for span in (curtain, teeth) for end in (0, 1)):
        heads[SPANS[0 if span is curtain else 1] + ENDS[end]] = span.ends[end]
    girder(curtain, [(-0.07, -0.035, "glow-magenta"), (-0.035, 0.07, "soot")], 0.07,
           [(-0.07, "night-0"), (0.07, "night-0")])
    girder(teeth, [(-0.05, 0.02, "soot"), (0.02, 0.05, "glow-magenta")], 0.07, [(0.05, "night-0")])
    for number in range(1, FANGS + 1):
        share = number / (FANGS + 1.0)
        if number % 2:
            fang(curtain, share, -UP, 1.48, "glow-magenta")
        else:
            fang(teeth, share, UP, 1.44, "glow-red")
    thick = 0.03
    for brace in range(BRACES):
        for left, right in ((brace, brace + 1), (brace + 1, brace)):
            high, low = left / BRACES, right / BRACES
            sheet([(curtain, high, (0.0, 0.0, -0.07 + thick)), (curtain, high, (0.0, 0.0, -0.07 - thick)),
                   (teeth, low, (0.0, 0.0, 0.05 - thick)), (teeth, low, (0.0, 0.0, 0.05 + thick))],
                  "indigo", FORWARD)
    # Two thin lines of light across the middle, one riding with each jaw.
    for span, height in ((curtain, -0.55), (teeth, 0.58)):
        sheet([(span, 0.0, (0.0, 0.0, height - 0.015)), (span, 1.0, (0.0, 0.0, height - 0.015)),
               (span, 1.0, (0.0, 0.0, height + 0.015)), (span, 0.0, (0.0, 0.0, height + 0.015))],
              "glow-magenta", FORWARD)

    # The sign: a board on a stake, with one bone per thing it can say.
    foot = X * (GATE_REST - 0.2)
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
