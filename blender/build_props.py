"""Builds the party-tricks props: the bonk hammer, the stand-in log, the smoke puff and the busy sign.

    blender -b --python blender/build_props.py [-- --only STEM] [-- --previews DIR]

Each prop is modelled from the code below, saved as `blender/<stem>.blend`,
checked against the wirenook prop contract and exported to the package's
`Models/<stem>.fbx`. See README.md beside this file.
"""

import math
import pathlib
import sys

sys.dont_write_bytecode = True

REPO = pathlib.Path(__file__).resolve().parents[1]
MODELS = REPO / "Packages" / "net.wirenook.party-tricks" / "Models"
LIBRARY = REPO.parent / "wirenook-assets" / "tools" / "blender"
if not (LIBRARY / "wirenook_props.py").is_file():
    raise SystemExit(f"The shared prop kit is missing: {LIBRARY / 'wirenook_props.py'}\n"
                     "Clone wirenook-assets beside this repository and run again.")
sys.path.insert(0, str(LIBRARY))

from mathutils import Vector

from wirenook_props import (FORWARD, GLOW, SOLID, UP, PropSpec, Puff, chamfered_rect, cloud, cloud_tones, main_build,
                            rabbit_badge, ring_points)

X = Vector((1.0, 0.0, 0.0))
BACK = -FORWARD

# How far a flat detail floats off the surface it is drawn on.
HAIR = 0.0008


# --------------------------------------------------------------------------
# Hammer
# --------------------------------------------------------------------------

HAMMER = PropSpec("Hammer", "hammer", triangle_budget=520, parts={"Impact": None}, materials=(SOLID, GLOW))


def build_hammer(mesh):
    """A toy mallet, held like a hammer: the handle runs up out of the fist and the head strikes forward."""
    head_height = 0.36
    face = 0.13
    body = mesh.part()
    body.lathe(UP * 0.0, UP, [
        (-0.085, 0.012), (-0.075, 0.020), (-0.062, 0.020), (-0.055, 0.0155),
        (0.055, 0.0155), (0.062, 0.019), (0.070, 0.013), (head_height - 0.07, 0.013),
    ], ["orange", "orange", "orange", "ink", "orange", "orange", "ice"], segments=8, cap_start="orange")

    centre = UP * head_height
    body.lathe(centre, FORWARD, [
        (-face, 0.055), (-face + 0.005, 0.075), (-0.085, 0.085), (-0.075, 0.078),
        (0.075, 0.078), (0.085, 0.085), (face - 0.005, 0.075), (face, 0.055),
    ], ["lavender", "lavender", "magenta", "pink", "magenta", "lavender", "lavender"],
        segments=12, cap_start="ice", cap_end="ice")
    for side in (X, -X):
        rabbit_badge(body, centre + side * (0.078 + HAIR), side, 0.026, "violet")

    # What a hit looks like: two stars crossed at the striking face, so the
    # burst shows from the front and from the side.
    impact = mesh.part("Impact", GLOW)
    burst = centre + FORWARD * (face + 0.02)
    for across, facing in ((X, FORWARD), (FORWARD, X)):
        star = [burst + (across * math.cos(a) + UP * math.sin(a)) * r
                for a, r in ((math.radians(22.5 * k), 0.17 if k % 2 == 0 else 0.07) for k in range(16))]
        for way in (facing, -facing):
            impact.fan(burst, star, "glow-warm" if facing == FORWARD else "glow-white", way)
    return {"Impact": burst}


# --------------------------------------------------------------------------
# Log
# --------------------------------------------------------------------------

LOG = PropSpec("Log", "log", triangle_budget=260)


def build_log(mesh):
    """What is left standing where someone was: a log on end, as in every ninja story."""
    height = 0.95
    log = mesh.part()
    log.lathe(UP * 0.0, UP, [(0.0, 0.165), (0.04, 0.178), (height - 0.05, 0.168), (height, 0.155)],
              "wood-dark", segments=10, sharp=(0, 3), cap_start="wood-dark", cap_end="cream")
    for radius, swatch, lift in ((0.105, "wood", 1.0), (0.05, "cream", 2.0)):
        log.polygon(ring_points(UP * (height + HAIR * lift), X, FORWARD, radius, radius, 10), swatch, UP)

    # The stub of a branch, with the one leaf it kept.
    stub = UP * 0.62
    log.lathe(stub, X, [(0.13, 0.036), (0.25, 0.030)], "wood-dark", segments=6, cap_end="cream")
    leaf = stub + X * 0.245 + UP * 0.03
    log.polygon([leaf, leaf + X * 0.07 + UP * 0.05, leaf + X * 0.15 + UP * 0.035, leaf + X * 0.08 - UP * 0.01],
                "leaf", FORWARD)
    log.polygon([leaf, leaf + X * 0.08 - UP * 0.01, leaf + X * 0.15 + UP * 0.035, leaf + X * 0.07 + UP * 0.05],
                "leaf", BACK)

    rabbit_badge(log, UP * 0.42 + FORWARD * (0.176 + HAIR), FORWARD, 0.05, "cream")
    return {}


# --------------------------------------------------------------------------
# Puff
# --------------------------------------------------------------------------

PUFF = PropSpec("Puff", "puff", triangle_budget=520)


def build_puff(mesh):
    """A cloud of smoke big enough to hide a swap behind, standing on the ground."""
    puff = mesh.part()
    cloud(puff, [
        Puff((0.0, 0.0, 0.55), (0.42, 0.40, 0.52), segments=10, rings=6),
        Puff((-0.32, 0.06, 0.34), (0.30, 0.28, 0.32)),
        Puff((0.31, -0.05, 0.37), (0.31, 0.28, 0.34)),
        Puff((0.12, 0.10, 0.98), (0.33, 0.30, 0.34)),
        Puff((-0.17, -0.09, 0.88), (0.28, 0.27, 0.30)),
        Puff((0.0, -0.26, 0.30), (0.24, 0.20, 0.26), segments=6, rings=4),
    ], floor=0.0, tones=cloud_tones(0.18, 0.80))
    return {}


# --------------------------------------------------------------------------
# Busy sign
# --------------------------------------------------------------------------

BUSY = PropSpec("Busy", "busy", triangle_budget=160)


def build_busy(mesh):
    """A little window with three dots in it: the wearer is looking at something you cannot see."""
    half_width, half_height, depth = 0.085, 0.06, 0.012
    sign = mesh.part()
    sign.loft([chamfered_rect(FORWARD * side, X, UP, half_width, half_height, 0.012) for side in (depth / 2, -depth / 2)],
              "ink", cap_start="panel", cap_end="panel")
    for facing in (FORWARD, BACK):
        surface = facing * (depth / 2 + HAIR)
        bar = [surface + X * x + UP * z for x, z in ((-0.075, 0.030), (0.075, 0.030), (0.075, 0.050), (-0.075, 0.050))]
        sign.polygon(bar if facing == FORWARD else bar[::-1], "lavender", facing)
        for x in (-0.038, 0.0, 0.038):
            sign.polygon(ring_points(surface + X * x - UP * 0.012, X, UP, 0.011, 0.011, 6), "magenta", facing)
    return {}


PROPS = [
    (HAMMER, build_hammer),
    (LOG, build_log),
    (PUFF, build_puff),
    (BUSY, build_busy),
]

if __name__ == "__main__":
    main_build(PROPS, REPO / "blender", MODELS)
