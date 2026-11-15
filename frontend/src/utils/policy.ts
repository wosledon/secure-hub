import type { TFunction } from 'i18next';
import type {
  PolicyDetectorInfo,
  PolicyDetectorRequirement,
  PolicyRow,
  PolicyVersionRow,
  PublishPolicyBody
} from '../api';

export type PolicyAction = 'block' | 'redact' | 'alert';
export type PolicyMatchMode = 'any' | 'all';

export interface PolicyDraft {
  policyKey: string;
  priority: number;
  mode: PolicyMatchMode;
  requirements: PolicyDetectorRequirement[];
  teams: string[];
  apps: string[];
  models: string[];
  action: PolicyAction;
  rollout: number;
  reason: string;
  changeReason: string;
  observation: boolean;
}

export const policyActions: PolicyAction[] = ['block', 'redact', 'alert'];

export const fallbackDetectors: PolicyDetectorInfo[] = [
  { id: 'pii.cn_idcard', category: 'pii', severity: 'critical', sample: '11010519491231002X' },
  { id: 'pii.cn_mobile', category: 'pii', severity: 'warning', sample: '13800138000' },
  { id: 'pii.bank_card', category: 'pii', severity: 'critical', sample: '4111111111111111' },
  { id: 'pii.email', category: 'pii', severity: 'info', sample: 'alice@example.com' },
  { id: 'secret.private_key', category: 'secret', severity: 'critical', sample: '-----BEGIN RSA PRIVATE KEY-----' },
  {
    id: 'secret.jwt',
    category: 'secret',
    severity: 'warning',
    sample: 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U'
  },
  { id: 'secret.generic', category: 'secret', severity: 'critical', sample: 'sk-live-9fK2mQ7vT1xR4pL8sN3wZ6bC' }
];

const detectorKey = (id: string) => id.replace(/\./g, '_');

export function detectorLabel(id: string, t: TFunction): string {
  // 自定义规则没有本地化文案，展示规则 key（去掉 custom. 前缀）。
  if (id.startsWith('custom.')) return id.slice('custom.'.length);
  return t(`policies.detector.${detectorKey(id)}.label`, { defaultValue: id });
}

export function detectorDescription(id: string, t: TFunction): string {
  return t(`policies.detector.${detectorKey(id)}.desc`, { defaultValue: '' });
}

export function detectorSample(id: string, detectors: PolicyDetectorInfo[]): string {
  return (detectors.find((detector) => detector.id === id) ?? fallbackDetectors.find((detector) => detector.id === id))?.sample ?? '';
}

export function blankDraft(): PolicyDraft {
  return {
    policyKey: '',
    priority: 100,
    mode: 'any',
    requirements: [],
    teams: [],
    apps: [],
    models: [],
    action: 'block',
    rollout: 100,
    reason: 'policy_matched',
    changeReason: '',
    observation: false
  };
}

export function activeVersion(row: PolicyRow): PolicyVersionRow | undefined {
  return row.versions.find((version) => version.version === row.active_version) ?? row.versions[0];
}

export function draftFromVersion(row: PolicyRow, version: PolicyVersionRow): PolicyDraft {
  const requirements = version.when.all.length > 0 ? version.when.all : version.when.any;
  const action = policyActions.includes(version.then.action as PolicyAction)
    ? (version.then.action as PolicyAction)
    : 'block';

  return {
    policyKey: row.policy_key,
    priority: version.priority,
    mode: version.when.all.length > 0 ? 'all' : 'any',
    requirements: requirements.map((requirement) => ({
      detector: requirement.detector,
      min_matches: Math.max(1, requirement.min_matches)
    })),
    teams: version.scope.teams ?? [],
    apps: version.scope.apps ?? [],
    models: version.scope.models ?? [],
    action,
    rollout: version.rollout_percent ?? 100,
    reason: version.then.reason || 'policy_matched',
    changeReason: '',
    observation: false
  };
}

