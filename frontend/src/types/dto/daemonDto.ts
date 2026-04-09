// daemonDto.ts - DTOs Daemon
export interface DaemonActionRequest {
  action: string;
  payload: unknown;
  requestId: string;
}

export interface DaemonActionResponse {
  requestId: string;
  success: boolean;
  data?: unknown;
  error?: string;
  timestamp: number;
}

export interface DaemonStatus {
  connected: boolean;
  machineName: string;
}

export interface DaemonToolInfo {
  name: string;
  description: string;
  parameters: string[];
}
