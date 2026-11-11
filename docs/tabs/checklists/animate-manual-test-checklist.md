# Animate manual test checklist

See the [feature guide](../../modeling/assembly/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Click **New Animation** and confirm the timeline opens immediately. Create a hinge clip, scrub to both ends, and check the model follows the saved positions.
- [ ] Drag a key repeatedly, then try moving it onto a second key. Confirm the first stays selected and both keys survive.
- [ ] Zoom in, collapse and expand a track group, then select a hinge key. Use **Selected key** to enter 2.125 s and 65°. Save and reopen to verify both values.
- [ ] Switch between length and angle parameters in the right panel. Check that the displayed units change, create a ramp, and scrub it.
- [ ] Apply Height = 20 mm and Width = Height × 2 in the parameter table. Select Height and choose **Animate parameter**. Animate Height and verify Width follows on a model bound to those values.
- [ ] Animate an unrestricted shaft from 0° to 1800° over five seconds. Use Constant speed and verify five full turns. Try Hold and each easing option on the starting key.
- [ ] Select the starting key and choose **Custom curve**. Open Curve Editor, drag both handles, then enter exact Time and Progress coordinates. Click **Apply curve**, scrub and play the clip, and save/reopen to verify the custom timing. Confirm a rendered clip follows the same timing.
- [ ] Key only the crank of a four-bar linkage. Scrub between keys and render a short clip. Check that the fourth pin stays connected throughout both previews.
- [ ] Play an exploded-view animation and confirm the starting and ending placements match the expected assembly states.
- [ ] Export a short animation to a known location. Open the saved file and confirm its duration and playback.
