-- Migration: 001_add_behavior_patterns.sql
-- Date: 2025-04-07
-- Description: Ajout de la table behavior_patterns pour la détection de patterns utilisateur

-- Drop if exists (safe migration)
DROP TABLE IF EXISTS behavior_patterns CASCADE;

-- Create behavior_patterns table
CREATE TABLE behavior_patterns (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pattern_type TEXT NOT NULL,          -- 'skip_meal', 'late_night', 'focus_flow', 'stress', etc.
    observed_at TIMESTAMPTZ DEFAULT NOW(),
    context TEXT,                        -- description du contexte observé
    orion_response TEXT                  -- comment ORION a réagi (pour apprendre)
);

-- Indexes
CREATE INDEX idx_behavior_patterns_type ON behavior_patterns(pattern_type);
CREATE INDEX idx_behavior_patterns_observed ON behavior_patterns(observed_at);

-- Enable RLS
ALTER TABLE behavior_patterns ENABLE ROW LEVEL SECURITY;

-- Note: Pour ajouter une RLS policy (optionnel, à configurer selon tes besoins)
-- CREATE POLICY "Allow all" ON behavior_patterns FOR ALL USING (true);
