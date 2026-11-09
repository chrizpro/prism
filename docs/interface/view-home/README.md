# Home orientation (View tab and view cube)

> **Documentation status:** Approved by the owner on 2026-11-07 (requested in session, simplified to one toggling button, then told to proceed with tests).
>
> This page describes product behavior. It is not, by itself, evidence of approval, implementation completeness, or passing tests.

## Purpose and scope

An imported mesh, or a part modeled in an awkward way, can arrive with its "top" pointing somewhere unhelpful. Set Home lets the user say "this is how the part should be held" without moving the geometry.

In scope: the View tab button, the matching button beside the view cube, how the cube and the standard views follow the orientation, and saving it in the document. Out of scope: Two/Four Views layouts, and a list of earlier homes.

## Workflow

1. Orbit until the part sits the way you want and choose **Set Home** (View tab, Navigate group, or the house-with-plus button under the cube's Home button).
2. The current view becomes the home view; the cube and Front/Back/Left/Right/Top/Bottom/Isometric describe the part's own axes. The cube's Home button returns to it.
3. The same button now reads **Reset Home** (house with a cross on the cube). Choosing it puts the original orientation back.

## Requirements review

| ID (optional) | Proposed behavior | Review state |
| --- | --- | --- |
| HOME-1 | Until the user sets one, Home is the isometric view and the cube is unchanged; the button reads Set Home. | Approved 2026-11-07 |
| HOME-2 | Set Home makes the main view's current direction the home view and turns the cube and standard views to match. It moves no geometry and is not a timeline step. | Approved 2026-11-07 |
| HOME-3 | After the orientation has changed the same button (ribbon and cube) reads Reset Home and restores the original orientation. | Approved 2026-11-07 |
| HOME-4 | The orientation is saved in the document and restored on open; a new document starts original. | Approved 2026-11-07 |

### Decisions needed

- Should Set Home also offer "use this view as Front or Top" for a face-on view?
- Should the Two/Four Views layouts follow the home orientation?

## Verification and traceability

| Requirement / behavior | Existing test or validation | Last observed result |
| --- | --- | --- |
| HOME-1 | [HOME-H001](../../../Prism.Tests/Headless/Viewport/HomeOrientationTests.cs) | Passed, headless, 2026-11-07 |
| HOME-2 | [HOME-H002](../../../Prism.Tests/Headless/Viewport/HomeOrientationTests.cs) | Passed, headless, 2026-11-07 |
| HOME-3 | [HOME-H003](../../../Prism.Tests/Headless/Viewport/HomeOrientationTests.cs) | Passed, headless, 2026-11-07 |
| HOME-4 | [HOME-H004](../../../Prism.Tests/Headless/Viewport/HomeOrientationTests.cs) | Passed, headless, 2026-11-07 |

The view cube button (draw, hover text, click) has no automated test and has not been checked in the desktop app. Bug fix 2026-11-09 (owner report): clicking the projection button beside the cube did nothing, and neither did the two roll buttons, because the click handler had lost their cases in an earlier rework. They are restored: the projection button toggles orthographic and perspective, and the roll buttons turn a face view 90 degrees. Not covered by an automated test.

**Loft opens in the home view (owner direction 2026-11-09).** Extrude and Revolve already looked right because the user arrived in perspective; Loft did not, and stayed orthographic after a cube click, which makes sections and rails hard to pick. Starting Loft or Loft Surface from an orthographic view, or from a straight-on standard view, now switches to perspective and moves to the home view (isometric until the user sets another). A perspective view at any other angle is left alone, and replaying Loft from history never moves the camera. The owner is "pretty sure" home is the right view; other tools can opt in the same way (`WantsHomeView`). Test: [HOME-H005](../../../Prism.Tests/Headless/Viewport/HomeOrientationTests.cs), unrun.

## Related resources

- Implementation: `Prism/Core/CadSession.Home.cs`, `Prism/Controls/ViewCube.cs`, View tab in `Prism/ViewModels/MainViewModel.cs`.
