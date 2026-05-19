import { useRef, useMemo, useEffect, useState } from 'react';
import { useFrame, extend } from '@react-three/fiber';
import { Text, shaderMaterial, Float, Billboard } from '@react-three/drei';
import * as THREE from 'three';

// ── Holographic panel shader ─────────────────────────────────────────────────
const HoloPanelMaterial = shaderMaterial(
  {
    uTime: 0,
    uColor: new THREE.Color('#22d3ee'),
    uOpacity: 0.12,
    uScanSpeed: 1.0,
  },
  // vertex
  `
    varying vec2 vUv;
    varying vec3 vPos;
    void main() {
      vUv = uv;
      vPos = position;
      gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
    }
  `,
  // fragment
  `
    uniform float uTime;
    uniform vec3 uColor;
    uniform float uOpacity;
    uniform float uScanSpeed;
    varying vec2 vUv;
    varying vec3 vPos;

    void main() {
      // Base panel color with gradient
      float gradient = mix(0.6, 1.0, vUv.y);
      vec3 col = uColor * gradient;

      // Horizontal scanlines
      float scanline = sin(vUv.y * 120.0 + uTime * uScanSpeed) * 0.5 + 0.5;
      scanline = smoothstep(0.3, 0.7, scanline) * 0.08;

      // Vertical scan beam
      float beam = smoothstep(0.0, 0.02, abs(vUv.y - fract(uTime * 0.15)));
      beam = (1.0 - beam) * 0.25;

      // Edge glow
      float edgeX = smoothstep(0.0, 0.05, vUv.x) * smoothstep(0.0, 0.05, 1.0 - vUv.x);
      float edgeY = smoothstep(0.0, 0.05, vUv.y) * smoothstep(0.0, 0.05, 1.0 - vUv.y);
      float edge = 1.0 - edgeX * edgeY;
      edge = edge * 0.3;

      // Grid pattern
      float gridX = smoothstep(0.98, 1.0, sin(vUv.x * 60.0) * 0.5 + 0.5) * 0.04;
      float gridY = smoothstep(0.98, 1.0, sin(vUv.y * 40.0) * 0.5 + 0.5) * 0.04;

      float alpha = uOpacity + scanline + beam + edge + gridX + gridY;
      gl_FragColor = vec4(col, alpha);
    }
  `
);

extend({ HoloPanelMaterial });

// ── Types ────────────────────────────────────────────────────────────────────
export interface HologramResponsePanelProps {
  text: string;
  isStreaming?: boolean;
  position?: [number, number, number];
  width?: number;
  height?: number;
}

