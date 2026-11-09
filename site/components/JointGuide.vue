<script setup>
const joints = [
  { id: 'rigid', name: 'Rigid', motion: 'No relative motion.', example: 'A bracket fastened to a base.', arrows: [] },
  { id: 'revolute', name: 'Revolute', motion: 'Turn about one axis; no sliding.', example: 'A door hinge or shaft bearing.', arrows: ['M 220 78 A 47 47 0 1 1 220 172'] },
  { id: 'slider', name: 'Slider', motion: 'Slide along one axis; no turning.', example: 'A carriage on a straight rail.', arrows: ['M 165 63 H 290'] },
  { id: 'cylindrical', name: 'Cylindrical', motion: 'Turn and slide along the same axis.', example: 'A shaft that can both spin and slide.', arrows: ['M 165 52 H 290', 'M 220 78 A 47 47 0 1 1 220 172'] },
  { id: 'ball', name: 'Ball', motion: 'Turn about a shared point; no sliding.', example: 'A ball-and-socket connection.', arrows: ['M 155 86 A 53 38 0 1 1 155 164', 'M 203 70 A 27 58 0 1 1 203 180'] },
  { id: 'planar', name: 'Planar', motion: 'Slide in a plane and turn about its normal.', example: 'A part resting flat on a table.', arrows: ['M 120 55 H 280', 'M 300 85 V 182', 'M 130 173 A 47 47 0 0 0 220 173'] },
]
</script>

<template>
  <div class="joint-guide">
    <p class="joint-legend">Schematic examples: <span class="fixed-key">gray = reference part</span>, <span class="moving-key">blue = connected part</span>. Orange arrows show allowed relative motion before limits.</p>
    <div class="joint-cards">
      <figure v-for="joint in joints" :key="joint.id">
        <h3>{{ joint.name }}</h3>
        <svg viewBox="0 0 360 230" role="img" :aria-labelledby="`joint-${joint.id}-title joint-${joint.id}-desc`">
          <title :id="`joint-${joint.id}-title`">{{ joint.name }} joint</title>
          <desc :id="`joint-${joint.id}-desc`">{{ joint.motion }} {{ joint.example }}</desc>
          <defs>
            <marker :id="`arrow-${joint.id}`" viewBox="0 0 10 10" refX="5" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M 1 1 L 9 5 L 1 9 Z" fill="#b84f0a" /></marker>
          </defs>
          <g v-if="joint.id === 'planar'">
            <path d="M 65 80 H 275 V 195 H 65 Z" fill="#d8dfe8" stroke="#526075" stroke-width="2" />
            <rect x="130" y="103" width="90" height="55" rx="6" fill="#3979cb" stroke="#173f73" stroke-width="2" />
            <text x="75" y="216">Plane viewed from above</text>
          </g>
          <g v-else-if="joint.id === 'ball'">
            <path d="M 65 175 V 125 H 145" fill="none" stroke="#526075" stroke-width="24" />
            <path d="M 180 125 L 257 91" stroke="#3979cb" stroke-width="24" stroke-linecap="round" />
            <circle cx="180" cy="125" r="23" fill="#3979cb" stroke="#173f73" stroke-width="2" />
            <circle cx="180" cy="125" r="5" fill="#fff" />
            <text x="100" y="211">Shared center stays together</text>
          </g>
          <g v-else>
            <rect x="65" y="98" width="72" height="85" rx="5" fill="#d8dfe8" stroke="#526075" stroke-width="2" />
            <path d="M 137 125 H 290" stroke="#3979cb" stroke-width="24" />
            <path d="M 40 125 H 315" stroke="#173f73" stroke-width="2" stroke-dasharray="5 6" />
            <text x="68" y="211">Reference</text><text x="234" y="211">Joint axis</text>
            <g v-if="joint.id === 'rigid'" stroke="#173f73" fill="none" stroke-width="4">
              <rect x="202" y="58" width="30" height="25" rx="3" fill="#fff" />
              <path d="M 208 58 V 48 A 9 9 0 0 1 226 48 V 58" />
            </g>
          </g>
          <path v-for="arrow in joint.arrows" :key="arrow" :d="arrow" fill="none" stroke="#b84f0a" stroke-width="3" :marker-start="`url(#arrow-${joint.id})`" :marker-end="`url(#arrow-${joint.id})`" />
        </svg>
        <figcaption><strong>{{ joint.motion }}</strong><br>{{ joint.example }}</figcaption>
      </figure>
    </div>
  </div>
</template>

<style scoped>
.joint-legend { font-size: 14px; line-height: 1.7; }
.fixed-key, .moving-key { white-space: nowrap; }
.joint-cards { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; margin: 20px 0 28px; }
figure { margin: 0; padding: 18px; border: 1px solid var(--vp-c-divider); border-radius: 10px; background: var(--vp-c-bg-soft); }
.joint-cards h3 { margin: 0 0 12px; font-size: 18px; }
svg { display: block; width: 100%; height: auto; background: #f4f7fb; border-radius: 6px; }
svg text { fill: #283b53; font: 13px 'Segoe UI', sans-serif; }
figcaption { margin-top: 12px; font-size: 14px; line-height: 1.6; color: var(--vp-c-text-2); }
figcaption strong { color: var(--vp-c-text-1); }
@media (max-width: 600px) { .joint-cards { grid-template-columns: 1fr; } }
</style>
