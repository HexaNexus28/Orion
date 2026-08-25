-- ============================================================================
-- 005 — Embedding distant : Ollama local -> fournisseur compatible OpenAI (J6b)
-- ============================================================================
-- POURQUOI. Le cerveau etait passe sur NIM en J3, mais `EmbeddingService` appelait toujours
-- Ollama sur localhost:11434. Sur le poste ca fonctionne ; sur le VPS il n'y a PAS d'Ollama,
-- donc la memoire y serait morte EN SILENCE — aucune requete en erreur, juste une recherche
-- qui ne remonte plus rien. C'etait le dernier obstacle a un ORION 24/7, PC eteint.
--
-- CHOIX DU MODELE, mesure le 2026-08-25 en APPELANT les API (le catalogue ment) :
--   mistral-embed                      VIVANT  1024 dims   <- retenu
--   nvidia/nemotron-3-embed-1b         VIVANT  2048 dims   <- refuse : pgvector plafonne
--                                                             ses index a 2000 dimensions
--   nvidia/llama-3.2-nv-embedqa-1b-v2  410 Gone
--   ...-v1 / embed-qa-4 / arctic-embed-l / nv-embedqa-mistral-7b-v2   404
--
-- Effet de bord VOULU : cerveau et memoire sont chez deux fournisseurs DIFFERENTS. Un embedding
-- ne peut pas basculer a chaud (chaque modele a son propre espace vectoriel), donc les mettre
-- tous les deux chez NIM aurait fait tomber les deux ensemble. Separes, une panne NIM laisse la
-- memoire intacte pendant que le cerveau bascule.
--
-- ⚠️ CETTE MIGRATION VIDE LES VECTEURS. C'est inevitable et non destructif pour le CONTENU :
-- les vecteurs nomic-embed-text (768) et mistral-embed (1024) vivent dans des espaces
-- incomparables. Les melanger ne leve aucune erreur et renvoie des resultats absurdes.
-- Le texte des souvenirs (`content`) est CONSERVE — seule la representation vectorielle est
-- recalculee, par la commande de revectorisation.
-- ============================================================================

BEGIN;

-- 1. L'ancien index porte sur une colonne qui change de type : il doit partir d'abord.
DROP INDEX IF EXISTS memory_vectors_embedding_idx;
DROP INDEX IF EXISTS idx_memory_vectors_embedding;

-- 2. Nouvelle dimension. On remplace la colonne au lieu de la convertir : aucune conversion
--    n'a de SENS entre deux espaces vectoriels, et tout sera reecrit de toute facon.
ALTER TABLE memory_vectors DROP COLUMN IF EXISTS embedding;
ALTER TABLE memory_vectors ADD COLUMN embedding vector(1024);

-- 3. Tracabilite de l'espace vectoriel. C'est CE qui rend la mort d'un fournisseur survivable :
--    sans ces colonnes, un melange de deux modeles est indetectable et empoisonne la recherche
--    en silence. Avec elles, on peut reperer, filtrer et revectoriser par lot.
ALTER TABLE memory_vectors ADD COLUMN IF NOT EXISTS embedding_model text;
ALTER TABLE memory_vectors ADD COLUMN IF NOT EXISTS embedding_dims  int;

COMMENT ON COLUMN memory_vectors.embedding_model IS
  'Modele ayant produit le vecteur. Deux modeles = deux espaces incomparables : ne JAMAIS
   comparer des lignes dont ce champ differe.';

-- 4. Index HNSW : possible ici parce que 1024 < 2000 (la limite de pgvector). C'etait
--    l'argument decisif contre les 2048 dimensions de nemotron.
CREATE INDEX memory_vectors_embedding_idx
  ON memory_vectors USING hnsw (embedding vector_cosine_ops);

-- 5. Marquer les lignes existantes comme A REVECTORISER (embedding NULL, modele NULL).
--    Elles restent lisibles, elles ne remontent simplement plus dans la recherche semantique
--    tant que la commande de revectorisation n'est pas passee.

COMMIT;

-- Verification attendue apres migration :
--   SELECT count(*) FILTER (WHERE embedding IS NULL) AS a_revectoriser,
--          count(*)                                  AS total
--   FROM memory_vectors;
