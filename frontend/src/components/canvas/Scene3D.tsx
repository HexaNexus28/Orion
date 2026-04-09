import { Canvas } from '@react-three/fiber';
import { OrbitControls } from '@react-three/drei';
import { Suspense, ReactNode } from 'react';

interface Scene3DProps {
  children?: ReactNode;
}

/**
 * Scene3D - Scène Three.js principale pour ORION
 * Contient l'entité 3D et les éléments holographiques
 * @react-three/fiber + @react-three/drei
 */
export const Scene3D: React.FC<Scene3DProps> = ({ children }) => {
  return (
    <div className="absolute inset-0 z-1">
      <Canvas
        camera={{ position: [0, 0, 5], fov: 50 }}
        gl={{ antialias: true, alpha: true }}
        style={{ background: 'transparent' }}
      >
        <Suspense fallback={null}>
          {/* Lighting */}
          <ambientLight intensity={0.5} />
          <pointLight position={[10, 10, 10]} intensity={1} color="#8b5cf6" />
          <pointLight position={[-10, -10, -10]} intensity={0.5} color="#6366f1" />

          {/* Content */}
          {children}

          {/* Optional controls for debug */}
          <OrbitControls
            enableZoom={false}
            enablePan={false}
            rotateSpeed={0.5}
          />
        </Suspense>
      </Canvas>
    </div>
  );
};

export default Scene3D;
