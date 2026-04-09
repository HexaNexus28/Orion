// ApiResponse.ts - Miroir TypeScript de ApiResponse<T> .NET
export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  message?: string;
  statusCode: number;
  errors?: Record<string, string[]>;
}
