export interface PersistedGrant {
  id: number;
  key: string;
  type: string;
  subjectId?: string;
  sessionId?: string;
  clientId: string;
  description?: string;
  creationTime: string;
  expiration?: string;
  consumedTime?: string;
  data: string;
}

export interface DeviceFlowCode {
  id: number;
  deviceCode: string;
  userCode: string;
  subjectId?: string;
  sessionId?: string;
  clientId: string;
  description?: string;
  creationTime: string;
  expiration: string;
  data: string;
}

export interface ServerSideSession {
  id: number;
  key: string;
  scheme: string;
  subjectId: string;
  sessionId?: string;
  displayName?: string;
  created: string;
  renewed: string;
  expires?: string;
}

export interface GrantFilter {
  subjectId?: string;
  clientId?: string;
  sessionId?: string;
  type?: string;
}
