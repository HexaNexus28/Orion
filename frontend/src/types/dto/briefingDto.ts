// briefingDto.ts - DTOs Briefing
export interface BriefingDto {
  id: string;
  content: string;
  createdAt: string;
  stats?: Record<string, unknown>;
  // Extended fields for UI display
  shiftStarStats?: {
    votes: number;
    rating: number;
    mrr: number;
  };
  calendarEvents?: Array<{
    title: string;
    time: string;
  }>;
  unreadEmails?: number;
  summary?: string;
}
