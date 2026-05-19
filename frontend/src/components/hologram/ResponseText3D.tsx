// components/hologram/ResponseText3D.tsx
// 3D text response panel — stable instance, no key changes during streaming
import { useRef, useMemo } from 'react';
import { useFrame } from '@react-three/fiber';
import { Text, Float } from '@react-three/drei';
import * as THREE from 'three';

interface ResponseText3DProps {
  text: string;
  isStreaming: boolean;
}

// Strip markdown formatting for clean 3D display
function stripMd(raw: string): string {
  return raw
    .replace(/\*\*(.*?)\*\*/g, '$1')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/^#{1,3}\s+/gm, '')
    .replace(/^[-*]\s+/gm, '• ')
    .trim();
}

// Cursor that blinks during streaming
const Cursor: React.FC<{ position: [number, number, number] }> = ({ position }) => {
  const ref = useRef<THREE.Mesh>(null);
  useFrame(() => {
    if (ref.current) ref.current.visible = Math.sin(Date.now() * 0.007) > 0;
  });
  return (
    <mesh ref={ref} position={position}>
      <boxGeometry args={[0.012, 0.055, 0.002]} />
      <meshBasicMaterial color="#22d3ee" />
    </mesh>
  );
};

// Backdrop glass panel — geometry stable, size driven by ref
const Backdrop: React.FC<{ width: number; height: number }> = ({ width, height }) => {
  // Stable geometry — only depends on fixed max size, no per-frame recreation
  const geo = useMemo(() => new THREE.PlaneGeometry(width, height), [width, height]);

  return (
    <mesh geometry={geo} position={[0, 0, -0.02]}>
      <meshBasicMaterial
        color="#0a0820"
        transparent
        opacity={0.45}
        depthWrite={false}
      />
    </mesh>
  );
};

export const ResponseText3D: React.FC<ResponseText3DProps> = ({ text, isStreaming }) => {
  const groupRef = useRef<THREE.Group>(null);
  const scaleRef = useRef(0);

  const visible = text.length > 0;
  const displayText = stripMd(text);

  // Spring entrance / exit — no React state, no re-renders
  useFrame((_, delta) => {
    const target = visible ? 1 : 0;
    scaleRef.current += (target - scaleRef.current) * (1 - Math.exp(-7 * delta));
    if (groupRef.current) {
      const s = scaleRef.current;
      groupRef.current.scale.set(s, s, s);
    }
  });

  if (!visible) return null;

  return (
    <Float speed={1.2} rotationIntensity={0.03} floatIntensity={0.25}>
      <group ref={groupRef} position={[0, -2.0, 1.2]} scale={[0, 0, 0]}>

        {/* Glass backdrop */}
        <Backdrop width={3.2} height={1.8} />

        {/* Top accent line */}
        <mesh position={[0, 0.85, -0.01]}>
          <planeGeometry args={[2.6, 0.003]} />
          <meshBasicMaterial color="#22d3ee" transparent opacity={0.6} />
        </mesh>

        {/* Main response text — single stable instance, content updates in place */}
        <Text
          position={[0, 0.1, 0]}
          fontSize={0.075}
          color="#d1d5db"
          anchorX="center"
          anchorY="middle"
          maxWidth={2.8}
          lineHeight={1.55}
          letterSpacing={0.01}
          textAlign="left"
          overflowWrap="break-word"
        >
          {displayText}
        </Text>

        {/* Streaming cursor */}
        {isStreaming && <Cursor position={[1.35, -0.6, 0]} />}

        {/* Bottom accent line */}
        <mesh position={[0, -0.82, -0.01]}>
          <planeGeometry args={[2.6, 0.002]} />
          <meshBasicMaterial color="#8b5cf6" transparent opacity={0.35} />
        </mesh>
      </group>
    </Float>
  );
};
