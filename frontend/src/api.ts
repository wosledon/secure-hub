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
    }),

  policies: () => request<PolicyRow[]>('/api/admin/policies'),
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
  catalogSync: () =>
    request<{ ok: boolean; providers: number; models: number; error?: string | null }>(
      '/api/admin/catalog/sync',
      { method: 'POST' }
    ),

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

export interface PolicyRow {
  policy_key: string;
  enabled: boolean;
  active_version: number | null;
  versions: Array<{
    version: number;
    priority: number;
    whenJson: string;
    thenJson: string;
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
