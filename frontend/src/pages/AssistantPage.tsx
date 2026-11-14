import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Divider,
  IconButton,
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
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import SendIcon from '@mui/icons-material/Send';
import StopCircleIcon from '@mui/icons-material/StopCircle';
import { useTranslation } from 'react-i18next';
import { api } from '../api';
import type {
  AssistantDoneEvent,
  AssistantMessageRow,
  AssistantSessionRow
} from '../api';

function bubble(
  row: AssistantMessageRow,
  t: (key: string) => string
) {
  const isUser = row.role === 'user';
  const toolNames = (row.toolNames ?? '')
    .split(',')
    .map((name) => name.trim())
    .filter((name) => name.length > 0);

  return (
    <Box
      key={row.id}
      sx={{
        display: 'flex',
        justifyContent: isUser ? 'flex-end' : 'flex-start',
        mb: 1.5
      }}
    >
      <Paper
        elevation={0}
        sx={{
          px: 2,
          py: 1.5,
          maxWidth: '78%',
          borderRadius: isUser ? '20px 20px 6px 20px' : '20px 20px 20px 6px',
          bgcolor: isUser ? 'primary.main' : 'background.paper',
          color: isUser ? 'primary.contrastText' : 'text.primary',
          border: isUser ? 'none' : '1px solid rgba(103,80,164,0.16)'
        }}
      >
        <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.7 }}>
          {row.content}
        </Typography>

        {!isUser && (toolNames.length > 0 || row.evidence.length > 0) && (
          <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap mt={1.25}>
            {toolNames.map((name) => (
              <Chip key={`tool-${name}`} size="small" label={name} variant="outlined" />
            ))}
            {row.evidence.slice(0, 3).map((id) => (
              <Tooltip key={`ev-${id}`} title={t('assistant.evidenceHint')}>
                <Chip size="small" label={id} color="secondary" variant="outlined" />
              </Tooltip>
            ))}
            {row.evidence.length > 3 && (
              <Chip size="small" label={`+${row.evidence.length - 3}`} variant="outlined" />
            )}
          </Stack>
        )}
      </Paper>
    </Box>
  );
}

