// components/canvas/OrionCore3D.tsx
import { useRef, useMemo, useCallback, MutableRefObject } from 'react';
import { useFrame, extend, ThreeEvent } from '@react-three/fiber';
import { shaderMaterial, Float } from '@react-three/drei';
import * as THREE from 'three';
import { useEntity } from '../../context/EntityContext';

// ── Clean energy orb shader — no planet noise ───────────────────────────────
const OrbMaterial = shaderMaterial(
  { uTime: 0, uColor: new THREE.Color('#22d3ee'), uAmplitude: 0 },
  // vertex
  `varying vec3 vNormal;
   varying vec3 vViewDir;
   void main() {
     vNormal = normalize(normalMatrix * normal);
     vec4 mvPos = modelViewMatrix * vec4(position, 1.0);
     vViewDir = normalize(-mvPos.xyz);
     gl_Position = projectionMatrix * mvPos;
   }`,
  // fragment — pure fresnel energy orb, no surface texture
  `uniform float uTime;
   uniform vec3 uColor;
   uniform float uAmplitude;
   varying vec3 vNormal;
   varying vec3 vViewDir;
   void main() {
     float fresnel = pow(1.0 - abs(dot(vNormal, vViewDir)), 2.0);
     float pulse = sin(uTime * 4.0) * 0.5 + 0.5;
     float inner = smoothstep(0.0, 0.6, 1.0 - fresnel);
     float glow = fresnel * (1.5 + uAmplitude * 2.0);
     glow += pulse * 0.12;
     vec3 col = uColor * glow + vec3(0.4, 0.2, 1.0) * uAmplitude * fresnel;
     float alpha = clamp(glow * 0.9 + inner * 0.15, 0.0, 1.0);
     gl_FragColor = vec4(col, alpha);
   }`
);

extend({ OrbMaterial });

// ── State configs ────────────────────────────────────────────────────────────
const STATE_CONFIGS = {
  idle:       { color: '#22d3ee', scale: 1.0  },
  listening:  { color: '#a78bfa', scale: 1.1  },
  thinking:   { color: '#fbbf24', scale: 1.05 },
  responding: { color: '#34d399', scale: 1.15 },
  error:      { color: '#ef4444', scale: 1.0  },
} as const;

interface OrionCore3DProps {
  onTap?: () => void;
  onLongPress?: () => void;
  onDoubleTap?: () => void;
}


// ── Particle cloud ──────────────────────────────────────────────────────────
const ParticleCloud: React.FC<{ amplitudeRef: MutableRefObject<number> }> = ({ amplitudeRef }) => {
  const count = 200;
  const positions = useMemo(() => {
    const pos = new Float32Array(count * 3);
    for (let i = 0; i < count; i++) {
      const r = 1.1 + Math.random() * 1.6;
      const theta = Math.random() * Math.PI * 2;
      const phi = Math.acos(2 * Math.random() - 1);
      pos[i * 3]     = r * Math.sin(phi) * Math.cos(theta);
      pos[i * 3 + 1] = r * Math.sin(phi) * Math.sin(theta);
      pos[i * 3 + 2] = r * Math.cos(phi);
    }
    return pos;
  }, []);

  const ref = useRef<THREE.Points>(null);
  useFrame((_, delta) => {
    if (ref.current) {
      const s = 1 + amplitudeRef.current * 0.5;
      ref.current.scale.lerp(new THREE.Vector3(s, s, s), 0.08);
      ref.current.rotation.y += delta * 0.05;
    }
  });

  return (
    <points ref={ref}>
      <bufferGeometry>
        <bufferAttribute attach="attributes-position" args={[positions, 3]} count={count} />
      </bufferGeometry>
      <pointsMaterial size={0.015} color="#a78bfa" transparent opacity={0.35} sizeAttenuation depthWrite={false} />
    </points>
  );
};

// ── Main component ──────────────────────────────────────────────────────────
export const OrionCore3D: React.FC<OrionCore3DProps> = ({ onTap, onLongPress, onDoubleTap }) => {
  const { state, amplitudeRef } = useEntity();
  const meshRef = useRef<THREE.Mesh>(null);
  const matRef = useRef<{ uTime: number; uAmplitude: number; uColor: THREE.Color }>(null);

  const longPressTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const isLongPressRef = useRef(false);
  const lastTapRef = useRef(0);

  const handlePointerDown = useCallback((e: ThreeEvent<PointerEvent>) => {
    e.stopPropagation();
    isLongPressRef.current = false;
    longPressTimer.current = setTimeout(() => {
      isLongPressRef.current = true;
      onLongPress?.();
    }, 500);
  }, [onLongPress]);

  const handlePointerUp = useCallback((e: ThreeEvent<PointerEvent>) => {
    e.stopPropagation();
    if (longPressTimer.current) {
      clearTimeout(longPressTimer.current);
      longPressTimer.current = null;
    }
    if (!isLongPressRef.current) {
      const now = Date.now();
      if (now - lastTapRef.current < 300 && lastTapRef.current > 0) {
        lastTapRef.current = 0;
        onDoubleTap?.();
      } else {
        lastTapRef.current = now;
        onTap?.();
      }
    }
  }, [onTap, onDoubleTap]);

  useFrame((_, delta) => {
    const amp = amplitudeRef.current;
    const cfg = STATE_CONFIGS[state] ?? STATE_CONFIGS.idle;


    if (matRef.current) {
      matRef.current.uTime += delta;
      matRef.current.uAmplitude += (amp - matRef.current.uAmplitude) * 0.12;
      matRef.current.uColor.lerp(new THREE.Color(cfg.color), 0.06);
    }
    if (meshRef.current) {
      const s = cfg.scale + amp * 0.12;
      meshRef.current.scale.lerp(new THREE.Vector3(s, s, s), 0.08);
    }
  });

  return (
    <Float speed={1.5} rotationIntensity={0.2} floatIntensity={0.4}>
      {/* Core orb */}
      <mesh ref={meshRef} onPointerDown={handlePointerDown} onPointerUp={handlePointerUp}>
        <sphereGeometry args={[0.72, 48, 48]} />
        {/* @ts-expect-error Custom shader material */}
        <orbMaterial ref={matRef} transparent depthWrite={false} />
      </mesh>

      {/* Inner glow sphere (additive blend) */}
      <mesh>
        <sphereGeometry args={[0.68, 24, 24]} />
        <meshBasicMaterial color="#0d0820" transparent opacity={0.55} depthWrite={false} />
      </mesh>

      {/* Particle cloud */}
      <ParticleCloud amplitudeRef={amplitudeRef} />
    </Float>
  );
};
