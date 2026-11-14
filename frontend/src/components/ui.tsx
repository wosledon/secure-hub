import { useState, type ReactNode } from 'react';
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
  IconButton,
  Skeleton,
  Stack,
  Tooltip,
  Typography
} from '@mui/material';
import { alpha } from '@mui/material/styles';
import CheckIcon from '@mui/icons-material/Check';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import InboxIcon from '@mui/icons-material/Inbox';
import { useTranslation } from 'react-i18next';

export function PageHeader({
  eyebrow,
  title,
  description,
  actions
}: {
  eyebrow?: string;
  title: string;
  description?: string;
  actions?: ReactNode;
}) {
  return (
    <Stack
      component="header"
      direction={{ xs: 'column', sm: 'row' }}
      spacing={2}
      alignItems={{ xs: 'stretch', sm: 'flex-start' }}
      justifyContent="space-between"
    >
      <Box minWidth={0}>
        {eyebrow && (
          <Typography variant="overline" color="primary.main" display="block" mb={0.5}>
            {eyebrow}
          </Typography>
        )}
        <Typography variant="h1" component="h1">
          {title}
        </Typography>
        {description && (
          <Typography variant="body1" color="text.secondary" mt={0.75} maxWidth={760}>
            {description}
          </Typography>
        )}
      </Box>
      {actions && (
        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
          {actions}
        </Stack>
      )}
    </Stack>
  );
}

export function StatCard({
  label,
  value,
  caption,
  icon,
  tone = 'primary'
}: {
  label: string;
  value: string | number;
  caption?: string;
  icon: ReactNode;
  tone?: 'primary' | 'success' | 'warning' | 'error' | 'secondary';
}) {
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={2}>
          <Box minWidth={0}>
            <Typography variant="body2" color="text.secondary" fontWeight={500}>
              {label}
            </Typography>
            <Typography variant="h4" mt={1.25} sx={{ fontVariantNumeric: 'tabular-nums' }}>
              {value}
            </Typography>
            {caption && (
              <Typography variant="caption" color="text.secondary" display="block" mt={1}>
                {caption}
              </Typography>
            )}
          </Box>
          <Box
            sx={(theme) => ({
              width: 38,
              height: 38,
              borderRadius: 10,
              display: 'grid',
              placeItems: 'center',
              flexShrink: 0,
              color: `${tone}.main`,
              bgcolor: alpha(theme.palette[tone].main, theme.palette.mode === 'dark' ? 0.16 : 0.09)
            })}
          >
            {icon}
          </Box>
        </Stack>
      </CardContent>
    </Card>
  );
}

const statusColor = (status: string): 'success' | 'error' | 'warning' | 'info' | 'default' => {
  const normalized = status.toLowerCase();
  if (['active', 'healthy', 'allow', 'enabled', 'resolved', 'ok', 'org'].includes(normalized)) return 'success';
  if (['block', 'blocked', 'revoked', 'error', 'unhealthy', 'disabled'].includes(normalized)) return 'error';
  if (['warning', 'dry-run', 'pending', 'unknown', 'user'].includes(normalized)) return 'warning';
  if (['redact', 'alert', 'personal', 'auto'].includes(normalized)) return 'info';
  return 'default';
};

export function StatusChip({ status, label }: { status: string; label?: string }) {
  return <Chip size="small" color={statusColor(status)} variant="outlined" label={label ?? status} />;
}

export function CopyButton({
  text,
  label,
  size = 'small',
  iconOnly = false
}: {
  text: string;
  label?: string;
  size?: 'small' | 'medium';
  iconOnly?: boolean;
}) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    await navigator.clipboard.writeText(text);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1600);
  };

  const content = copied ? <CheckIcon fontSize="small" /> : <ContentCopyIcon fontSize="small" />;
  return (
    <>
      {iconOnly ? (
        <Tooltip title={copied ? t('common.copied') : (label ?? t('common.copy'))}>
          <IconButton size={size} onClick={() => void copy()} aria-label={label ?? t('common.copy')}>
            {content}
          </IconButton>
        </Tooltip>
      ) : (
        <Button size={size} startIcon={content} onClick={() => void copy()} color={copied ? 'success' : 'inherit'}>
          {copied ? t('common.copied') : (label ?? t('common.copy'))}
        </Button>
      )}
      <Box
        component="span"
        aria-live="polite"
        sx={{
          position: 'absolute',
          width: '1px',
          height: '1px',
          p: 0,
          m: '-1px',
          overflow: 'hidden',
          clipPath: 'inset(50%)',
          whiteSpace: 'nowrap',
          border: 0
        }}
      >
        {copied ? t('common.copied') : ''}
      </Box>
    </>
  );
}