export default function AssistantPage() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<AssistantSessionRow[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [messages, setMessages] = useState<AssistantMessageRow[]>([]);
  const [input, setInput] = useState('');
  const [streaming, setStreaming] = useState(false);
  const [streamText, setStreamText] = useState('');
  const [loadingSession, setLoadingSession] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const abortRef = useRef<AbortController | null>(null);
  const bottomRef = useRef<HTMLDivElement | null>(null);

  const loadSessions = useCallback(async () => {
    try {
      const result = await api.assistantSessions();
      setRows(result.sessions);
    } catch (err) {
      setError((err as Error).message);
    }
  }, []);

  useEffect(() => {
    void loadSessions();
    return () => abortRef.current?.abort();
  }, [loadSessions]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [messages, streamText]);

  const openSession = useCallback(async (id: string) => {
    abortRef.current?.abort();
    setStreaming(false);
    setStreamText('');
    setActiveId(id);
    setLoadingSession(true);
    setError(null);

    try {
      const detail = await api.assistantSession(id);
      setMessages(detail.messages);
    } catch (err) {
      setError((err as Error).message);
      setMessages([]);
    } finally {
      setLoadingSession(false);
    }
  }, []);

  const startNew = useCallback(() => {
    abortRef.current?.abort();
    setStreaming(false);
    setStreamText('');
    setActiveId(null);
    setMessages([]);
    setError(null);
  }, []);

  const removeSession = useCallback(
    async (id: string) => {
      try {
        await api.deleteAssistantSession(id);
        setRows((prev) => prev.filter((row) => row.id !== id));
        if (activeId === id) startNew();
      } catch (err) {
        setError((err as Error).message);
      }
    },
    [activeId, startNew]
  );

  const send = useCallback(() => {
    const question = input.trim();
    if (question.length === 0 || streaming) return;

    setInput('');
    setError(null);
    setStreaming(true);
    setStreamText('');

    setMessages((prev) => [
      ...prev,
      {
        id: `pending-user-${Date.now()}`,
        role: 'user',
        content: question,
        evidence: [],
        toolCallCount: 0,
        createdAt: new Date().toISOString()
      }
    ]);

    let accumulated = '';
    let stats: AssistantDoneEvent | null = null;
    let sessionId = activeId;

    abortRef.current = api.assistantStream(
      { question, sessionId: activeId ?? undefined },
      {
        onDelta: (text) => {
          accumulated += text;
          setStreamText(accumulated);
        },
        onStats: (event) => {
          stats = event;
        },
        onSession: (event) => {
          if (event.session_id) {
            sessionId = event.session_id;
            setActiveId(event.session_id);
          }
        },
        onError: (message) => setError(message),
        onDone: () => {
          setStreaming(false);
          setStreamText('');
          setMessages((prev) => [
            ...prev,
            {
              id: `pending-assistant-${Date.now()}`,
              role: 'assistant',
              content: accumulated.length > 0 ? accumulated : t('assistant.emptyAnswer'),
              evidence: stats?.evidence ?? [],
              toolNames: stats?.toolNames ?? null,
              toolCallCount: stats?.toolCalls ?? 0,
              model: stats?.model ?? null,
              createdAt: new Date().toISOString()
            }
          ]);
          void loadSessions();
          if (sessionId && sessionId !== activeId) {
            void openSession(sessionId);
          }
        }
      }
    );
  }, [activeId, input, loadSessions, openSession, streaming, t]);

  const stop = useCallback(() => {
    abortRef.current?.abort();
    abortRef.current = null;
  }, []);

  const activeTitle = useMemo(
    () => rows.find((row) => row.id === activeId)?.title ?? t('assistant.newSession'),
    [activeId, rows, t]
  );

  return (
    <Box>
      <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}>
        <Box>
          <Typography variant="h2">{t('assistant.title')}</Typography>
          <Typography variant="body2" color="text.secondary">
            {t('assistant.hint')}
          </Typography>
        </Box>
        <Button startIcon={<AddCommentIcon />} onClick={startNew} variant="outlined">
          {t('assistant.newSession')}
        </Button>
      </Stack>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
          {error}
        </Alert>
      )}

      <Box display="flex" gap={2} alignItems="stretch" sx={{ height: 'calc(100vh - 220px)', minHeight: 420 }}>
        <Card sx={{ width: 260, flexShrink: 0, display: 'flex', flexDirection: 'column' }}>
          <CardContent sx={{ flex: 1, overflowY: 'auto', p: 1 }}>
            <Typography variant="caption" color="text.secondary" sx={{ px: 1 }}>
              {t('assistant.sessions')}
            </Typography>
            <List dense sx={{ mt: 0.5 }}>
              {rows.length === 0 && (
                <Typography variant="body2" color="text.secondary" sx={{ px: 1, py: 2 }}>
                  {t('common.empty')}
                </Typography>
              )}
              {rows.map((row) => (
                <ListItemButton
                  key={row.id}
                  selected={row.id === activeId}
                  onClick={() => void openSession(row.id)}
                  sx={{ borderRadius: 3, pr: 0.5 }}
                >
                  <ListItemText
                    primary={row.title}
                    secondary={new Date(row.updatedAt).toLocaleString()}
                    primaryTypographyProps={{ noWrap: true, fontSize: 14 }}
                    secondaryTypographyProps={{ fontSize: 11 }}
                  />
                  <Tooltip title={t('common.actions')}>
                    <IconButton
                      size="small"
                      onClick={(event) => {
                        event.stopPropagation();
                        void removeSession(row.id);
                      }}
                    >
                      <DeleteOutlineIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                </ListItemButton>
              ))}
            </List>
          </CardContent>
        </Card>

        <Card sx={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
          <CardContent sx={{ flex: 1, overflowY: 'auto' }}>
            <Typography variant="subtitle2" color="text.secondary" gutterBottom>
              {activeTitle}
            </Typography>
            <Divider sx={{ mb: 2 }} />

            {loadingSession && (
              <Box display="flex" justifyContent="center" py={4}>
                <CircularProgress size={24} />
              </Box>
            )}

            {!loadingSession && messages.length === 0 && !streaming && (
              <Typography variant="body2" color="text.secondary">
                {t('assistant.placeholder')}
              </Typography>
            )}

            {messages.map((row) => bubble(row, t as unknown as (key: string) => string))}

            {streaming && (
              <Box sx={{ display: 'flex', justifyContent: 'flex-start', mb: 1.5 }}>
                <Paper
                  elevation={0}
                  sx={{
                    px: 2,
                    py: 1.5,
                    maxWidth: '78%',
                    borderRadius: '20px 20px 20px 6px',
                    border: '1px solid rgba(103,80,164,0.16)'
                  }}
                >
                  {streamText.length > 0 ? (
                    <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.7 }}>
                      {streamText}
                    </Typography>
                  ) : (
                    <Stack direction="row" spacing={1} alignItems="center">
                      <CircularProgress size={14} />
                      <Typography variant="body2" color="text.secondary">
                        {t('assistant.thinking')}
                      </Typography>
                    </Stack>
                  )}
                </Paper>
              </Box>
            )}

            <div ref={bottomRef} />
          </CardContent>

          <Divider />
          <Box sx={{ p: 2 }}>
            <Stack direction="row" spacing={1} alignItems="flex-end">
              <TextField
                fullWidth
                multiline
                minRows={1}
                maxRows={5}
                size="small"
                value={input}
                placeholder={t('assistant.inputPlaceholder')}
                onChange={(event) => setInput(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' && !event.shiftKey) {
                    event.preventDefault();
                    send();
                  }
                }}
                disabled={streaming}
              />
              {streaming ? (
                <IconButton color="error" onClick={stop} aria-label={t('assistant.stop')}>
                  <StopCircleIcon />
                </IconButton>
              ) : (
                <IconButton
                  color="primary"
                  onClick={send}
                  disabled={input.trim().length === 0}
                  aria-label={t('assistant.send')}
                >
                  <SendIcon />
                </IconButton>
              )}
            </Stack>
            <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
              {t('assistant.inputHint')}
            </Typography>
          </Box>
        </Card>
      </Box>
    </Box>
  );
}
