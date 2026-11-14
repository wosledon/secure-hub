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

/** 解析一个 SSE 帧并分发到对应回调。 */
function dispatchFrame(frame: string, handlers: AssistantStreamHandlers) {
  let event = '';
  const dataLines: string[] = [];

  for (const raw of frame.split('\n')) {
    const line = raw.replace(/\r$/, '');
    if (line.startsWith(':')) continue;

    if (line.startsWith('event:')) {
      event = line.slice(6).trim();
    } else if (line.startsWith('data:')) {
      dataLines.push(line.slice(5).trimStart());
    }
  }

  if (dataLines.length === 0 || event.length === 0) return;
  const payload = dataLines.join('\n');

  try {
    if (event === 'delta') {
      const parsed = JSON.parse(payload) as { text?: string | null };
      if (parsed.text) handlers.onDelta(parsed.text);
    } else if (event === 'done') {
      handlers.onStats?.(JSON.parse(payload) as AssistantDoneEvent);
    } else if (event === 'session') {
      handlers.onSession?.(JSON.parse(payload) as AssistantSessionEvent);
    } else if (event === 'error') {
      const parsed = JSON.parse(payload) as { text?: string | null };
      handlers.onError?.(parsed.text ?? 'unknown error');
    }
  } catch {
    // 单个帧损坏不应中断整条流
  }
}

