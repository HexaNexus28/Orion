import { Float, Billboard, RoundedBox, Text } from '@react-three/drei';
import { useRef } from 'react';
import { Group } from 'three';

export interface HologramCardProps {
  title: string;
  value: string | number;
  subtitle?: string;
  position?: [number, number, number];
  color?: string;
}

/**
 * HologramCard - Carte de données flottante en 3D
 * Utilise Float (apesanteur) + Billboard (toujours face caméra)
 * Affiche des métriques/statistiques qui orbitent autour de l'entité
 */
export const HologramCard: React.FC<HologramCardProps> = ({
  title,
  value,
  subtitle,
  position = [1.5, 0.5, 0],
  color = '#8b5cf6',
}) => {
  const groupRef = useRef<Group>(null);

  return (
    <Float
      speed={2}
      rotationIntensity={0.3}
      floatIntensity={0.5}
      floatingRange={[-0.1, 0.1]}
    >
      <group ref={groupRef} position={position}>
        {/* Billboard keeps the card facing camera */}
        <Billboard>
          {/* Card background */}
          <RoundedBox
            args={[1.2, 0.8, 0.05]}
            radius={0.05}
            smoothness={4}
          >
            <meshStandardMaterial
              color="#1a1a2e"
              transparent
              opacity={0.9}
              emissive={color}
              emissiveIntensity={0.2}
            />
          </RoundedBox>

          {/* Glow effect */}
          <pointLight
            position={[0, 0, 0.5]}
            intensity={0.5}
            color={color}
            distance={2}
          />

          {/* Title text */}
          <Text
            position={[0, 0.2, 0.03]}
            fontSize={0.08}
            color="#a78bfa"
            anchorX="center"
            anchorY="middle"
            maxWidth={1}
          >
            {title}
          </Text>

          {/* Value text */}
          <Text
            position={[0, 0, 0.03]}
            fontSize={0.15}
            color={color}
            anchorX="center"
            anchorY="middle"
            font="/fonts/inter-bold.woff"
          >
            {value.toString()}
          </Text>

          {/* Subtitle text */}
          {subtitle && (
            <Text
              position={[0, -0.2, 0.03]}
              fontSize={0.06}
              color="#6b7280"
              anchorX="center"
              anchorY="middle"
              maxWidth={1}
            >
              {subtitle}
            </Text>
          )}
        </Billboard>
      </group>
    </Float>
  );
};

export default HologramCard;