export function toPublishBody(draft: PolicyDraft): PublishPolicyBody {
  const requirements = draft.requirements.map((requirement) => ({
    detector: requirement.detector,
    minMatches: Math.max(1, requirement.min_matches)
  }));

  return {
    policyKey: draft.policyKey.trim(),
    priority: draft.priority,
    scope: { teams: draft.teams, apps: draft.apps, models: draft.models },
    when: draft.mode === 'all' ? { all: requirements } : { any: requirements },
    then: {
      action: draft.observation ? 'alert' : draft.action,
      reason: draft.reason.trim() || 'policy_matched',
      ruleId: draft.requirements[0]?.detector
    },
    onError: 'block',
    rolloutPercent: draft.rollout,
    changeReason: draft.changeReason.trim()
  };
}

export function actionLabel(action: string, t: TFunction): string {
  return t(`policies.actionLabel.${action}`, { defaultValue: action });
}

export function summarizePolicy(row: PolicyRow, t: TFunction): string {
  const version = activeVersion(row);
  if (!version) return t('policies.noVersions');

  const requirements = version.when.all.length > 0 ? version.when.all : version.when.any;
  const action = actionLabel(version.then.action, t);

  if (requirements.length === 0) {
    return t('policies.summaryScopeOnly', { scope: scopeSummary(version.scope, t), action });
  }

  const list = requirements.map((requirement) => detectorLabel(requirement.detector, t)).join(t('policies.listSeparator'));
  return version.when.all.length > 0
    ? t('policies.summaryAll', { detectors: list, action })
    : t('policies.summaryAny', { detectors: list, action });
}

export function scopeSummary(
  scope: { teams: string[]; apps: string[]; models: string[] },
  t: TFunction
): string {
  const parts: string[] = [];
  if (scope.teams.length > 0) parts.push(t('policies.scopeTeams', { list: scope.teams.join(', ') }));
  if (scope.apps.length > 0) parts.push(t('policies.scopeApps', { list: scope.apps.join(', ') }));
  if (scope.models.length > 0) parts.push(t('policies.scopeModels', { list: scope.models.join(', ') }));
  return parts.length > 0 ? parts.join(' · ') : t('policies.scopeAll');
}

export interface PolicyTemplate {
  id: string;
  icon: 'shield' | 'key' | 'mobile' | 'mail';
  draft: PolicyDraft;
}

export const policyTemplates: PolicyTemplate[] = [
  {
    id: 'piiBlock',
    icon: 'shield',
    draft: {
      ...blankDraft(),
      policyKey: 'pii-block',
      priority: 200,
      requirements: [
        { detector: 'pii.cn_idcard', min_matches: 1 },
        { detector: 'pii.bank_card', min_matches: 1 }
      ],
      action: 'block',
      reason: 'pii_detected'
    }
  },
  {
    id: 'secretBlock',
    icon: 'key',
    draft: {
      ...blankDraft(),
      policyKey: 'secret-block',
      priority: 300,
      requirements: [
        { detector: 'secret.private_key', min_matches: 1 },
        { detector: 'secret.generic', min_matches: 1 }
      ],
      action: 'block',
      reason: 'secret_detected'
    }
  },
  {
    id: 'mobileRedact',
    icon: 'mobile',
    draft: {
      ...blankDraft(),
      policyKey: 'pii-redact',
      priority: 100,
      requirements: [{ detector: 'pii.cn_mobile', min_matches: 1 }],
      action: 'redact',
      reason: 'pii_redacted'
    }
  },
  {
    id: 'emailAlert',
    icon: 'mail',
    draft: {
      ...blankDraft(),
      policyKey: 'email-alert',
      priority: 50,
      requirements: [{ detector: 'pii.email', min_matches: 1 }],
      action: 'alert',
      reason: 'pii_alert'
    }
  }
];

export function versionAction(version: PolicyVersionRow): string {
  return version.then.action || 'block';
}
