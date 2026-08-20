// healthDto.ts - DTOs Health

export interface HealthCheckDto {
  status: string;
  /** Nom de l'enum backend : "None" | "Ollama" | "Nim". */
  llmProvider: string;
  /** Modèle réellement actif — un repli silencieux ne doit plus être invisible. */
  llmModel: string;
  timestamp: string;
}
