import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  IconButton,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import EditIcon from '@mui/icons-material/EditOutlined';
import SearchIcon from '@mui/icons-material/Search';
import { useTranslation } from 'react-i18next';
import { api, type CustomRuleRow, type UpsertCustomRuleBody } from '../api';
import { ConfirmDialog, EmptyState, ErrorState, TableSkeleton } from './ui';

type Severity = UpsertCustomRuleBody['severity'];
type Direction = UpsertCustomRuleBody['direction'];
type MatchType = UpsertCustomRuleBody['matchType'];

/** 后端按数字枚举序列化（Info=0/Warning=1/Critical=2，Inbound=0/Outbound=1），请求侧用小写字符串。 */
const toSeverity = (value: number | string): Severity => {
  if (typeof value === 'string') {
    const normalized = value.toLowerCase();
    if (normalized === 'critical' || normalized === 'warning' || normalized === 'info') return normalized;
    return value === '2' ? 'critical' : value === '1' ? 'warning' : 'info';
  }
  return value === 2 ? 'critical' : value === 1 ? 'warning' : 'info';
};

const toDirection = (value: number | string): Direction => {
  if (typeof value === 'string') {
    return value.toLowerCase() === 'outbound' ? 'outbound' : 'inbound';
  }
  return value === 1 ? 'outbound' : 'inbound';
};

const severityTone = (severity: Severity): 'error' | 'warning' | 'info' =>
  severity === 'critical' ? 'error' : severity === 'warning' ? 'warning' : 'info';

/** 与后端 CustomRuleValidation.KeyPattern 保持一致。 */
const KEY_PATTERN = /^[a-z][a-z0-9_]{1,63}$/;
const MAX_PATTERN_LENGTH = 500;

interface DraftState {
  key: string;
  pattern: string;
  matchType: MatchType;
  ignoreCase: boolean;
  severity: Severity;
  direction: Direction;
  enabled: boolean;
  sample: string;
  description: string;
}

const emptyDraft: DraftState = {
  key: '',
  pattern: '',
  matchType: 'keyword',
  ignoreCase: true,
  severity: 'warning',
  direction: 'inbound',
  enabled: true,
  sample: '',
  description: ''
};

function draftFrom(rule: CustomRuleRow): DraftState {
  return {
    key: rule.key,
    pattern: rule.pattern,
    matchType: rule.matchType === 'regex' ? 'regex' : 'keyword',
    ignoreCase: rule.ignoreCase,
    severity: toSeverity(rule.severity),
    direction: toDirection(rule.direction),
    enabled: rule.enabled,
    sample: rule.sample ?? '',
    description: rule.description ?? ''
  };
}

