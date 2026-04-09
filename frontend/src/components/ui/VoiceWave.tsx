import { motion } from 'framer-motion';

interface VoiceWaveProps {
  isActive: boolean;
  amplitude: number;
}

export const VoiceWave: React.FC<VoiceWaveProps> = ({ isActive, amplitude }) => {
  const bars = Array.from({ length: 20 });

  return (
    <div className="flex items-center justify-center gap-1 h-12">
      {bars.map((_, i) => {
        const delay = i * 0.05;
        const height = isActive
          ? Math.max(4, amplitude * 40 * (0.5 + Math.random() * 0.5))
          : 4;

        return (
          <motion.div
            key={i}
            className="w-1 bg-orion-accent rounded-full"
            animate={{
              height: isActive ? [4, height, 4] : 4,
              opacity: isActive ? 1 : 0.3
            }}
            transition={{
              duration: 0.2,
              repeat: isActive ? Infinity : 0,
              repeatType: 'reverse',
              delay: isActive ? delay : 0
            }}
          />
        );
      })}
    </div>
  );
};
