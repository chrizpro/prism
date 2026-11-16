# Solid patterns

Linear repeats in up to three directions. Circular uses an axis with optional rings. Along Curve places instances by count or spacing along a path. Fill distributes instances inside a planar region with a margin. Table reads offsets or absolute positions from rows and supports a Z rotation.

## Review and verification

This guide describes observed behavior. Additional requirements are proposed and need owner review. [Tracking issue #30](https://github.com/chrizpro/prism/issues/30).

- [Maintained headless coverage](../../../Prism.Tests/Headless/Solid/PatternTests.cs)
- [Pattern math unit tests](../../../Prism.Tests/Unit/Solid/)

The headless cases check instance count, analytic centers and conserved copy volume for all five pattern tools. Unit checks exercise path sampling, reverse alignment and fill holes.

## Coverage gaps

Complex paths, alternate fill layouts, ring and direction controls, skip lists, external data and Join/Cut results still need coverage.

## Ridge wheel gallery study

Owner-approved on 2026-11-15 after tire and wheel concept review ([#131](https://github.com/chrizpro/prism/issues/131)). The [native reproduction script](../../../gallery/build_ridge_wheel.py) uses five Circular Pattern features with 32 sectors each for chamfered chevrons, staggered intermediate blocks and shoulder lugs; narrow sipes are cut into the seed before copying. Three six-instance Circular Patterns create split spokes, lug cutting tools and recessed nuts. The spoke seed has two branches and recessed pockets. This is a static visual multi-solid assembly with overlapping component joints. [GAL-I015](../../../Prism.Tests/Integration/DesktopCncPatternGalleryTests.cs) checks saved native validity, volumes/counts, ground contact, lug bores and retained pattern recipes. Windows verification on 2026-11-15 passed GAL-I015 on all 185 solids and all eight pattern recipes. The 17 maintained Headless pattern cases also passed. The coverage gaps above remain for other pattern controls and joining/cutting modes.