export function EmptyState({
  title,
  description,
  action,
  compact = false
}: {
  title: string;
  description?: string;
  action?: ReactNode;
  compact?: boolean;
}) {
  return (
    <Stack alignItems="center" textAlign="center" spacing={1.25} py={compact ? 3 : 6} px={2}>
      <Box sx={{ color: 'text.disabled', bgcolor: 'action.hover', p: 1.25, borderRadius: 3, display: 'grid' }}>
        <InboxIcon />
      </Box>
      <Typography variant="subtitle1">{title}</Typography>
      {description && <Typography variant="body2" color="text.secondary" maxWidth={420}>{description}</Typography>}
      {action}
    </Stack>
  );
}

export function ErrorState({ message, retry }: { message: string; retry?: () => void }) {
  const { t } = useTranslation();
  return (
    <Alert severity="error" icon={<ErrorOutlineIcon />} action={retry && <Button color="inherit" size="small" onClick={retry}>{t('common.retry')}</Button>}>
      {message}
    </Alert>
  );
}

export function TableSkeleton({ rows = 5, columns = 5 }: { rows?: number; columns?: number }) {
  return (
    <Box p={2} role="status" aria-label="Loading…">
      {Array.from({ length: rows }, (_, row) => (
        <Stack direction="row" spacing={2} key={row} mb={2}>
          {Array.from({ length: columns }, (_, column) => (
            <Skeleton key={column} width={`${100 / columns}%`} height={28} />
          ))}
        </Stack>
      ))}
    </Box>
  );
}

export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel,
  busy = false,
  danger = false,
  onClose,
  onConfirm,
  children
}: {
  open: boolean;
  title: string;
  description?: string;
  confirmLabel: string;
  busy?: boolean;
  danger?: boolean;
  onClose: () => void;
  onConfirm: () => void;
  children?: ReactNode;
}) {
  const { t } = useTranslation();
  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="xs">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        {description && <Typography variant="body1" color="text.secondary">{description}</Typography>}
        {children}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
        <Button variant="contained" color={danger ? 'error' : 'primary'} onClick={onConfirm} disabled={busy}>
          {busy ? t('common.working') : confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export function SecretDialog({ open, secret, onClose }: { open: boolean; secret: string; onClose: () => void }) {
  const { t } = useTranslation();
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('common.secretReadyTitle')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2}>
          <Alert severity="warning">{t('admin.keyOnce')}</Alert>
          <Box component="code" sx={{ p: 2, borderRadius: 2, bgcolor: 'action.hover', wordBreak: 'break-all', userSelect: 'all' }}>
            {secret}
          </Box>
        </Stack>
      </DialogContent>
      <DialogActions>
        <CopyButton text={secret} label={t('common.copySecret')} size="medium" />
        <Button variant="contained" onClick={onClose}>{t('common.secretSaved')}</Button>
      </DialogActions>
    </Dialog>
  );
}

export function CodeBlock({ code, label }: { code: string; label?: string }) {
  return (
    <Box sx={{ border: 1, borderColor: 'divider', borderRadius: 2, overflow: 'hidden' }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between" px={1.5} py={0.5} bgcolor="action.hover">
        <Typography variant="caption" color="text.secondary">{label ?? 'cURL'}</Typography>
        <CopyButton text={code} iconOnly />
      </Stack>
      <Box component="pre" sx={{ m: 0, p: 2, overflowX: 'auto', fontSize: '0.75rem', lineHeight: 1.7, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
        <code>{code}</code>
      </Box>
    </Box>
  );
}
