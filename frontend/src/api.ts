export interface ApiErrorBody {
  code: string;
  message: string;
  request_id?: string | null;
  suggested_model?: string | null;
  candidates?: string[] | null;
}

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: 'include',
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {})
    }
  });

  if (!response.ok) {
    let body: { error?: ApiErrorBody } = {};
    try {
      body = (await response.json()) as { error?: ApiErrorBody };
    } catch {
      // ignore non-json error bodies
    }
    throw new ApiError(
      response.status,
      body.error?.code ?? 'unknown',
      body.error?.message ?? response.statusText
    );
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export const api = {
  login: (userName: string, password: string) =>
    request<{ user: { user_name: string; roles: number[] } }>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ userName, password })
    }),
  logout: () => request<{ ok: boolean }>('/api/auth/logout', { method: 'POST' }),
  me: () => request<{ user: { user_name: string; roles: number[]; team_id?: string } }>('/api/auth/me'),

  providers: () => request<ProviderRow[]>('/api/admin/providers'),
  createProvider: (body: CreateProviderBody) =>
    request<{ id: string; alias: string }>('/api/admin/providers', {
      method: 'POST',
      body: JSON.stringify(body)
    }),

  keys: () => request<KeyRow[]>('/api/admin/keys'),
  createKey: (body: { displayName: string; ownerType: 0 | 1; ownerId: string }) =>
    request<{ key: string; key_id: string }>('/api/admin/keys', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  revokeKey: (id: string) =>
    request<{ ok: boolean }>(`/api/admin/keys/${id}/revoke`, { method: 'POST' }),

  audit: (params?: { from?: string; to?: string; eventType?: string }) => {
    const search = new URLSearchParams();
    if (params?.from) search.set('from', params.from);
    if (params?.to) search.set('to', params.to);
    if (params?.eventType) search.set('eventType', params.eventType);
    const query = search.toString();
    return request<{ count: number; data: AuditRow[] }>(`/api/admin/audit${query ? `?${query}` : ''}`);
  },
  verifyAudit: () =>
    request<{ ok: boolean; checked_count: number; first_break_sequence: number | null }>(
      '/api/admin/audit/verify'
    ),

  myProviders: () => request<MyProviderRow[]>('/api/me/providers'),
  createMyProvider: (body: CreateProviderBody) =>
    request<{ id: string; alias: string }>('/api/me/providers', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  myKeys: () => request<MyKeyRow[]>('/api/me/keys'),
  createMyKey: () =>
    request<{ key: string; key_id: string }>('/api/me/keys', { method: 'POST' }),
  myUsage: () =>
    request<{ data: UsageRow[] }>('/api/me/usage'),

  models: () =>
    request<{ data: ModelRow[] }>('/v1/models', {
      headers: { Authorization: `Bearer ${localStorage.getItem('sh.devkey') ?? ''}` }
    })
};

export interface ProviderRow {
  id: string;
  alias: string;
  providerKey: string;
  baseUrl: string;
  displayName: string;
  scope: string;
  enabled: boolean;
  healthStatus: string;
}

export interface MyProviderRow {
  id: string;
  alias: string;
  providerKey: string;
  baseUrl: string;
  models: string[];
}

export interface CreateProviderBody {
  alias: string;
  providerKey: string;
  baseUrl: string;
  apiKey: string;
  models: string[];
}

export interface KeyRow {
  id: string;
  key_id: string;
  prefix: string;
  display_name: string;
  owner_type: string;
  owner_id: string;
  is_revoked: boolean;
  created_at: string;
  expires_at?: string | null;
}

export interface MyKeyRow {
  id: string;
  key_id: string;
  prefix: string;
  display_name: string;
  is_revoked: boolean;
  created_at: string;
}

export interface AuditRow {
  eventId: string;
  sequence: number;
  occurredAt: string;
  eventType: string;
  severity: string;
  action: string;
  actorType: string;
  actorLabel: string;
  requestId?: string | null;
  modelAlias?: string | null;
  ruleId?: string | null;
  reasonCode: string;
  inputTokens?: number | null;
  inputTokensOriginal?: number | null;
  outputTokens?: number | null;
  billing_owner: string;
  hash: string;
}

export interface UsageRow {
  model: string;
  requests: number;
  input_tokens: number;
  output_tokens: number;
  cost_usd: number;
}

export interface ModelRow {
  id: string;
  owned_by: string;
  x_securehub: {
    source: string;
    billing: string;
    context_window: number;
  };
}
