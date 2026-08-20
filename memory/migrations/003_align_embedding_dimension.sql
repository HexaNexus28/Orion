-- Migration: 003_align_embedding_dimension.sql
-- Date: 2026-08-20
-- Description: Aligne memory_vectors.embedding sur la dimension réellement produite.
--
-- POURQUOI
-- La base live déclarait `vector(1536)` — la dimension par défaut d'OpenAI — alors que
-- `memory/schema.sql` annonçait `vector(768)` et qu'ORION vectorise avec `nomic-embed-text`,
-- qui produit 768 dimensions. La doc et la réalité avaient divergé.
--
-- Symptôme : toute recherche sémantique échouait avec
--   Npgsql.PostgresException 22000: different vector dimensions 768 and 1536
-- exception avalée par un catch, d'où un « RAG indisponible » silencieux et une mémoire
-- qui n'a jamais rien pu restituer.
--
-- SÛRETÉ
-- Aucune donnée n'est perdue : au moment de la migration, tous les vecteurs stockés valaient
-- NULL (la colonne pgvector était hors du modèle EF, donc jamais écrite).
--
-- INDEX
-- L'index ivfflat n'est volontairement PAS recréé : sur une table quasi vide il ne sert à rien
-- et pgvector recommande de le construire une fois les données présentes. À ajouter quand la
-- mémoire aura du volume :
--   CREATE INDEX ON memory_vectors USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);

BEGIN;

-- Garde-fou : refuser la migration si des vecteurs existent déjà, pour ne pas les détruire.
DO $$
DECLARE existants INTEGER;
BEGIN
    SELECT COUNT(*) INTO existants FROM memory_vectors WHERE embedding IS NOT NULL;
    IF existants > 0 THEN
        RAISE EXCEPTION
            'Migration refusée : % vecteur(s) déjà stocké(s). Changer la dimension imposerait de les revectoriser — le faire explicitement.',
            existants;
    END IF;
END $$;

DROP INDEX IF EXISTS memory_vectors_embedding_idx;

ALTER TABLE memory_vectors
    ALTER COLUMN embedding TYPE vector(768);

COMMIT;
