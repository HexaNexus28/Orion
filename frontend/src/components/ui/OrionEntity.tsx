import React, { useEffect, useRef, useCallback } from 'react';
import { motion } from 'framer-motion';
import type { OrionEntityProps } from '../../types';
import { getStateConfig } from '../../utils/animationUtils';

export const OrionEntity: React.FC<OrionEntityProps> = ({
  state,
  amplitude = 0,
  onTap,
  onLongPress,
  onDoubleTap,
}) => {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const config = getStateConfig(state);
  const longPressTimer = useRef<NodeJS.Timeout | null>(null);
  const isLongPress = useRef(false);
  const lastTapTime = useRef<number>(0);

  const handlePointerDown = useCallback(() => {
    isLongPress.current = false;
    longPressTimer.current = setTimeout(() => {
      isLongPress.current = true;
      onLongPress?.();
    }, 500);
  }, [onLongPress]);

  const handlePointerUp = useCallback(() => {
    if (longPressTimer.current) {
      clearTimeout(longPressTimer.current);
      longPressTimer.current = null;
    }
    if (!isLongPress.current) {
      const now = Date.now();
      const timeSinceLast = now - lastTapTime.current;

      if (timeSinceLast < 300 && lastTapTime.current > 0) {
        // Double tap
        lastTapTime.current = 0;
        onDoubleTap?.();
      } else {
        lastTapTime.current = now;
        onTap?.();
      }
    }
  }, [onTap, onDoubleTap]);

  const handlePointerLeave = useCallback(() => {
    if (longPressTimer.current) {
      clearTimeout(longPressTimer.current);
      longPressTimer.current = null;
    }
  }, []);

  // Canvas animation for rings
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    let animationId: number;
    let time = 0;

    const resize = () => {
      canvas.width = canvas.offsetWidth * window.devicePixelRatio;
      canvas.height = canvas.offsetHeight * window.devicePixelRatio;
      ctx.scale(window.devicePixelRatio, window.devicePixelRatio);
    };

    resize();
    window.addEventListener('resize', resize);

    const animate = () => {
      const width = canvas.offsetWidth;
      const height = canvas.offsetHeight;
      const centerX = width / 2;
      const centerY = height / 2;
      const baseRadius = 60;

      ctx.clearRect(0, 0, width, height);

      // Draw rings
      for (let i = 0; i < config.rings; i++) {
        const offset = (i / config.rings) * Math.PI * 2;
        const wave = Math.sin(time * config.speed + offset) * 10;
        const ampBoost = 1 + amplitude * 0.5;
        const radius = (baseRadius + i * 25 + wave) * ampBoost;
        const alpha = 0.6 - (i / config.rings) * 0.4;

        ctx.beginPath();
        ctx.arc(centerX, centerY, radius, 0, Math.PI * 2);
        ctx.strokeStyle = config.color.replace(')', `, ${alpha})`).replace('rgb', 'rgba');
        ctx.lineWidth = 2;
        ctx.stroke();
      }

      // Draw core
      const coreRadius = 20 + amplitude * 15;
      const gradient = ctx.createRadialGradient(
        centerX, centerY, 0,
        centerX, centerY, coreRadius
      );
      gradient.addColorStop(0, config.color);
      gradient.addColorStop(1, 'transparent');

      ctx.beginPath();
      ctx.arc(centerX, centerY, coreRadius, 0, Math.PI * 2);
      ctx.fillStyle = gradient;
      ctx.fill();

      // Glow effect
      ctx.shadowBlur = 20 + amplitude * 20;
      ctx.shadowColor = config.color;

      time += 0.05;
      animationId = requestAnimationFrame(animate);
    };

    animate();

    return () => {
      window.removeEventListener('resize', resize);
      cancelAnimationFrame(animationId);
    };
  }, [state, amplitude, config]);

  return (
    <motion.div
      className="relative w-80 h-80 cursor-pointer"
      onPointerDown={handlePointerDown}
      onPointerUp={handlePointerUp}
      onPointerLeave={handlePointerLeave}
      animate={{
        scale: state === 'listening' ? [1, 1.05, 1] : 1,
      }}
      transition={{
        duration: 1,
        repeat: state === 'listening' ? Infinity : 0,
        ease: "easeInOut"
      }}
    >
      {/* Glow background */}
      <div
        className="absolute inset-0 rounded-full blur-3xl opacity-30"
        style={{ backgroundColor: config.color }}
      />

      {/* Canvas for animated rings */}
      <canvas
        ref={canvasRef}
        className="absolute inset-0 w-full h-full"
      />

      {/* State indicator */}
      <motion.div
        className="absolute bottom-4 left-0 right-0 text-center text-xs uppercase tracking-widest"
        initial={{ opacity: 0, y: 10, color: config.color }}
        animate={{ opacity: 1, y: 0, color: config.color }}
        key={state}
      >
        {state}
      </motion.div>
    </motion.div>
  );
};
