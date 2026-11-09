"""cadkit: drive the CAD application from Python.

    import cadkit as cad

    cad.box(width=40, depth=30, height=20)
    with cad.sketch("XY") as s:
        s.rect((-10, -10), (10, 10))
    cad.extrude("Sketch 1", distance=15)

Run it from the application's script editor, or from VS Code / PyCharm / a terminal while the application is open.
Everything is in this one package and needs nothing but the Python standard library.
"""
from ._conn import CadError, connect, call, tools
from .api import (
    Obj, holes, show, faces, edges, check, commands, describe, help, sketch, history, dialog, ask, run, objects, get, state, undo, redo, screenshot, view, save, delete, select,
    box, cylinder, sphere, cone, torus,
    flatten, refold, wrap, extrude, revolve, sweep, loft, fillet, chamfer, shell, union, subtract, intersect,
    move, rotate, scale, mirror,
    flange, fold, edge_flange, flat_pattern, drawing, image, edit_image,
    linear_pattern, circular_pattern, rib, split_body, push_pull, move_face, delete_face, thicken,
    cam_setup, cnc_profile, cnc_pocket, cnc_adaptive, cnc_drill, cnc_face, cnc_finish, cnc_post, print_export,
    fdm_check, resin_check, cnc_design_check, cnc_stock_review, resin_export, cnc_rough,
    form_box, form_cylinder, form_loft, form_pipe, form_info, form_op, form_to_nurbs,
)

__all__ = [n for n in dir() if not n.startswith("_")]
__version__ = "0.1"
