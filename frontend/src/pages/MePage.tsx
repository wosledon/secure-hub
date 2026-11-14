import { useCallback, useEffect, useState } from 'react';
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
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tabs,
  TextField,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import KeyIcon from '@mui/icons-material/Key';
import { useTranslation } from 'react-i18next';
import { api, type ModelRow, type MyKeyRow, type MyProviderRow } from '../api';

const DEVKEY_STORAGE = 'sh.devkey';

export default function MePage() {
  const { t } = useTranslation();
  const [tab, setTab] = useState(0);

  return (
    <Stack spacing={3}>
      <Box>
        <Typography variant="h1">{t('nav.me')}</Typography>
        <Typography color="text.secondary">{t('me.modelsHint')}</Typography>
      </Box>

      <Tabs value={tab} onChange={(_, value) => setTab(value)}>
        <Tab label={t('nav.models')} />
        <Tab label={t('nav.providers')} />
        <Tab label={t('nav.keys')} />
      </Tabs>

      {tab === 0 && <ModelsTab />}
      {tab === 1 && <MyProvidersTab />}
      {tab === 2 && <MyKeysTab />}
    </Stack>
  );
}

function ModelsTab() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<ModelRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [hasKey, setHasKey] = useState(!!localStorage.getItem(DEVKEY_STORAGE));

  const load = useCallback(async () => {
    if (!localStorage.getItem(DEVKEY_STORAGE)) {
      setHasKey(false);
      return;
    }
    try {
      const result = await api.models();
      setRows(result.data);
      setError(null);
    } catch (err) {
      setError(String(err));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <Card>
      <CardContent>
        <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}>
          <Typography variant="h2">{t('nav.models')}</Typography>
          <Button onClick={() => void load()}>{t('common.refresh')}</Button>
        </Stack>

        {!hasKey && (
          <Alert severity="info" sx={{ mb: 2 }}>
            先在「{t('nav.keys')}」里生成一个个人密钥，再回到这里查看可用模型。
          </Alert>
        )}
        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t('table.modelId')}</TableCell>
              <TableCell>{t('table.source')}</TableCell>
              <TableCell>{t('table.billing')}</TableCell>
              <TableCell>context</TableCell>
              <TableCell>{t('common.actions')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.id}>
                <TableCell>
                  <Box component="code">{row.id}</Box>
                </TableCell>
                <TableCell>
                  <Chip
                    size="small"
                    color={row.x_securehub.source === 'personal' ? 'secondary' : 'primary'}
                    label={row.x_securehub.source}
                    variant="outlined"
                  />
                </TableCell>
                <TableCell>
                  {row.x_securehub.billing === 'user' ? t('me.billingUser') : t('me.billingOrg')}
                </TableCell>
                <TableCell>{row.x_securehub.context_window.toLocaleString()}</TableCell>
                <TableCell>
                  <Button
                    size="small"
                    onClick={() => void navigator.clipboard.writeText(row.id)}
                  >
                    {t('common.copy')}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
            {rows.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>{t('common.empty')}</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function MyProvidersTab() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<MyProviderRow[]>([]);
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({
    alias: 'my-openai',
    providerKey: 'openai',
    baseUrl: 'https://api.openai.com/v1',
    apiKey: '',
    models: 'gpt-4o-mini'
  });
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => setRows(await api.myProviders()), []);
  useEffect(() => {
    void load();
  }, [load]);

  const submit = async () => {
    setError(null);
    try {
      await api.createMyProvider({
        alias: form.alias,
        providerKey: form.providerKey,
        baseUrl: form.baseUrl,
        apiKey: form.apiKey,
        models: form.models.split(',').map((m) => m.trim()).filter(Boolean)
      });
      setOpen(false);
      await load();
    } catch (err) {
      setError(String(err));
    }
  };

  return (
    <Card>
      <CardContent>
        <Stack direction="row" justifyContent="space-between" alignItems="center" mb={1}>
          <Typography variant="h2">{t('nav.providers')}</Typography>
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>
            {t('me.createByok')}
          </Button>
        </Stack>
        <Typography color="text.secondary" variant="body2" mb={2}>
          {t('me.byokHint')}
        </Typography>

        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t('common.alias')}</TableCell>
              <TableCell>{t('common.provider')}</TableCell>
              <TableCell>{t('common.baseUrl')}</TableCell>
              <TableCell>{t('common.models')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.id}>
                <TableCell>
                  <Chip size="small" label={row.alias} />
                </TableCell>
                <TableCell>{row.providerKey}</TableCell>
                <TableCell>{row.baseUrl}</TableCell>
                <TableCell>{row.models.join(', ')}</TableCell>
              </TableRow>
            ))}
            {rows.length === 0 && (
              <TableRow>
                <TableCell colSpan={4}>{t('common.empty')}</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </CardContent>

      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{t('me.createByok')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} mt={1}>
            <TextField label={t('common.alias')} value={form.alias} onChange={(e) => setForm({ ...form, alias: e.target.value })} helperText="用于 me/<alias>/<model>" />
            <TextField label={t('common.provider')} value={form.providerKey} onChange={(e) => setForm({ ...form, providerKey: e.target.value })} />
            <TextField label={t('common.baseUrl')} value={form.baseUrl} onChange={(e) => setForm({ ...form, baseUrl: e.target.value })} />
            <TextField label={t('common.apiKey')} type="password" value={form.apiKey} onChange={(e) => setForm({ ...form, apiKey: e.target.value })} />
            <TextField label={t('common.models')} value={form.models} onChange={(e) => setForm({ ...form, models: e.target.value })} helperText="逗号分隔" />
            {error && <Alert severity="error">{error}</Alert>}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>{t('common.cancel')}</Button>
          <Button variant="contained" onClick={() => void submit()} disabled={!form.alias || !form.apiKey}>
            {t('common.save')}
          </Button>
        </DialogActions>
      </Dialog>
    </Card>
  );
}

function MyKeysTab() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<MyKeyRow[]>([]);
  const [created, setCreated] = useState<string | null>(null);

  const load = useCallback(async () => setRows(await api.myKeys()), []);
  useEffect(() => {
    void load();
  }, [load]);

  const create = async () => {
    const result = await api.createMyKey();
    setCreated(result.key);
    localStorage.setItem(DEVKEY_STORAGE, result.key);
    await load();
  };

  return (
    <Card>
      <CardContent>
        <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}>
          <Typography variant="h2">{t('nav.keys')}</Typography>
          <Button variant="contained" startIcon={<KeyIcon />} onClick={() => void create()}>
            {t('me.createMyKey')}
          </Button>
        </Stack>

        {created && (
          <Alert severity="warning" sx={{ mb: 2 }}>
            <Typography variant="body2" mb={1}>
              {t('admin.keyOnce')}
            </Typography>
            <Box component="code" sx={{ wordBreak: 'break-all' }}>
              {created}
            </Box>
          </Alert>
        )}

        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t('common.name')}</TableCell>
              <TableCell>key id</TableCell>
              <TableCell>status</TableCell>
              <TableCell>{t('common.createdAt')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.id}>
                <TableCell>{row.display_name}</TableCell>
                <TableCell>{row.key_id}</TableCell>
                <TableCell>
                  <Chip size="small" color={row.is_revoked ? 'default' : 'success'} label={row.is_revoked ? 'revoked' : 'active'} />
                </TableCell>
                <TableCell>{new Date(row.created_at).toLocaleString()}</TableCell>
              </TableRow>
            ))}
            {rows.length === 0 && (
              <TableRow>
                <TableCell colSpan={4}>{t('common.empty')}</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}
