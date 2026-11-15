import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Card,
  Chip,
  CircularProgress,
  Divider,
  IconButton,
  InputAdornment,
  List,
  ListItemButton,
  ListItemText,
  Paper,
  Stack,
  TextField,
  Tooltip,
  Typography
} from '@mui/material';
import AddCommentIcon from '@mui/icons-material/AddComment';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import HistoryIcon from '@mui/icons-material/History';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import PersonOutlineIcon from '@mui/icons-material/PersonOutline';
import SearchIcon from '@mui/icons-material/Search';
import SendIcon from '@mui/icons-material/Send';
import StopCircleIcon from '@mui/icons-material/StopCircle';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../api';
import type { AssistantDoneEvent, AssistantMessageRow, AssistantSessionRow } from '../api';
import { BrandMark } from '../components/Brand';
import { ConfirmDialog, EmptyState, PageHeader } from '../components/ui';
import { useFormatters } from '../utils/format';

export default function AssistantWorkspace() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [searchParams, setSearchParams] = useSearchParams();
  const activeId = searchParams.get('session');
  const [rows, setRows] = useState<AssistantSessionRow[]>([]);
  const [tools, setTools] = useState<Array<{ name: string; readOnly: boolean }>>([]);
  const [messages, setMessages] = useState<AssistantMessageRow[]>([]);
  const [input, setInput] = useState('');
  const [sessionQuery, setSessionQuery] = useState('');
  const [streaming, setStreaming] = useState(false);
  const [streamText, setStreamText] = useState('');
  const [loadingSession, setLoadingSession] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const bottomRef = useRef<HTMLDivElement | null>(null);

  const loadSessions = useCallback(async () => {
    try { setRows((await api.assistantSessions()).sessions); } catch (err) { setError((err as Error).message); }
  }, []);

  useEffect(() => {
    void loadSessions();
    api.assistantTools().then((result) => setTools(result.tools)).catch(() => undefined);
    return () => abortRef.current?.abort();
  }, [loadSessions]);

  useEffect(() => {
    abortRef.current?.abort();
    setStreaming(false);
    setStreamText('');
    setError(null);
    if (!activeId) { setMessages([]); return; }
    setLoadingSession(true);
    api.assistantSession(activeId)
      .then((detail) => setMessages(detail.messages))
      .catch((err) => { setError((err as Error).message); setMessages([]); })
      .finally(() => setLoadingSession(false));
  }, [activeId]);

  useEffect(() => {
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    bottomRef.current?.scrollIntoView({ behavior: reduced ? 'auto' : 'smooth', block: 'end' });
  }, [messages, streamText]);

  const startNew = useCallback(() => {
    abortRef.current?.abort();
    setStreaming(false);
    setStreamText('');
    setError(null);
    setSearchParams({});
  }, [setSearchParams]);

  const removeSession = async () => {
    if (!deleteTarget) return;
    try {
      await api.deleteAssistantSession(deleteTarget);
      setRows((current) => current.filter((row) => row.id !== deleteTarget));
      if (activeId === deleteTarget) startNew();
      setDeleteTarget(null);
    } catch (err) { setError((err as Error).message); }
  };

  const sendQuestion = useCallback((rawQuestion: string) => {
    const question = rawQuestion.trim();
    if (!question || streaming) return;
    setInput('');
    setError(null);
    setStreaming(true);
    setStreamText('');
    setMessages((current) => [...current, { id: `pending-user-${Date.now()}`, role: 'user', content: question, evidence: [], toolCallCount: 0, createdAt: new Date().toISOString() }]);

    let accumulated = '';
    let stats: AssistantDoneEvent | null = null;
    let failed = false;
    let stopped = false;
    abortRef.current = api.assistantStream(
      { question, sessionId: activeId ?? undefined },
      {
        onDelta: (text) => { accumulated += text; setStreamText(accumulated); },
        onStats: (event) => { stats = event; },
        onSession: (event) => { if (event.session_id) setSearchParams({ session: event.session_id }); },
        onError: (message) => { failed = true; setError(message); },
        onDone: () => {
          setStreaming(false);
          setStreamText('');
          if (!failed && (accumulated || !stopped)) {
            setMessages((current) => [...current, {
              id: `pending-assistant-${Date.now()}`, role: 'assistant', content: accumulated || t('assistant.emptyAnswer'),
              evidence: stats?.evidence ?? [], toolNames: stats?.toolNames ?? null,
              toolCallCount: stats?.toolCalls ?? 0, model: stats?.model ?? null, createdAt: new Date().toISOString()
            }]);
          }
          void loadSessions();
        }
      }
    );
    const originalAbort = abortRef.current.abort.bind(abortRef.current);
    abortRef.current.abort = () => { stopped = true; originalAbort(); };
  }, [activeId, loadSessions, setSearchParams, streaming, t]);

  const filteredSessions = useMemo(() => rows.filter((row) => row.title.toLowerCase().includes(sessionQuery.trim().toLowerCase())), [rows, sessionQuery]);
  const activeTitle = rows.find((row) => row.id === activeId)?.title ?? t('assistant.newSession');
  // 写工具只对 SecurityWrite 角色注入；有它就不该再标「只读模式」。
  const canWrite = tools.some((tool) => !tool.readOnly);

  return (
    <Stack spacing={2.5} sx={{ height: 'calc(100vh - 128px)', minHeight: 620 }}>
      <PageHeader title={t('assistant.title')} description={t('assistant.hint')} actions={<>
        <Chip size="small" color={canWrite ? 'warning' : 'success'} icon={<LockOutlinedIcon />} variant="outlined" label={canWrite ? t('assistant.readWrite') : t('assistant.readonly')} />
        <Button variant="contained" startIcon={<AddCommentIcon />} onClick={startNew}>{t('assistant.newSession')}</Button>
      </>} />
      {error && <Alert severity="error" onClose={() => setError(null)}>{error}</Alert>}
      <Box sx={{ flex: 1, minHeight: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '280px minmax(0, 1fr)' }, gap: 2 }}>
        <Card sx={{ display: { xs: 'none', lg: 'flex' }, flexDirection: 'column', minHeight: 0 }}>
          <Box p={1.5}><TextField fullWidth size="small" placeholder={t('common.search')} value={sessionQuery} onChange={(event) => setSessionQuery(event.target.value)} autoComplete="off" InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> }} /></Box>
          <Typography variant="overline" color="text.secondary" px={2} pb={1}>{t('assistant.sessions')}</Typography>
          <List dense sx={{ flex: 1, minHeight: 0, overflowY: 'auto', px: 1 }}>
            {filteredSessions.map((row) => (
              <ListItemButton key={row.id} selected={row.id === activeId} onClick={() => setSearchParams({ session: row.id })} sx={{ borderRadius: 2, mb: 0.5, pr: 0.5, alignItems: 'flex-start' }}>
                <ListItemText primary={row.title} secondary={`${row.messageCount} · ${format.dateTime(row.updatedAt)}`} primaryTypographyProps={{ noWrap: true, fontSize: 13, fontWeight: 550 }} secondaryTypographyProps={{ fontSize: 11 }} />
                <Tooltip title={t('common.remove')}><IconButton size="small" onClick={(event) => { event.stopPropagation(); setDeleteTarget(row.id); }} aria-label={t('common.remove')}><DeleteOutlineIcon sx={{ fontSize: 16 }} /></IconButton></Tooltip>
              </ListItemButton>
            ))}
          </List>
          {filteredSessions.length === 0 && <EmptyState compact title={t('assistant.emptySessions')} />}
          <Divider />
          <Box p={1.5}><Stack direction="row" spacing={0.75} alignItems="center" mb={1}><HistoryIcon sx={{ fontSize: 15 }} color="action" /><Typography variant="caption" color="text.secondary">{t('assistant.tools')} · {tools.length || 8}</Typography></Stack><Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>{tools.slice(0, 4).map((tool) => <Chip key={tool.name} size="small" variant="outlined" color={tool.readOnly ? undefined : 'warning'} label={tool.name.replaceAll('_', ' ')} sx={{ fontSize: 9, height: 21 }} />)}</Stack></Box>
        </Card>

        <Card sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, overflow: 'hidden' }}>
          <Stack direction="row" alignItems="center" justifyContent="space-between" px={2.5} py={1.5} borderBottom={1} borderColor="divider">
            <Box minWidth={0}><Typography variant="subtitle1" noWrap>{activeTitle}</Typography><Typography variant="caption" color="text.secondary">{t('assistant.poweredBy')}</Typography></Box>
            <Chip size="small" variant="outlined" label="SSE" />
          </Stack>
          <Box sx={{ flex: 1, minHeight: 0, overflowY: 'auto', p: { xs: 2, md: 3 } }}>
            {loadingSession && <Box display="flex" justifyContent="center" py={8}><CircularProgress size={26} /></Box>}
            {!loadingSession && messages.length === 0 && !streaming && (
              <Stack alignItems="center" justifyContent="center" height="100%" maxWidth={680} mx="auto" textAlign="center" spacing={2.5}>
                <Box sx={{ position: 'relative' }}><BrandMark size={52} /><AutoAwesomeIcon sx={{ position: 'absolute', right: -8, top: -8, color: 'secondary.main', fontSize: 20 }} /></Box>
                <Box><Typography variant="h1">{t('assistant.welcome')}</Typography><Typography variant="body1" color="text.secondary" mt={1}>{t('assistant.welcomeHint')}</Typography></Box>
                <Stack spacing={1} width="100%" mt={1}>
                  {[t('assistant.promptOne'), t('assistant.promptTwo'), t('assistant.promptThree')].map((prompt, index) => (
                    <Paper key={prompt} component="button" onClick={() => sendQuestion(prompt)} sx={{ p: 1.75, textAlign: 'left', cursor: 'pointer', border: 1, borderColor: 'divider', bgcolor: 'background.paper', color: 'text.primary', display: 'flex', alignItems: 'center', gap: 1.5, transition: 'border-color 160ms ease, transform 160ms ease', '&:hover': { borderColor: 'primary.main', transform: 'translateY(-1px)' } }}>
                      <Chip size="small" label={`0${index + 1}`} color="primary" variant="outlined" /><Typography variant="body2" flexGrow={1}>{prompt}</Typography><SendIcon sx={{ fontSize: 16, color: 'text.disabled' }} />
                    </Paper>
                  ))}
                </Stack>
              </Stack>
            )}
            {!loadingSession && messages.map((row) => <MessageBubble key={row.id} row={row} />)}
            {streaming && (
              <Stack direction="row" spacing={1.5} alignItems="flex-start" mb={2}>
                <Avatar sx={{ width: 30, height: 30, bgcolor: 'primary.main' }}><AutoAwesomeIcon sx={{ fontSize: 16 }} /></Avatar>
                <Paper elevation={0} sx={{ p: 2, border: 1, borderColor: 'divider', maxWidth: '82%', flexGrow: streamText ? 0 : 1 }}>
                  {streamText ? <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.75 }}>{streamText}</Typography> : <Stack direction="row" spacing={1} alignItems="center"><CircularProgress size={14} /><Typography variant="body2" color="text.secondary">{t('assistant.thinking')}</Typography></Stack>}
                </Paper>
              </Stack>
            )}
            <div ref={bottomRef} />
          </Box>
          <Box sx={{ p: 2, borderTop: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
            <Stack direction="row" spacing={1} alignItems="flex-end">
              <TextField fullWidth multiline minRows={1} maxRows={5} value={input} placeholder={t('assistant.inputPlaceholder')} onChange={(event) => setInput(event.target.value)} onKeyDown={(event) => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); sendQuestion(input); } }} disabled={streaming} name="assistantQuestion" autoComplete="off" />
              {streaming ? <Tooltip title={t('assistant.stop')}><IconButton color="error" onClick={() => abortRef.current?.abort()} aria-label={t('assistant.stop')} sx={{ width: 42, height: 42 }}><StopCircleIcon /></IconButton></Tooltip> : <Tooltip title={t('assistant.send')}><span><IconButton color="primary" onClick={() => sendQuestion(input)} disabled={!input.trim()} aria-label={t('assistant.send')} sx={{ width: 42, height: 42, bgcolor: 'primary.main', color: 'primary.contrastText', '&:hover': { bgcolor: 'primary.dark' }, '&.Mui-disabled': { bgcolor: 'action.disabledBackground' } }}><SendIcon fontSize="small" /></IconButton></span></Tooltip>}
            </Stack>
            <Typography variant="caption" color="text.secondary" display="block" mt={1}>{t('assistant.inputHint')}</Typography>
          </Box>
        </Card>
      </Box>
      <ConfirmDialog open={!!deleteTarget} title={t('assistant.deleteTitle')} description={t('assistant.deleteHint')} confirmLabel={t('common.remove')} danger onClose={() => setDeleteTarget(null)} onConfirm={() => void removeSession()} />
    </Stack>
  );
}

