import { Float, Billboard, Text } from '@react-three/drei';
import { useMemo } from 'react';
import * as THREE from 'three';

export interface DataPoint {
  label: string;
  value: number;
  color?: string;
}

export interface HologramChartProps {
  data: DataPoint[];
  position?: [number, number, number];
  title?: string;
  type?: 'bar' | 'pie';
}

/**
 * HologramChart - Graphique 3D flottant pour visualiser des données
 * Types supportés : bar chart (par défaut), pie chart
 * Utilisé pour afficher des statistiques ShiftStar ou métriques système
 */
export const HologramChart: React.FC<HologramChartProps> = ({
  data,
  position = [-1.5, 0, 0],
  title,
  type = 'bar',
}) => {
  const maxValue = useMemo(() => Math.max(...data.map(d => d.value)), [data]);

  if (type === 'pie') {
    // Pie chart implementation
    const total = data.reduce((sum, d) => sum + d.value, 0);
    let currentAngle = 0;

    return (
      <Float speed={1.5} rotationIntensity={0.2} floatIntensity={0.3}>
        <group position={position}>
          <Billboard>
            {/* Title */}
            {title && (
              <Text position={[0, 0.8, 0]} fontSize={0.08} color="#a78bfa" anchorX="center">
                {title}
              </Text>
            )}

            {/* Pie segments */}
            {data.map((point, index) => {
              const angle = (point.value / total) * Math.PI * 2;
              const geometry = new THREE.CircleGeometry(0.4, 32, currentAngle, angle);
              const material = new THREE.MeshBasicMaterial({
                color: point.color || `hsl(${(index * 360) / data.length}, 70%, 60%)`,
                side: THREE.DoubleSide,
              });
              currentAngle += angle;

              return (
                <mesh
                  key={point.label}
                  geometry={geometry}
                  material={material}
                  position={[0, 0, 0]}
                />
              );
            })}
          </Billboard>
        </group>
      </Float>
    );
  }

  // Bar chart (default)
  const barWidth = 0.15;
  const spacing = 0.05;
  const totalWidth = data.length * barWidth + (data.length - 1) * spacing;
  const startX = -totalWidth / 2 + barWidth / 2;

  return (
    <Float speed={1.5} rotationIntensity={0.2} floatIntensity={0.3}>
      <group position={position}>
        <Billboard>
          {/* Title */}
          {title && (
            <Text position={[0, 0.8, 0]} fontSize={0.08} color="#a78bfa" anchorX="center">
              {title}
            </Text>
          )}

          {/* Bars */}
          {data.map((point, index) => {
            const height = (point.value / maxValue) * 0.6;
            const x = startX + index * (barWidth + spacing);

            return (
              <group key={point.label} position={[x, 0, 0]}>
                {/* Bar */}
                <mesh position={[0, height / 2, 0]}>
                  <boxGeometry args={[barWidth, height, 0.05]} />
                  <meshStandardMaterial
                    color={point.color || '#8b5cf6'}
                    emissive={point.color || '#8b5cf6'}
                    emissiveIntensity={0.3}
                    transparent
                    opacity={0.9}
                  />
                </mesh>

                {/* Label */}
                <Text position={[0, -0.15, 0.03]} fontSize={0.05} color="#6b7280" anchorX="center">
                  {point.label}
                </Text>

                {/* Value */}
                <Text position={[0, height + 0.1, 0]} fontSize={0.06} color="#e5e7eb" anchorX="center">
                  {point.value.toString()}
                </Text>
              </group>
            );
          })}

          {/* Base line */}
          <mesh position={[0, 0, 0]}>
            <boxGeometry args={[totalWidth + 0.2, 0.01, 0.01]} />
            <meshStandardMaterial color="#4b5563" />
          </mesh>
        </Billboard>
      </group>
    </Float>
  );
};

export default HologramChart;