export function CustomRulesCard({ onRulesChanged }: { onRulesChanged?: (rules: CustomRuleRow[]) => void }) {
  const { t } = useTranslation();
  const [rules, setRules] = useState<CustomRuleRow[]>([]);
  const [limits, setLimits] = useState({ maxRules: 100, maxPatternLength: MAX_PATTERN_LENGTH });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');

  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<CustomRuleRow | null>(null);
  const [draft, setDraft] = useState<DraftState>(emptyDraft);
  const [saving, setSaving] = useState(false);
  const [fieldError, setFieldError] = useState<string | null>(null);

  const [pendingDelete, setPendingDelete] = useState<CustomRuleRow | null>(null);
  const [deleting, setDeleting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const payload = await api.customRules();
      setRules(payload.rules ?? []);
      setLimits(payload.limits ?? { maxRules: 100, maxPatternLength: MAX_PATTERN_LENGTH });
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const applyRules = useCallback(
    (next: CustomRuleRow[]) => {
      setRules(next);
      onRulesChanged?.(next);
    },
    [onRulesChanged]
  );

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return rules;
    return rules.filter((rule) =>
      [rule.key, rule.pattern, rule.description ?? '', rule.sample ?? ''].some((value) => value.toLowerCase().includes(query))
    );
  }, [rules, search]);

  const openCreate = () => {
    setEditing(null);
    setDraft(emptyDraft);
    setFieldError(null);
    setDialogOpen(true);
  };

  const openEdit = (rule: CustomRuleRow) => {
    setEditing(rule);
    setDraft(draftFrom(rule));
    setFieldError(null);
    setDialogOpen(true);
  };

  const validate = (): string | null => {
    const key = draft.key.trim();
    if (!KEY_PATTERN.test(key)) return t('policies.customRules.invalidKey');
    if (!draft.pattern.trim()) return t('policies.customRules.patternRequired');
    if (draft.pattern.length > limits.maxPatternLength) {
      return t('policies.customRules.patternRequired');
    }
    if (draft.matchType === 'regex') {
      try {
        new RegExp(draft.pattern);
      } catch (cause) {
        return t('policies.customRules.regexInvalid', {
          error: cause instanceof Error ? cause.message : String(cause)
        });
      }
    }
    return null;
  };

  const save = async () => {
    const problem = validate();
    if (problem) {
      setFieldError(problem);
      return;
    }
    setFieldError(null);
    setSaving(true);
    try {
      const body = {
        pattern: draft.pattern,
        matchType: draft.matchType,
        ignoreCase: draft.ignoreCase,
        severity: draft.severity,
        direction: draft.direction,
        enabled: draft.enabled,
        sample: draft.sample.trim() || undefined,
        description: draft.description.trim() || undefined
      };
      const key = draft.key.trim();
      const result =
        editing && editing.key === key
          ? await api.updateCustomRule(key, body)
          : await api.createCustomRule({ ...body, key });
      applyRules(result.rules ?? []);
      setDialogOpen(false);
    } catch (cause) {
      setFieldError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    if (!pendingDelete) return;
    setDeleting(true);
    try {
      const result = await api.deleteCustomRule(pendingDelete.key);
      applyRules(result.rules ?? []);
      setPendingDelete(null);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
      setPendingDelete(null);
    } finally {
      setDeleting(false);
    }
  };

  const toggle = async (rule: CustomRuleRow, enabled: boolean) => {
    try {
      const result = await api.updateCustomRule(rule.key, {
        pattern: rule.pattern,
        matchType: rule.matchType === 'regex' ? 'regex' : 'keyword',
        ignoreCase: rule.ignoreCase,
        severity: toSeverity(rule.severity),
        direction: toDirection(rule.direction),
        enabled,
        sample: rule.sample ?? undefined,
        description: rule.description ?? undefined
      });
      applyRules(result.rules ?? []);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  };

  const enabledCount = rules.filter((rule) => rule.enabled).length;
  const full = !editing && rules.length >= limits.maxRules;

  return (
    <Card>
      <CardContent>
        <Stack spacing={2}>
          <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" spacing={1.5} alignItems={{ xs: 'stretch', sm: 'flex-start' }}>
            <Box minWidth={0}>
              <Typography variant="h2">{t('policies.customRules.section')}</Typography>
              <Typography variant="body2" color="text.secondary" mt={0.5}>
                {t('policies.customRules.sectionHint')}
              </Typography>
            </Box>
            <Button variant="contained" startIcon={<AddIcon />} onClick={openCreate} disabled={full} sx={{ flexShrink: 0 }}>
              {t('policies.customRules.create')}
            </Button>
          </Stack>

          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
            <TextField
              size="small"
              placeholder={t('policies.customRules.search')}
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              InputProps={{ startAdornment: <SearchIcon fontSize="small" sx={{ mr: 1, color: 'text.disabled' }} /> }}
              sx={{ minWidth: 240 }}
            />
            <Chip size="small" variant="outlined" label={t('policies.customRules.enabledRule', { count: enabledCount })} />
            <Chip size="small" variant="outlined" label={t('policies.customRules.totalRule', { count: rules.length })} />
            <Chip size="small" variant="outlined" label={t('policies.customRules.limitRule', { max: limits.maxRules })} />
          </Stack>

          {error && <ErrorState message={error} retry={() => void load()} />}
          {loading && <TableSkeleton rows={3} columns={4} />}

          {!loading && !error && rules.length === 0 && (
            <EmptyState
              title={t('policies.customRules.empty')}
              description={t('policies.customRules.emptyHint')}
              action={
                <Button variant="outlined" startIcon={<AddIcon />} onClick={openCreate}>
                  {t('policies.customRules.create')}
                </Button>
              }
            />
          )}

          {!loading && !error && rules.length > 0 && filtered.length === 0 && (
            <EmptyState compact title={t('policies.customRules.noMatch')} />
          )}

          {!loading &&
            !error &&
            filtered.map((rule) => {
              const severity = toSeverity(rule.severity);
              const direction = toDirection(rule.direction);
              return (
                <Stack
                  key={rule.key}
                  direction={{ xs: 'column', sm: 'row' }}
                  spacing={1.5}
                  alignItems={{ xs: 'stretch', sm: 'center' }}
                  sx={{
                    p: 1.5,
                    borderRadius: 2,
                    border: '1px solid',
                    borderColor: rule.enabled ? 'divider' : 'action.disabled'
                  }}
                >
                  <Stack direction="row" spacing={1} alignItems="center" minWidth={0} flexWrap="wrap" useFlexGap>
                    <Switch
                      size="small"
                      checked={rule.enabled}
                      onChange={(event) => void toggle(rule, event.target.checked)}
                      inputProps={{ 'aria-label': t('policies.customRules.toggleLabel', { key: rule.key }) }}
                    />
                    <Typography variant="subtitle2" sx={{ fontFamily: 'monospace' }}>
                      {rule.key}
                    </Typography>
                    <Chip
                      size="small"
                      color={severityTone(severity)}
                      variant={severity === 'critical' ? 'filled' : 'outlined'}
                      label={t(`policies.customRules.severityLabel.${severity}`)}
                    />
                    <Chip size="small" variant="outlined" label={t(`policies.customRules.${rule.matchType === 'regex' ? 'regex' : 'keyword'}`)} />
                    <Chip size="small" variant="outlined" label={t(`policies.customRules.${direction === 'outbound' ? 'directionOutbound' : 'directionInbound'}`)} />
                  </Stack>

                  <Box minWidth={0} flexGrow={1}>
                    <Typography
                      variant="body2"
                      sx={{ fontFamily: 'monospace', wordBreak: 'break-all' }}
                      color={rule.enabled ? 'text.primary' : 'text.disabled'}
                    >
                      {rule.pattern}
                    </Typography>
                    {(rule.description || rule.sample) && (
                      <Typography variant="caption" color="text.secondary" display="block">
                        {[rule.description, rule.sample].filter(Boolean).join(' · ')}
                      </Typography>
                    )}
                  </Box>

                  <Stack direction="row" spacing={0.5} alignItems="center" flexShrink={0} justifyContent={{ xs: 'flex-end', sm: 'flex-start' }}>
                    <Tooltip title={t('policies.customRules.edit')}>
                      <IconButton size="small" onClick={() => openEdit(rule)} aria-label={t('policies.customRules.edit')}>
                        <EditIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                    <Tooltip title={t('policies.customRules.delete')}>
                      <IconButton size="small" color="error" onClick={() => setPendingDelete(rule)} aria-label={t('policies.customRules.delete')}>
                        <DeleteOutlineIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  </Stack>
                </Stack>
              );
            })}

          <Divider />
          <Typography variant="caption" color="text.secondary">
            {t('policies.customRules.footnote', { key: 'custom.*' })}
          </Typography>
        </Stack>
      </CardContent>

      <Dialog open={dialogOpen} onClose={() => setDialogOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{editing ? t('policies.customRules.editTitle', { key: editing.key }) : t('policies.customRules.createTitle')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} mt={0.5}>
            {fieldError && <Alert severity="error">{fieldError}</Alert>}

            <TextField
              label={t('policies.customRules.key')}
              helperText={t('policies.customRules.keyHint')}
              value={draft.key}
              onChange={(event) => setDraft({ ...draft, key: event.target.value })}
              disabled={Boolean(editing)}
              fullWidth
              required
              autoFocus={!editing}
              inputProps={{ spellCheck: false }}
            />

            <TextField
              label={t('policies.customRules.pattern')}
              helperText={t('policies.customRules.patternHint')}
              value={draft.pattern}
              onChange={(event) => setDraft({ ...draft, pattern: event.target.value })}
              fullWidth
              required
              multiline
              minRows={2}
              inputProps={{ maxLength: limits.maxPatternLength, spellCheck: false }}
              sx={{ '& textarea': { fontFamily: 'monospace', fontSize: 13 } }}
            />

            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField
                select
                label={t('policies.customRules.matchType')}
                value={draft.matchType}
                onChange={(event) => setDraft({ ...draft, matchType: event.target.value as MatchType })}
                fullWidth
              >
                <MenuItem value="keyword">{t('policies.customRules.keyword')}</MenuItem>
                <MenuItem value="regex">{t('policies.customRules.regex')}</MenuItem>
              </TextField>
              <TextField
                select
                label={t('policies.customRules.severity')}
                value={draft.severity}
                onChange={(event) => setDraft({ ...draft, severity: event.target.value as Severity })}
                fullWidth
              >
                <MenuItem value="critical">{t('policies.customRules.severityLabel.critical')}</MenuItem>
                <MenuItem value="warning">{t('policies.customRules.severityLabel.warning')}</MenuItem>
                <MenuItem value="info">{t('policies.customRules.severityLabel.info')}</MenuItem>
              </TextField>
            </Stack>

            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField
                select
                label={t('policies.customRules.direction')}
                value={draft.direction}
                onChange={(event) => setDraft({ ...draft, direction: event.target.value as Direction })}
                fullWidth
              >
                <MenuItem value="inbound">{t('policies.customRules.directionInbound')}</MenuItem>
                <MenuItem value="outbound">{t('policies.customRules.directionOutbound')}</MenuItem>
              </TextField>
              <Stack direction="row" alignItems="center" spacing={1} justifyContent="space-between" sx={{ minWidth: 0 }}>
                <Box minWidth={0}>
                  <Typography variant="body2">{t('policies.customRules.ignoreCase')}</Typography>
                  <Typography variant="caption" color="text.secondary">{t('policies.customRules.ignoreCaseHint')}</Typography>
                </Box>
                <Switch
                  checked={draft.ignoreCase}
                  onChange={(event) => setDraft({ ...draft, ignoreCase: event.target.checked })}
                  inputProps={{ 'aria-label': t('policies.customRules.ignoreCase') }}
                />
              </Stack>
            </Stack>

            <TextField
              label={t('policies.customRules.description')}
              helperText={t('policies.customRules.descriptionHint')}
              value={draft.description}
              onChange={(event) => setDraft({ ...draft, description: event.target.value })}
              fullWidth
              inputProps={{ maxLength: 500 }}
            />

            <TextField
              label={t('policies.customRules.sample')}
              helperText={t('policies.customRules.sampleHint')}
              value={draft.sample}
              onChange={(event) => setDraft({ ...draft, sample: event.target.value })}
              fullWidth
              inputProps={{ maxLength: 500, spellCheck: false }}
            />

            <Stack direction="row" alignItems="center" spacing={1} justifyContent="space-between">
              <Box minWidth={0}>
                <Typography variant="body2">{t('policies.customRules.enabled')}</Typography>
                <Typography variant="caption" color="text.secondary">{t('policies.customRules.enabledHint')}</Typography>
              </Box>
              <Switch
                checked={draft.enabled}
                onChange={(event) => setDraft({ ...draft, enabled: event.target.checked })}
                inputProps={{ 'aria-label': t('policies.customRules.enabled') }}
              />
            </Stack>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDialogOpen(false)} disabled={saving}>{t('common.cancel')}</Button>
          <Button variant="contained" onClick={() => void save()} disabled={saving}>
            {saving ? t('common.working') : t('common.save')}
          </Button>
        </DialogActions>
      </Dialog>

      <ConfirmDialog
        open={Boolean(pendingDelete)}
        title={t('policies.customRules.deleteTitle', { key: pendingDelete?.key ?? '' })}
        description={t('policies.customRules.deleteHint')}
        confirmLabel={t('common.delete')}
        danger
        busy={deleting}
        onClose={() => setPendingDelete(null)}
        onConfirm={() => void remove()}
      />
    </Card>
  );
}