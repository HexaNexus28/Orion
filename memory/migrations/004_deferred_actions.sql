-- Migration: 004_deferred_actions.sql
-- Date: 2026-08-21
-- Description: File d'actions différées — ce qu'ORION ne peut pas faire maintenant
--              parce que le PC est éteint, et qu'il fera à son réveil.
--
-- POURQUOI
-- ORION se scinde en deux plans (docs/orion-always-on.md) : le « cerveau » qui doit tourner
-- 24/7, et les « mains » — le daemon et ses 13 outils système — qui agissent sur CE PC et ne
-- sont pas déportables. Sans cette table, PC éteint donne « Daemon non connecté » : un échec
-- sec qui fait PARAÎTRE ORION cassé alors qu'il fonctionne. C'est la dégradation muette qui a
-- déjà coûté des mois sur ce projet.
--
-- CE QUI EST FIGÉ DANS LA LIGNE, ET POURQUOI
-- `is_destructive` est copié au moment de l'enfilement, pas relu à l'exécution. Si le drapeau
-- change dans le code demain, une action déjà en file garde le régime sous lequel l'utilisateur
-- l'a demandée. Une file qui change de règles entre la demande et l'exécution n'est pas une file,
-- c'est une surprise.
--
-- TTL
-- 24 h par défaut. Un `git_commit` d'hier soir exécuté trois jours plus tard n'est pas un service.
-- L'expiration est portée par la DONNÉE (`expires_at`), pas par le code qui draine : une action
-- reste expirable même si le backend est resté éteint.

BEGIN;

CREATE TABLE IF NOT EXISTS deferred_actions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Le nom de l'OUTIL (`open_app`, `git_commit`), pas l'action daemon : c'est le niveau
    -- auquel l'utilisateur a formulé sa demande, donc le seul auquel on peut la lui redemander.
    tool_name       TEXT NOT NULL,
    arguments       JSONB NOT NULL DEFAULT '{}'::jsonb,

    -- pending | awaiting_confirmation | executed | failed | expired | cancelled
    status          TEXT NOT NULL DEFAULT 'pending',
    is_destructive  BOOLEAN NOT NULL DEFAULT FALSE,

    -- D'où venait la demande : 'chat' (l'utilisateur a parlé) ou 'proactive' (ORION a décidé).
    origin          TEXT NOT NULL DEFAULT 'chat',
    -- Le fil dans lequel répondre au réveil. NULL = hors conversation.
    conversation_id UUID REFERENCES conversations(id) ON DELETE SET NULL,
    -- La phrase exacte de l'utilisateur, pour pouvoir la lui rappeler telle quelle.
    requested_by    TEXT,

    requested_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at      TIMESTAMPTZ NOT NULL DEFAULT NOW() + INTERVAL '24 hours',
    resolved_at     TIMESTAMPTZ,
    result          TEXT,
    error           TEXT,

    CONSTRAINT deferred_actions_status_valide CHECK (
        status IN ('pending', 'awaiting_confirmation', 'executed', 'failed', 'expired', 'cancelled')
    ),
    CONSTRAINT deferred_actions_origin_valide CHECK (origin IN ('chat', 'proactive'))
);

-- Le drain ne lit QUE ce qui est encore vivant : index partiel sur les deux seuls états actifs.
CREATE INDEX IF NOT EXISTS idx_deferred_actions_en_attente
    ON deferred_actions (expires_at)
    WHERE status IN ('pending', 'awaiting_confirmation');

CREATE INDEX IF NOT EXISTS idx_deferred_actions_requested
    ON deferred_actions (requested_at DESC);

ALTER TABLE deferred_actions ENABLE ROW LEVEL SECURITY;

COMMIT;