// ── Strip markdown to plain text with line structure ─────────────────────────
function stripMarkdown(md: string): { lines: TextLine[] } {
  const lines: TextLine[] = [];
  const raw = md.split('\n');

  for (const line of raw) {
    const trimmed = line.trim();
    if (!trimmed) {
      lines.push({ text: '', type: 'blank' });
      continue;
    }
    // Headers
    if (trimmed.startsWith('###')) {
      lines.push({ text: trimmed.replace(/^###\s*/, '').replace(/\*\*/g, ''), type: 'h3' });
    } else if (trimmed.startsWith('##')) {
      lines.push({ text: trimmed.replace(/^##\s*/, '').replace(/\*\*/g, ''), type: 'h2' });
    } else if (trimmed.startsWith('#')) {
      lines.push({ text: trimmed.replace(/^#\s*/, '').replace(/\*\*/g, ''), type: 'h1' });
    }
    // List items
    else if (trimmed.startsWith('- ') || trimmed.startsWith('* ')) {
      lines.push({ text: '▸ ' + trimmed.slice(2).replace(/\*\*/g, '').replace(/`/g, ''), type: 'list' });
    }
    // Bold line (whole line bold)
    else if (trimmed.startsWith('**') && trimmed.endsWith('**')) {
      lines.push({ text: trimmed.replace(/\*\*/g, ''), type: 'bold' });
    }
    // Normal text
    else {
      lines.push({ text: trimmed.replace(/\*\*/g, '').replace(/`/g, ''), type: 'body' });
    }
  }
  return { lines };
}

interface TextLine {
  text: string;
  type: 'h1' | 'h2' | 'h3' | 'body' | 'bold' | 'list' | 'blank';
}

const TYPE_CONFIG: Record<TextLine['type'], { size: number; color: string; spacing: number }> = {
  h1: { size: 0.09, color: '#22d3ee', spacing: 0.14 },
  h2: { size: 0.075, color: '#38bdf8', spacing: 0.12 },
  h3: { size: 0.065, color: '#67e8f9', spacing: 0.10 },
  bold: { size: 0.058, color: '#a5f3fc', spacing: 0.09 },
  body: { size: 0.052, color: '#cbd5e1', spacing: 0.08 },
  list: { size: 0.050, color: '#94a3b8', spacing: 0.075 },
  blank: { size: 0, color: '#000', spacing: 0.03 },
};

// ── Floating data particles ──────────────────────────────────────────────────
const HoloParticles: React.FC<{ count: number; bounds: [number, number]; active: boolean }> = ({
  count, bounds, active,
}) => {
  const ref = useRef<THREE.Points>(null);

  const [positions, velocities] = useMemo(() => {
    const pos = new Float32Array(count * 3);
    const vel = new Float32Array(count * 3);
    for (let i = 0; i < count; i++) {
      pos[i * 3] = (Math.random() - 0.5) * bounds[0] * 1.4;
      pos[i * 3 + 1] = (Math.random() - 0.5) * bounds[1] * 1.4;
      pos[i * 3 + 2] = (Math.random() - 0.5) * 0.3;
      vel[i * 3] = (Math.random() - 0.5) * 0.002;
      vel[i * 3 + 1] = (Math.random() - 0.5) * 0.002;
      vel[i * 3 + 2] = (Math.random() - 0.5) * 0.001;
    }
    return [pos, vel];
  }, [count, bounds]);

  useFrame(() => {
    if (!ref.current || !active) return;
    const pos = ref.current.geometry.attributes.position;
    for (let i = 0; i < count; i++) {
      const ix = i * 3, iy = i * 3 + 1, iz = i * 3 + 2;
      (pos.array as Float32Array)[ix] += velocities[ix];
      (pos.array as Float32Array)[iy] += velocities[iy];
      (pos.array as Float32Array)[iz] += velocities[iz];
      // Wrap around
      if (Math.abs((pos.array as Float32Array)[ix]) > bounds[0] * 0.7) velocities[ix] *= -1;
      if (Math.abs((pos.array as Float32Array)[iy]) > bounds[1] * 0.7) velocities[iy] *= -1;
      if (Math.abs((pos.array as Float32Array)[iz]) > 0.15) velocities[iz] *= -1;
    }
    pos.needsUpdate = true;
  });

  return (
    <points ref={ref}>
      <bufferGeometry>
        <bufferAttribute
          attach="attributes-position"
          args={[positions, 3]}
          count={count}
        />
      </bufferGeometry>
      <pointsMaterial
        size={0.008}
        color="#22d3ee"
        transparent
        opacity={0.4}
        sizeAttenuation
        depthWrite={false}
      />
    </points>
  );
};

// ── Corner bracket geometry ──────────────────────────────────────────────────
const CornerBracket: React.FC<{
  pos: [number, number, number];
  flipX?: boolean;
  flipY?: boolean;
  color: string;
}> = ({ pos, flipX, flipY, color }) => {
  const sx = flipX ? -1 : 1;
  const sy = flipY ? -1 : 1;
  const len = 0.12;
  const thick = 0.005;

  return (
    <group position={pos}>
      {/* Horizontal */}
      <mesh position={[sx * len / 2, 0, 0.01]}>
        <boxGeometry args={[len, thick, thick]} />
        <meshBasicMaterial color={color} />
      </mesh>
      {/* Vertical */}
      <mesh position={[0, sy * len / 2, 0.01]}>
        <boxGeometry args={[thick, len, thick]} />
        <meshBasicMaterial color={color} />
      </mesh>
      {/* Corner glow sphere */}
      <mesh position={[0, 0, 0.01]}>
        <sphereGeometry args={[0.015, 12, 12]} />
        <meshBasicMaterial color={color} transparent opacity={0.8} />
      </mesh>
      <pointLight position={[0, 0, 0.05]} color={color} intensity={0.15} distance={0.5} />
    </group>
  );
};

// ── Rotating accent ring ─────────────────────────────────────────────────────
const AccentRing: React.FC<{
  radius: number; color: string; speed: number; tilt: [number, number, number];
}> = ({ radius, color, speed, tilt }) => {
  const ref = useRef<THREE.Mesh>(null);
  useFrame((_, delta) => {
    if (ref.current) ref.current.rotation.z += delta * speed;
  });
  return (
    <mesh ref={ref} rotation={tilt}>
      <torusGeometry args={[radius, 0.004, 6, 80, Math.PI * 1.5]} />
      <meshBasicMaterial color={color} transparent opacity={0.25} />
    </mesh>
  );
};

// ── Header bar ───────────────────────────────────────────────────────────────
const HeaderBar: React.FC<{
  width: number; yPos: number; isStreaming: boolean;
}> = ({ width, yPos, isStreaming }) => {
  const dotRef = useRef<THREE.Mesh>(null);

  useFrame(() => {
    if (dotRef.current && isStreaming) {
      dotRef.current.scale.setScalar(0.8 + Math.sin(Date.now() * 0.008) * 0.3);
    }
  });

  return (
    <group position={[0, yPos, 0.015]}>
      {/* Status dot */}
      <mesh ref={dotRef} position={[-width / 2 + 0.06, 0, 0]}>
        <sphereGeometry args={[0.012, 12, 12]} />
        <meshBasicMaterial color={isStreaming ? '#22d3ee' : '#4ade80'} />
      </mesh>
      <pointLight
        position={[-width / 2 + 0.06, 0, 0.03]}
        color={isStreaming ? '#22d3ee' : '#4ade80'}
        intensity={0.2}
        distance={0.3}
      />
      {/* Label */}
      <Text
        position={[-width / 2 + 0.14, 0, 0]}
        fontSize={0.035}
        color="#22d3ee"
        anchorX="left"
        anchorY="middle"
        letterSpacing={0.15}
      >
        {isStreaming ? 'ORION ● STREAM' : 'ORION ● RESPONSE'}
      </Text>
      {/* Separator line */}
      <mesh position={[0, -0.03, 0]}>
        <boxGeometry args={[width * 0.9, 0.002, 0.002]} />
        <meshBasicMaterial color="#22d3ee" transparent opacity={0.3} />
      </mesh>
    </group>
  );
};

// ── Streaming cursor (blinking line) ─────────────────────────────────────────
const StreamCursor: React.FC<{ position: [number, number, number] }> = ({ position }) => {
  const ref = useRef<THREE.Mesh>(null);
  useFrame(() => {
    if (ref.current) {
      ref.current.visible = Math.sin(Date.now() * 0.008) > 0;
    }
  });
  return (
    <mesh ref={ref} position={position}>
      <boxGeometry args={[0.003, 0.04, 0.002]} />
      <meshBasicMaterial color="#22d3ee" />
    </mesh>
  );
};

// ── Main Component ───────────────────────────────────────────────────────────
export const HologramResponsePanel: React.FC<HologramResponsePanelProps> = ({
  text,
  isStreaming = false,
  position = [0, -0.5, 0],
  width = 2.4,
  height = 1.6,
}) => {
  const groupRef = useRef<THREE.Group>(null);
  const shaderRef = useRef<{ uTime: number; uScanSpeed: number }>(null);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    if (text) setVisible(true);
    else {
      const t = setTimeout(() => setVisible(false), 300);
      return () => clearTimeout(t);
    }
  }, [text]);

  // Parse text into 3D text lines
  const { lines } = useMemo(() => stripMarkdown(text), [text]);

  // Calculate auto height based on content
  const contentHeight = useMemo(() => {
    let h = 0.12; // header
    for (const line of lines) {
      h += TYPE_CONFIG[line.type].spacing;
    }
    return Math.max(h + 0.08, 0.5);
  }, [lines]);

  const panelHeight = Math.min(contentHeight, height);
  const hw = width / 2;
  const hh = panelHeight / 2;

  // Animate shader + spring-like entrance/exit scale
  const scaleRef = useRef(0);
  useFrame((_, delta) => {
    const target = visible ? 1 : 0;
    // Spring-like animation (damped)
    scaleRef.current += (target - scaleRef.current) * (1 - Math.exp(-6 * delta));
    if (groupRef.current) {
      const s = scaleRef.current;
      groupRef.current.scale.set(s, s, s);
    }
    if (shaderRef.current) {
      shaderRef.current.uTime += delta;
      shaderRef.current.uScanSpeed = isStreaming ? 3.0 : 0.5;
    }
  });

  // Build text Y positions (top-down layout)
  const textElements = useMemo(() => {
    const elems: { line: TextLine; y: number }[] = [];
    let y = hh - 0.10; // start below header
    for (const line of lines) {
      const cfg = TYPE_CONFIG[line.type];
      y -= cfg.spacing;
      if (y < -hh + 0.05) break; // clip to panel
      elems.push({ line, y });
    }
    return elems;
  }, [lines, hh]);

  // Last text position for cursor
  const lastTextY = textElements.length > 0
    ? textElements[textElements.length - 1].y
    : 0;

  return (
    <Float speed={1.5} rotationIntensity={0.02} floatIntensity={0.3}>
    <Billboard follow lockX={false} lockY={false} lockZ={false}>
    <group ref={groupRef} position={position} scale={[0, 0, 0]}>

      {/* ── Holographic background panel (custom shader) ── */}
      <mesh position={[0, 0, -0.005]}>
        <planeGeometry args={[width, panelHeight]} />
        {/* @ts-expect-error Custom shader material */}
        <holoPanelMaterial
          ref={shaderRef}
          transparent
          depthWrite={false}
          side={THREE.DoubleSide}
        />
      </mesh>

      {/* ── Wireframe edges ── */}
      <lineSegments>
        <edgesGeometry args={[new THREE.BoxGeometry(width, panelHeight, 0.01)]} />
        <lineBasicMaterial color="#22d3ee" transparent opacity={0.3} />
      </lineSegments>

      {/* ── Corner brackets ── */}
      <CornerBracket pos={[-hw, hh, 0]} color="#22d3ee" />
      <CornerBracket pos={[hw, hh, 0]} flipX color="#22d3ee" />
      <CornerBracket pos={[-hw, -hh, 0]} flipY color="#8b5cf6" />
      <CornerBracket pos={[hw, -hh, 0]} flipX flipY color="#8b5cf6" />

      {/* ── Header ── */}
      <HeaderBar width={width} yPos={hh - 0.04} isStreaming={isStreaming} />

      {/* ── Accent rings ── */}
      <AccentRing radius={width * 0.58} color="#8b5cf6" speed={0.3} tilt={[0.3, 0, 0]} />
      <AccentRing radius={panelHeight * 0.6} color="#22d3ee" speed={-0.2} tilt={[0, 0.4, 0]} />

      {/* ── 3D Text lines (SDF via drei Text — real 3D geometry) ── */}
      <group position={[0, 0, 0.02]}>
        {textElements.map(({ line, y }, i) => {
          if (line.type === 'blank') return null;
          const cfg = TYPE_CONFIG[line.type];
          const xOffset = line.type === 'list' ? -hw + 0.15 : -hw + 0.1;
          return (
            <Text
              key={`${i}-${line.text.slice(0, 20)}`}
              position={[xOffset, y, 0]}
              fontSize={cfg.size}
              color={cfg.color}
              anchorX="left"
              anchorY="middle"
              maxWidth={width - 0.25}
              lineHeight={1.2}
              letterSpacing={line.type === 'h1' || line.type === 'h2' ? 0.05 : 0.01}
            >
              {line.text}
            </Text>
          );
        })}
      </group>

      {/* ── Streaming cursor ── */}
      {isStreaming && (
        <StreamCursor position={[hw - 0.1, lastTextY, 0.02]} />
      )}

      {/* ── Floating particles ── */}
      <HoloParticles count={40} bounds={[width, panelHeight]} active={visible} />

      {/* ── Ambient glow ── */}
      <pointLight position={[0, 0, 0.5]} color="#22d3ee" intensity={0.15} distance={3} />
    </group>
    </Billboard>
    </Float>
  );
};

export default HologramResponsePanel;
