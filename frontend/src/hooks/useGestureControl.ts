import { useCallback, useRef, useState } from 'react';
import { useHandTracking } from './useHandTracking';
import type { HandGesture, HandTrackingResult } from '../algorithms/handTracker';

interface GestureCommand {
  gesture: HandGesture;
  action: () => void;
  cooldownMs: number;
  description: string;
}

interface UseGestureControlOptions {
  onOpenPalm?: () => void;      // Pause/Play
  onClosedFist?: () => void;   // Stop
  onPointUp?: () => void;       // Volume up / Scroll up
  onPointDown?: () => void;     // Volume down / Scroll down
  onSwipeLeft?: () => void;     // Previous
  onSwipeRight?: () => void;    // Next
  onThumbsUp?: () => void;      // Validate / Like
  onThumbsDown?: () => void;    // Cancel / Dislike
  enabled?: boolean;
}

export const useGestureControl = (options: UseGestureControlOptions = {}) => {
  const {
    onOpenPalm,
    onClosedFist,
    onPointUp,
    onPointDown,
    onSwipeLeft,
    onSwipeRight,
    onThumbsUp,
    onThumbsDown,
    enabled = true,
  } = options;

  const [lastGesture, setLastGesture] = useState<HandGesture | null>(null);
  const [gestureConfidence, setGestureConfidence] = useState(0);
  const cooldownsRef = useRef<Record<string, number>>({});
  const swipeStartRef = useRef<{ x: number; y: number; time: number } | null>(null);

  const { isTracking, startTracking, stopTracking, videoRef } = useHandTracking({
    enabled,
    onGestureDetected: useCallback((result: HandTrackingResult) => {
      if (!enabled) return;

      if (!result.handPresent) {
        setLastGesture(null);
        setGestureConfidence(0);
        swipeStartRef.current = null;
        return;
      }

      const now = Date.now();
      const gesture = result.gesture;

      // Check cooldown
      if (cooldownsRef.current[gesture] && now < cooldownsRef.current[gesture]) {
        return;
      }

      // Update state
      setLastGesture(gesture);
      setGestureConfidence(result.landmarks ? 0.9 : 0.5);

      // Handle swipe detection
      if (result.landmarks && result.landmarks.length > 0) {
        const wrist = result.landmarks[0];
        const currentPos = { x: wrist.x, y: wrist.y };

        if (swipeStartRef.current) {
          const dx = currentPos.x - swipeStartRef.current.x;
          const dy = currentPos.y - swipeStartRef.current.y;
          const dt = now - swipeStartRef.current.time;

          // Detect horizontal swipe (fast movement)
          if (dt < 500 && Math.abs(dx) > 0.3) {
            if (dx > 0 && onSwipeRight) {
              executeGesture('swipe_right', onSwipeRight, 800);
            } else if (dx < 0 && onSwipeLeft) {
              executeGesture('swipe_left', onSwipeLeft, 800);
            }
            swipeStartRef.current = null;
            return;
          }

          if (dt < 500 && Math.abs(dy) > 0.25) {
            if (dy < 0 && onPointUp) {
              executeGesture('point_up', onPointUp, 800);
            } else if (dy > 0 && onPointDown) {
              executeGesture('point_down', onPointDown, 800);
            }
            swipeStartRef.current = null;
            return;
          }
        }

        // Start new swipe tracking
        if (!swipeStartRef.current || now - swipeStartRef.current.time > 500) {
          swipeStartRef.current = { x: currentPos.x, y: currentPos.y, time: now };
        }
      }

      // Handle static gestures
      switch (gesture) {
        case 'open_palm':
          if (onOpenPalm) executeGesture('open_palm', onOpenPalm, 1500);
          break;
        case 'closed_fist':
          if (onClosedFist) executeGesture('closed_fist', onClosedFist, 1500);
          break;
        case 'pointing':
          if (onPointUp) executeGesture('pointing', onPointUp, 800);
          break;
        case 'thumbs_up':
          if (onThumbsUp) {
            executeGesture('thumbs_up', onThumbsUp, 1200);
          }
          break;
        case 'thumbs_down':
          if (onThumbsDown) {
            executeGesture('thumbs_down', onThumbsDown, 1200);
          }
          break;
      }
    }, [enabled, onOpenPalm, onClosedFist, onPointUp, onPointDown, onSwipeLeft, onSwipeRight, onThumbsUp, onThumbsDown]),
  });

  const executeGesture = (gestureName: string, action: () => void, cooldownMs: number) => {
    const now = Date.now();
    cooldownsRef.current[gestureName] = now + cooldownMs;
    action();
  };

  return {
    isTracking,
    startTracking,
    stopTracking,
    lastGesture,
    gestureConfidence,
    videoRef,
    isGestureActive: lastGesture !== null && lastGesture !== 'none',
  };
};

export type { GestureCommand, UseGestureControlOptions };
