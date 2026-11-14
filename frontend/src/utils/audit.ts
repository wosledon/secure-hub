import { useTranslation } from 'react-i18next';

export const auditEventTypes = [
  'auth_succeeded', 'auth_failed', 'key_created', 'key_rotated', 'key_revoked', 'access_denied',
  'guardrail_blocked', 'guardrail_redacted', 'guardrail_alert', 'guardrail_error',
  'policy_created', 'policy_updated', 'policy_activated', 'route_failover', 'route_selected',
  'route_denied', 'quota_exceeded', 'compression_applied', 'compression_rolled_back',
  'content_viewed', 'content_write_failed', 'data_exported', 'provider_created',
  'provider_credential_updated', 'catalog_synced', 'byok_enabled', 'assistant_tool_invoked',
  'assistant_session_started', 'assistant_answer_returned', 'content_restored', 'security_baseline_failed',
  'user_created', 'user_updated', 'user_deactivated', 'user_password_reset',
  'team_created', 'team_updated', 'app_created', 'app_updated',
  'content_purged', 'rate_limited', 'provider_updated', 'provider_disabled', 'provider_tested',
  'provider_credential_rotated', 'entitlement_updated', 'entitlement_revoked', 'key_constraints_updated'
];

export function useAuditLabels() {
  const { t } = useTranslation();
  const translate = (group: string, code?: string | null) =>
    code ? t(`${group}.${code}`, { defaultValue: code }) : '—';

  return {
    event: (code?: string | null) => translate('auditEvent', code),
    reason: (code?: string | null) => translate('auditReason', code),
    action: (code?: string | null) => translate('auditValue.action', code),
    severity: (code?: string | null) => translate('auditValue.severity', code),
    actorType: (code?: string | null) => translate('auditValue.actorType', code),
    billing: (code?: string | null) => translate('auditValue.billing', code),
    disputeType: (code?: string | null) => translate('auditValue.disputeType', code)
  };
}