function MessageBubble({ row }: { row: AssistantMessageRow }) {
  const { t } = useTranslation();
  const isUser = row.role === 'user';
  const toolNames = (row.toolNames ?? '').split(',').map((name) => name.trim()).filter(Boolean);
  return (
    <Stack direction="row" spacing={1.5} justifyContent={isUser ? 'flex-end' : 'flex-start'} alignItems="flex-start" mb={2.25}>
      {!isUser && <Avatar sx={{ width: 30, height: 30, bgcolor: 'primary.main' }}><AutoAwesomeIcon sx={{ fontSize: 16 }} /></Avatar>}
      <Box sx={{ maxWidth: '82%', minWidth: 0 }}>
        <Paper elevation={0} sx={{ px: 2, py: 1.5, bgcolor: isUser ? 'primary.main' : 'background.paper', color: isUser ? 'primary.contrastText' : 'text.primary', border: isUser ? 0 : 1, borderColor: 'divider', borderRadius: isUser ? '12px 12px 4px 12px' : '4px 12px 12px 12px' }}>
          <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.75, overflowWrap: 'anywhere' }}>{row.content}</Typography>
        </Paper>
        {!isUser && (toolNames.length > 0 || row.evidence.length > 0) && <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap mt={1}>{toolNames.map((name) => <Chip key={name} size="small" icon={<LockOutlinedIcon />} label={name} variant="outlined" />)}{row.evidence.map((id) => <Tooltip key={id} title={t('assistant.evidenceHint')}><Chip size="small" label={id} color="secondary" variant="outlined" /></Tooltip>)}</Stack>}
      </Box>
      {isUser && <Avatar sx={{ width: 30, height: 30, bgcolor: 'action.selected' }}><PersonOutlineIcon sx={{ fontSize: 17 }} /></Avatar>}
    </Stack>
  );
}
