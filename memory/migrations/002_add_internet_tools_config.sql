-- Migration: 002_add_internet_tools_config.sql
-- Date: 2025-04-07
-- Description: Ajout d'une table pour la configuration des tools Internet (optionnel)
-- Les clés API sont stockées dans appsettings/backend, pas en DB

-- Cette migration est optionnelle - garde un historique des changements
-- Si tu veux stocker des configs utilisateur dynamiques:

/*
CREATE TABLE tool_configs (
    tool_name TEXT PRIMARY KEY,
    config JSONB NOT NULL DEFAULT '{}',
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

ALTER TABLE tool_configs ENABLE ROW LEVEL SECURITY;
CREATE INDEX idx_tool_configs_name ON tool_configs(tool_name);
*/

-- Pour l'instant, on n'ajoute rien - la config reste dans appsettings.json
-- Cette migration documente le changement pour l'historique
