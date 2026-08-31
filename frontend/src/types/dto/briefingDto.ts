// briefingDto.ts - DTOs Briefing
// Miroir de Orion.Core/DTOs/Responses/BriefingDto.cs — garder les deux alignés.
//
// `shiftStarStats`, `calendarEvents`, `unreadEmails` et `summary` ont été retirés : le serveur
// ne les a jamais envoyés. L'overlay affichait `summary`, donc du vide, depuis toujours.

/** Un article réellement collecté : c'est ce qui rend le briefing vérifiable. */
export interface BriefingSource {
  title: string;
  url: string;
  source: string;
  /** 'local' | 'africa' | 'world' */
  circle: string;
}

export interface BriefingDto {
  id: string;
  /** Le texte du briefing — c'est CE champ que le serveur remplit. */
  content: string;
  createdAt: string;
  stats?: Record<string, unknown>;
  sources?: BriefingSource[];
}