export const api = {
  login: (userName: string, password: string, totpCode?: string) =>
    request<{ user: { id: string; user_name: string; roles: number[]; team_id?: string } }>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ userName, password, totpCode })
    }),
  logout: () => request<{ ok: boolean }>('/api/auth/logout', { method: 'POST' }),
  me: () => request<{ user: { id: string; user_name: string; roles: number[]; team_id?: string } }>('/api/auth/me'),
  health: () => request<{ status: string; product: string; version: string }>('/health'),
  enableTotp: () =>
    request<{ secret: string; otpauth_uri: string; note: string }>('/api/auth/totp/enable', { method: 'POST' }),
  activateTotp: (code: string) =>
    request<{ activated: boolean }>('/api/auth/totp/activate', {
      method: 'POST',
      body: JSON.stringify({ code })
    }),

  providers: () => request<ProviderRow[]>('/api/admin/providers'),
  createProvider: (body: CreateProviderBody) =>
    request<{ id: string; alias: string }>('/api/admin/providers', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  updateProvider: (id: string, body: { displayName?: string; baseUrl?: string; enabled?: boolean }) =>
    request<ProviderRow>(`/api/admin/providers/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  disableProvider: (id: string) =>
    request<{ ok: boolean }>(`/api/admin/providers/${id}`, { method: 'DELETE' }),
  rotateProviderCredential: (id: string, apiKey: string) =>
    request<{ ok: boolean; key_hint: string }>(`/api/admin/providers/${id}/credential`, {
      method: 'POST',
      body: JSON.stringify({ apiKey })
    }),
  testProvider: (id: string) =>
    request<{ healthy: boolean; health_status: string; tested_at: string; error?: string | null }>(
      `/api/admin/providers/${id}/test`,
      { method: 'POST' }
    ),
  providerDetail: (id: string) => request<ProviderDetail>(`/api/admin/providers/${id}`),

  keys: () => request<KeyRow[]>('/api/admin/keys'),
  createKey: (body: { displayName: string; ownerType: 0 | 1; ownerId: string; scopes?: string[] }) =>
    request<{ key: string; key_id: string }>('/api/admin/keys', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  revokeKey: (id: string) =>
    request<{ ok: boolean }>(`/api/admin/keys/${id}/revoke`, { method: 'POST' }),

  audit: (params?: { from?: string; to?: string; appId?: string; eventType?: string }) => {
    const search = new URLSearchParams();
    if (params?.from) search.set('from', params.from);
    if (params?.to) search.set('to', params.to);
    if (params?.appId) search.set('appId', params.appId);
    if (params?.eventType) search.set('eventType', params.eventType);
    const query = search.toString();
    return request<{ count: number; data: AuditRow[] }>(`/api/admin/audit${query ? `?${query}` : ''}`);
  },
  verifyAudit: () =>
    request<{ ok: boolean; checked_count: number; first_break_sequence: number | null }>(
      '/api/admin/audit/verify'
    ),
  auditExportUrl: (params?: { from?: string; to?: string }) => {
    const search = new URLSearchParams();
    if (params?.from) search.set('from', params.from);
    if (params?.to) search.set('to', params.to);
    const query = search.toString();
    return `/api/admin/audit/export${query ? `?${query}` : ''}`;
  },
  disputes: () => request<AuditDisputeRow[]>('/api/admin/audit/disputes'),
  auditEventDetail: (eventId: string) => request<AuditEventDetail>(`/api/admin/audit/${eventId}`),
  auditRequestTrace: (requestId: string) => request<AuditRequestTrace>(`/api/admin/audit/request/${encodeURIComponent(requestId)}`),
  auditContent: (contentId: string) => request<AuditContentResponse>(`/api/admin/audit/content/${contentId}`),
  purgeRetention: () =>
    request<{ ok: boolean; content_blobs: number; redaction_tokens: number; bytes: number }>(
      '/api/admin/audit/retention/purge',
      { method: 'POST' }
    ),
  usageBreakdown: (days = 7) => request<UsageBreakdown>(`/api/admin/usage/breakdown?days=${days}`),
  capacity: () => request<CapacityReport>('/api/admin/capacity'),
  disputeAudit: (eventId: string, body: { disputeType: string; note?: string }) =>
    request<{ ok: boolean }>(`/api/admin/audit/${eventId}/dispute`, {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  adminUsage: () => request<{ from: string; data: UsageRow[] }>('/api/admin/usage/summary'),

  myProviders: () => request<MyProviderRow[]>('/api/me/providers'),
  createMyProvider: (body: CreateProviderBody) =>
    request<{ id: string; alias: string }>('/api/me/providers', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  updateMyProvider: (id: string, body: { displayName?: string; baseUrl?: string; enabled?: boolean }) =>
    request<ProviderRow>(`/api/me/providers/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  disableMyProvider: (id: string) =>
    request<{ ok: boolean }>(`/api/me/providers/${id}`, { method: 'DELETE' }),
  rotateMyProviderCredential: (id: string, apiKey: string) =>
    request<{ ok: boolean; key_hint: string }>(`/api/me/providers/${id}/credential`, {
      method: 'POST',
      body: JSON.stringify({ apiKey })
    }),
  testMyProvider: (id: string) =>
    request<{ healthy: boolean; health_status: string; tested_at: string; error?: string | null }>(
      `/api/me/providers/${id}/test`,
      { method: 'POST' }
    ),
  myKeys: () => request<MyKeyRow[]>('/api/me/keys'),
  createMyKey: () =>
    request<{ key: string; key_id: string }>('/api/me/keys', { method: 'POST' }),
  revokeMyKey: (id: string) => request<{ ok: boolean }>(`/api/me/keys/${id}/revoke`, { method: 'POST' }),
  rotateMyKey: (id: string) =>
    request<{ key: string; key_id: string; replaced_key_id: string }>(`/api/me/keys/${id}/rotate`, { method: 'POST' }),
  updateKeyConstraints: (id: string, body: KeyConstraintsBody) =>
    request<{ ok: boolean; expires_at?: string | null }>(`/api/admin/keys/${id}/constraints`, {
      method: 'PUT',
      body: JSON.stringify(body)
    }),
  updateMyKeyConstraints: (id: string, body: KeyConstraintsBody) =>
    request<{ ok: boolean; expires_at?: string | null }>(`/api/me/keys/${id}/constraints`, {
      method: 'PUT',
      body: JSON.stringify(body)
    }),
  entitlements: () => request<EntitlementRow[]>('/api/admin/entitlements'),
  upsertEntitlement: (body: UpsertEntitlementBody) =>
    request<{ id: string }>('/api/admin/entitlements', { method: 'POST', body: JSON.stringify(body) }),
  deleteEntitlement: (id: string) =>
    request<{ ok: boolean }>(`/api/admin/entitlements/${id}`, { method: 'DELETE' }),
  myUsage: () =>
    request<{ data: UsageRow[] }>('/api/me/usage'),

  models: () =>
    request<{ data: ModelRow[] }>('/v1/models', {
      headers: { Authorization: `Bearer ${localStorage.getItem('sh.devkey') ?? ''}` }
    }),

  policies: () => request<PolicyRow[]>('/api/admin/policies'),
  policySnapshot: () => request<PolicySnapshot>('/api/admin/policies/snapshot'),
  activatePolicy: (policyKey: string, version: number) =>
    request<{ policy_key: string; active_version: number; snapshot_reloaded: boolean }>(
      `/api/admin/policies/${encodeURIComponent(policyKey)}/activate?version=${version}`,
      { method: 'POST' }
    ),
  dryRunPolicy: (body: PublishPolicyBody) =>
    request<PolicyDryRunResult>('/api/admin/policies/dry-run', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  publishPolicy: (body: PublishPolicyBody) =>
    request<{ version: number; snapshot_reloaded: boolean }>('/api/admin/policies', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  budgets: () => request<BudgetRow[]>('/api/admin/budgets'),
  upsertBudget: (body: UpsertBudgetBody) =>
    request<{ id: string }>('/api/admin/budgets', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  catalog: (params?: { provider?: string; q?: string }) => {
    const search = new URLSearchParams();
    if (params?.provider) search.set('provider', params.provider);
    if (params?.q) search.set('q', params.q);
    const query = search.toString();
    return request<CatalogModelRow[]>(`/api/admin/catalog${query ? `?${query}` : ''}`);
  },
  catalogSync: () =>
    request<{ ok: boolean; providers: number; models: number; error?: string | null; synced_at?: string | null }>(
      '/api/admin/catalog/sync',
      { method: 'POST' }
    ),
  catalogProviders: (scope: 'admin' | 'me' = 'admin') =>
    request<CatalogProviderRow[]>(`/api/${scope}/catalog/providers`),
  catalogProviderModels: (providerKey: string, scope: 'admin' | 'me' = 'admin') =>
    request<CatalogProviderModel[]>(
      `/api/${scope}/catalog/providers/${encodeURIComponent(providerKey)}/models`
    ),
  createProviderFromCatalog: (body: CreateProviderFromCatalogBody) =>
    request<{ id: string; alias: string; models: number; scope: string }>(
      '/api/admin/providers/from-catalog',
      { method: 'POST', body: JSON.stringify(body) }
    ),
  createMyProviderFromCatalog: (body: CreateProviderFromCatalogBody) =>
    request<{ id: string; alias: string; models: number; scope: string }>(
      '/api/me/providers/from-catalog',
      { method: 'POST', body: JSON.stringify(body) }
    ),
  orgTeams: () => request<OrgTeamRow[]>('/api/admin/org/teams'),
  createTeam: (body: { name: string }) =>
    request<{ id: string; name: string; is_active: boolean }>('/api/admin/org/teams', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  updateTeam: (id: string, body: { name: string; isActive: boolean }) =>
    request<{ id: string; name: string; is_active: boolean }>(`/api/admin/org/teams/${id}`, {
      method: 'PUT',
      body: JSON.stringify(body)
    }),
  orgApps: () => request<OrgAppRow[]>('/api/admin/org/apps'),
  createApp: (body: { name: string; teamId?: string | null }) =>
    request<{ id: string; name: string; team_id?: string | null; is_active: boolean }>('/api/admin/org/apps', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  updateApp: (id: string, body: { name: string; teamId?: string | null; isActive: boolean }) =>
    request<{ id: string; name: string; team_id?: string | null; is_active: boolean }>(`/api/admin/org/apps/${id}`, {
      method: 'PUT',
      body: JSON.stringify(body)
    }),
  orgUsers: () => request<OrgUserRow[]>('/api/admin/org/users'),
  createUser: (body: { userName: string; password: string; teamId?: string | null; roles: number[] }) =>
    request<{ id: string }>('/api/admin/org/users', { method: 'POST', body: JSON.stringify(body) }),
  updateUser: (id: string, body: { teamId?: string | null; roles: number[]; isActive: boolean }) =>
    request<{ id: string }>(`/api/admin/org/users/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  resetUserPassword: (id: string, password: string) =>
    request<{ ok: boolean; sessions_revoked: boolean }>(`/api/admin/org/users/${id}/password`, {
      method: 'POST',
      body: JSON.stringify({ password })
    }),
  modelGroups: () => request<ModelGroupRow[]>('/api/admin/model-groups'),
  upsertModelGroup: (body: UpsertModelGroupBody) =>
    request<{ id: string; name: string }>('/api/admin/model-groups', {
      method: 'POST',
      body: JSON.stringify(body)
    }),

  assistantSessions: () =>
    request<{ sessions: AssistantSessionRow[] }>('/api/assistant/sessions'),
  assistantSession: (id: string) =>
    request<AssistantSessionDetail>(`/api/assistant/sessions/${id}`),
  deleteAssistantSession: (id: string) =>
    request<void>(`/api/assistant/sessions/${id}`, { method: 'DELETE' }),
  assistantTools: () =>
    request<{ tools: Array<{ name: string; readOnly: boolean }> }>('/api/assistant/tools'),

  /** 非流式问答（保留作为降级路径）。 */
  assistantChat: (body: AssistantChatBody) =>
    request<AssistantChatResult>('/api/assistant/chat', {
      method: 'POST',
      body: JSON.stringify(body)
    }),

  /** 流式问答：按 SSE 逐段回调，返回 abort 控制器。 */
  assistantStream: (
    body: AssistantChatBody,
    handlers: AssistantStreamHandlers
  ): AbortController => {
    const controller = new AbortController();

    void (async () => {
      try {
        const response = await fetch('/api/assistant/chat/stream', {
          method: 'POST',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body),
          signal: controller.signal
        });

        if (!response.ok || !response.body) {
          let message = response.statusText;
          try {
            const errBody = (await response.json()) as { error?: ApiErrorBody };
            message = errBody.error?.message ?? message;
          } catch {
            // 非 JSON 错误体，直接用品状态文本
          }
          handlers.onError?.(message);
          return;
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        for (;;) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });

          let boundary = buffer.indexOf('\n\n');
          while (boundary >= 0) {
            const frame = buffer.slice(0, boundary);
            buffer = buffer.slice(boundary + 2);
            dispatchFrame(frame, handlers);
            boundary = buffer.indexOf('\n\n');
          }
        }

        if (buffer.trim().length > 0) {
          dispatchFrame(buffer, handlers);
        }

        handlers.onDone?.();
      } catch (error) {
        if ((error as Error).name === 'AbortError') {
          handlers.onDone?.();
          return;
        }
        handlers.onError?.((error as Error).message);
      }
    })();

    return controller;
  }
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
  lastTestedAt?: string | null;
  lastTestError?: string | null;
}

export interface ProviderDetail extends ProviderRow {
  offerings: Array<{
    UpstreamModel: string;
    CanonicalModelId?: string | null;
    ContextWindow: number;
    MaxOutputTokens: number;
    supports_tools: boolean;
    supports_reasoning: boolean;
    input_price: number;
    output_price: number;
    Enabled: boolean;
  }>;
}

export interface KeyConstraintsBody {
  allowedModels?: string[];
  allowedProviders?: string[];
  allowedIpCidrs?: string[];
  rpm?: number | null;
  tpm?: number | null;
  maxConcurrency?: number | null;
  expiresAt?: string | null;
}

export interface EntitlementRow {
  id: string;
  subject_type: string;
  subjectId?: string | null;
  subject_label?: string | null;
  providerId: string;
  provider_alias?: string | null;
  models: string[];
  tokenQuota?: number | null;
  costQuota?: number | null;
  tokensUsed?: number | null;
  expiresAt?: string | null;
  createdAt: string;
}

export interface UpsertEntitlementBody {
  id?: string | null;
  subjectType: number;
  subjectId?: string | null;
  providerId: string;
  models?: string[];
  tokenQuota?: number | null;
  costQuota?: number | null;
  expiresAt?: string | null;
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
  constraints?: string | null;
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
  billing_owner?: string;
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

export interface PolicySnapshot {
  count: number;
  error?: string | null;
  policies: Array<{
    policy_key: string;
    version: number;
    priority: number;
    action: string;
    rollout: number;
    apps: string[];
    teams: string[];
    any: string[];
    all: string[];
  }>;
}

export interface PolicyDryRunResult {
  sampled_events: number;
  estimated_affected: number;
  confidence: string;
  note: string;
}

export interface CatalogModelRow {
  id: string;
  provider: string;
  model: string;
  family?: string | null;
  supportsTools: boolean;
  supportsReasoning: boolean;
  contextWindow: number;
  maxOutputTokens: number;
  input_price: number;
  output_price: number;
  syncedAt: string;
}

export interface ModelGroupMember {
  provider: string;
  providerId?: string;
  upstreamModel: string;
  priority: number;
  weight: number;
  state: string;
  enabled: boolean;
}

export interface ModelGroupRow {
  id: string;
  name: string;
  isAuto: boolean;
  allowCrossScope: boolean;
  strategy: string;
  members: ModelGroupMember[];
}

export interface UpsertModelGroupBody {
  name: string;
  allowCrossScope: boolean;
  strategy: string;
  members: Array<{
    providerId: string;
    upstreamModel: string;
    priority: number;
    weight: number;
    enabled: boolean;
  }>;
}

export interface AuditDisputeRow {
  id: string;
  eventId: string;
  dispute_type: string;
  note?: string | null;
  createdBy: string;
  createdAt: string;
  resolvedAt?: string | null;
}

export interface CatalogProviderRow {
  provider_key: string;
  name: string;
  api?: string | null;
  doc?: string | null;
  npm?: string | null;
  env: string[];
  model_count: number;
  syncedAt: string;
}

export interface CatalogProviderModel {
  modelId: string;
  family?: string | null;
  supports_tools: boolean;
  supports_reasoning: boolean;
  supports_structured_output: boolean;
  context_window: number;
  max_output_tokens: number;
  input_price: number;
  output_price: number;
}

export interface CreateProviderFromCatalogBody {
  alias: string;
  providerKey: string;
  apiKey: string;
  models: string[];
  baseUrl?: string;
}

export interface OrgTeamRow {
  id: string;
  name: string;
  is_active: boolean;
  createdAt: string;
  user_count: number;
  app_count: number;
}

export interface OrgAppRow {
  id: string;
  name: string;
  team_id?: string | null;
  team_name?: string | null;
  is_active: boolean;
  createdAt: string;
  key_count: number;
}

export interface OrgUserRow {
  id: string;
  userName: string;
  teamId?: string | null;
  team_name?: string | null;
  roles: number[];
  is_active: boolean;
  createdAt: string;
  totp_enabled: boolean;
}

export interface AuditContentRef {
  content_id: string;
  direction: string;
  variant: string;
  size_bytes: number;
  truncated: boolean;
  created_at: string;
  expires_at: string;
  expired?: boolean;
}

export interface AuditEventDetail {
  event: AuditRow & { teamId?: string | null; detail?: string | null; policy_id?: string | null; policy_version?: number | null; upstreamModel?: string | null; providerId?: string | null };
  disputes: AuditDisputeRow[];
  content: AuditContentRef[];
}

export interface AuditRequestTrace {
  request_id: string;
  events: AuditRow[];
  content: AuditContentRef[];
}

export interface AuditContentResponse {
  content_id: string;
  request_id: string;
  direction: string;
  variant: string;
  text: string;
  truncated: boolean;
  size_bytes: number;
  created_at: string;
  expires_at: string;
}

export interface UsageBreakdownRow {
  requests: number;
  input_tokens: number;
  output_tokens: number;
  cost_usd: number;
  name?: string;
  date?: string;
  model?: string;
  compression_saved_tokens?: number;
}

export interface UsageBreakdown {
  from: string;
  days: number;
  totals: {
    requests: number;
    input_tokens: number;
    input_tokens_original: number;
    output_tokens: number;
    cost_usd: number;
    compression_saved_tokens: number;
    org_requests: number;
    user_requests: number;
  };
  by_day: UsageBreakdownRow[];
  by_team: UsageBreakdownRow[];
  by_app: UsageBreakdownRow[];
  by_provider: UsageBreakdownRow[];
  by_model: UsageBreakdownRow[];
}

export interface CapacityReport {
  retention: {
    raw_days: number;
    redacted_days: number;
    compressed_days: number;
    max_content_bytes: number;
    persist_content: boolean;
  };
  content: {
    blobs: number;
    bytes: number;
    expired_blobs: number;
    expired_bytes: number;
    by_variant: Array<{ variant: string; blobs: number; bytes: number }>;
    by_direction: Array<{ direction: string; blobs: number; bytes: number }>;
  };
  keys: { total: number; active: number; revoked: number; expired: number };
  redaction_tokens: { total: number; expired: number };
}

export interface PolicyRow {
  policy_key: string;
  enabled: boolean;
  active_version: number | null;
  versions: Array<{
    version: number;
    priority: number;
    whenJson: string;
    thenJson: string;
    scopeJson?: string;
    rolloutJson?: string;
    changeReason?: string;
    onError: string;
    createdAt: string;
  }>;
}

export interface PublishPolicyBody {
  policyKey: string;
  priority: number;
  scope?: { teams?: string[]; apps?: string[]; models?: string[] };
  when?: { any?: Array<{ detector: string; minMatches: number }> };
  then?: { action: string; reason: string; ruleId?: string };
  onError?: string;
  rolloutPercent?: number;
  changeReason?: string;
}

export interface BudgetRow {
  id: string;
  subject: string;
  subjectId?: string | null;
  period: string;
  tokenLimit?: number | null;
  costLimit?: number | null;
  tokensUsed: number;
  costUsed: number;
  enabled: boolean;
}

export interface UpsertBudgetBody {
  subject: number;
  subjectId?: string | null;
  period: string;
  tokenLimit?: number | null;
  costLimit?: number | null;
  enabled: boolean;
}

export interface AssistantChatBody {
  question: string;
  model?: string;
  sessionId?: string;
}

export interface AssistantChatResult {
  sessionId?: string | null;
  answer: string;
  evidence: string[];
  toolCalls: number;
  model: string;
  toolNames?: string | null;
}

export interface AssistantSessionRow {
  id: string;
  title: string;
  model?: string | null;
  messageCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface AssistantMessageRow {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  toolNames?: string | null;
  evidence: string[];
  toolCallCount: number;
  model?: string | null;
  createdAt: string;
}

export interface AssistantSessionDetail extends AssistantSessionRow {
  messages: AssistantMessageRow[];
}

/** `done` 事件：工具轮数、证据、实际模型。字段为 camelCase（后端用 Web 选项序列化）。 */
export interface AssistantDoneEvent {
  kind: string;
  toolCalls: number;
  evidence?: string[] | null;
  model?: string | null;
  toolNames?: string | null;
}

/** 流末补发的会话信息（snake_case，便于前端直接挂到下一次请求）。 */
export interface AssistantSessionEvent {
  session_id?: string | null;
  tool_calls: number;
  evidence?: string[] | null;
  model?: string | null;
}

export interface AssistantStreamHandlers {
  onDelta: (text: string) => void;
  onStats?: (event: AssistantDoneEvent) => void;
  onSession?: (event: AssistantSessionEvent) => void;
  onError?: (message: string) => void;
  onDone?: () => void;
}
