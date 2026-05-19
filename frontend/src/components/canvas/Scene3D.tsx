// components/canvas/Scene3D.tsx
import { Canvas } from '@react-three/fiber';
import { Stars, PerspectiveCamera } from '@react-three/drei';
import { EffectComposer, Bloom, Vignette } from '@react-three/postprocessing';
import { OrionCore3D } from './OrionCore3D';
import { ResponseText3D } from '../hologram/ResponseText3D';

export interface Scene3DProps {
  responseText?: string;
  isStreaming?: boolean;
  onTap?: () => void;
  onLongPress?: () => void;
  onDoubleTap?: () => void;
}

export const Scene3D: React.FC<Scene3DProps> = ({
  responseText = '',
  isStreaming = false,
  onTap,
  onLongPress,
  onDoubleTap,
}) => {
  return (
    <div className="absolute inset-0 z-0">
      <Canvas
        dpr={[1, 1.5]}
        gl={{ antialias: true, alpha: false, powerPreference: 'high-performance' }}
        frameloop="always"
      >
        <PerspectiveCamera makeDefault position={[0, 0, 6]} fov={50} />
        <color attach="background" args={['#050510']} />
        <fog attach="fog" args={['#050510', 12, 28]} />

        <ambientLight intensity={0.15} />
        <pointLight position={[2, 3, 4]} intensity={0.8} color="#8b5cf6" />
        <pointLight position={[-3, -1, 2]} intensity={0.4} color="#22d3ee" />

        <Stars radius={60} depth={60} count={2500} factor={3} saturation={0} fade speed={0.8} />

        {/* Orb — shifted up when there's a response */}
        <group position={[0, responseText ? 1.2 : 0, 0]}>
          <OrionCore3D onTap={onTap} onLongPress={onLongPress} onDoubleTap={onDoubleTap} />
        </group>

        {/* 3D response text — single stable instance */}
        <ResponseText3D text={responseText} isStreaming={isStreaming} />

        <EffectComposer>
          <Bloom luminanceThreshold={0.25} luminanceSmoothing={0.8} height={200} intensity={0.6} />
          <Vignette eskil={false} offset={0.15} darkness={1.0} />
        </EffectComposer>
      </Canvas>
    </div>
  );
};
