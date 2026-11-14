# Motion manual test checklist

See the [feature guide](../../cae/motion/README.md). Record the Prism build and any result that differs from the expected behavior.

- [ ] Ground one component and give a second component one movable joint. Confirm the mechanism moves only in the intended direction.
- [ ] Choose Movement only, configure a drive, then select another joint and return. Confirm every value is retained.
- [ ] Run a study, type an exact result time in seconds, and check the displayed sample time. Play and pause the result.
- [ ] Set a drive to Move to Position with Custom curve. Open Curve Editor, change the handles, apply and rerun. Check the resulting speed changes.
- [ ] Stop a running study. Change the setup during another run and confirm stale results are discarded.
- [ ] Add a spring or force with a known direction and run a short Motion Study. Check the resulting direction of movement and scrub the playback.
- [ ] Save and reopen the assembly. Verify its joints and load objects retain their assignments, then recreate the session's Motion Study inputs.
