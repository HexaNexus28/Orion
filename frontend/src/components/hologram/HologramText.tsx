import { Float, Billboard, Text } from '@react-three/drei';

export interface HologramTextProps {
  text: string;
  position?: [number, number, number];
  fontSize?: number;
  color?: string;
  maxWidth?: number;
}

/**
 * HologramText - Texte 3D flottant dans l'espace
 * Utilisé pour afficher des réponses courtes ou des labels
 * Toujours face à la caméra via Billboard
 */
export const HologramText: React.FC<HologramTextProps> = ({
  text,
  position = [0, 1.5, 0],
  fontSize = 0.12,
  color = '#e5e7eb',
  maxWidth = 2,
}) => {
  return (
    <Float
      speed={1.5}
      rotationIntensity={0.1}
      floatIntensity={0.3}
      floatingRange={[-0.05, 0.05]}
    >
      <group position={position}>
        <Billboard>
          <Text
            fontSize={fontSize}
            color={color}
            anchorX="center"
            anchorY="middle"
            maxWidth={maxWidth}
            lineHeight={1.2}
            letterSpacing={0.02}
          >
            {text}
          </Text>
        </Billboard>
      </group>
    </Float>
  );
};

export default HologramText;
