import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

export const zhCN = {
  app: { name: 'Secure Hub', tagline: '企业 AI 网关' },
  nav: {
    admin: '管理端',
    me: '我的工作台',
    assistant: '审计助手',
    overview: '概览',
    providers: '供应商',
    keys: '密钥',
    audit: '审计',
    models: '可用模型',
    logout: '退出登录'
  },
  login: {
    title: '登录',
    userName: '用户名',
    password: '密码',
    submit: '登录',
    failed: '用户名或密码错误'
  },
  common: {
    create: '新建',
    save: '保存',
    cancel: '取消',
    refresh: '刷新',
    copy: '复制',
    copied: '已复制',
    loading: '加载中…',
    empty: '暂无数据',
    actions: '操作',
    name: '名称',
    alias: '别名',
    provider: '供应商',
    baseUrl: '接口地址',
    apiKey: 'API Key',
    models: '模型',
    createdAt: '创建时间',
    language: '语言'
  },
  admin: {
    overviewHint: '企业调用、成本与拦截概览',
    createProvider: '新增企业供应商',
    createKey: '签发应用密钥',
    auditHint: '按时间范围检索（默认近 7 天）',
    verifyChain: '校验审计链',
    chainOk: '审计链完整，已校验 {{count}} 条',
    chainBroken: '审计链在序号 {{seq}} 处断裂',
    keyOnce: '密钥只显示一次，请立即保存'
  },
  me: {
    modelsHint: '企业分配给你的，以及你自己接入的供应商',
    byokHint: '填入你自己的供应商 Key，费用由你承担，但调用仍会被审计',
    createByok: '接入我的供应商',
    createMyKey: '生成个人密钥',
    billingOrg: '企业账',
    billingUser: '个人账'
  },
  assistant: {
    title: '审计助手',
    hint: '用自然语言查审计、成本与路由情况；结论必须带证据',
    sessions: '会话',
    newSession: '新会话',
    placeholder: '问点什么，比如：最近 7 天有多少请求被护栏拦截？',
    inputPlaceholder: '输入问题，Enter 发送，Shift+Enter 换行',
    inputHint: '助手只能读审计元数据；正文读取默认关闭，需管理员开启。',
    send: '发送',
    stop: '停止',
    thinking: '正在检索审计数据…',
    emptyAnswer: '模型未返回内容，请换个问法。',
    evidenceHint: '证据 ID，可在审计日志中检索'
  },
  table: {
    source: '来源',
    billing: '计费',
    modelId: '模型 ID',
    quota: '额度'
  }
};

export const en = {
  app: { name: 'Secure Hub', tagline: 'Enterprise AI Gateway' },
  nav: {
    admin: 'Admin',
    me: 'My Workspace',
    assistant: 'Audit Assistant',
    overview: 'Overview',
    providers: 'Providers',
    keys: 'Keys',
    audit: 'Audit',
    models: 'Models',
    logout: 'Sign out'
  },
  login: {
    title: 'Sign in',
    userName: 'User name',
    password: 'Password',
    submit: 'Sign in',
    failed: 'Invalid user name or password'
  },
  common: {
    create: 'Create',
    save: 'Save',
    cancel: 'Cancel',
    refresh: 'Refresh',
    copy: 'Copy',
    copied: 'Copied',
    loading: 'Loading…',
    empty: 'No data',
    actions: 'Actions',
    name: 'Name',
    alias: 'Alias',
    provider: 'Provider',
    baseUrl: 'Base URL',
    apiKey: 'API key',
    models: 'Models',
    createdAt: 'Created',
    language: 'Language'
  },
  admin: {
    overviewHint: 'Calls, cost and blocks across the organization',
    createProvider: 'Add enterprise provider',
    createKey: 'Issue app key',
    auditHint: 'Search by time range (last 7 days by default)',
    verifyChain: 'Verify audit chain',
    chainOk: 'Audit chain intact, {{count}} events checked',
    chainBroken: 'Audit chain broken at sequence {{seq}}',
    keyOnce: 'The key is shown once. Store it now.'
  },
  me: {
    modelsHint: 'Models your organization granted plus your own BYOK providers',
    byokHint: 'Bring your own provider key. You pay, but calls are still audited',
    createByok: 'Connect my provider',
    createMyKey: 'Create personal key',
    billingOrg: 'Org',
    billingUser: 'Personal'
  },
  assistant: {
    title: 'Audit Assistant',
    hint: 'Ask about audit, cost and routing; every conclusion must cite evidence',
    sessions: 'Sessions',
    newSession: 'New session',
    placeholder: 'Try: how many requests were blocked by guardrails in the last 7 days?',
    inputPlaceholder: 'Type a question. Enter to send, Shift+Enter for a new line',
    inputHint: 'The assistant reads audit metadata only. Content reading is off unless an admin enables it.',
    send: 'Send',
    stop: 'Stop',
    thinking: 'Searching audit data…',
    emptyAnswer: 'The model returned nothing. Try rephrasing.',
    evidenceHint: 'Evidence id, searchable in the audit log'
  },
  table: {
    source: 'Source',
    billing: 'Billing',
    modelId: 'Model ID',
    quota: 'Quota'
  }
};

const stored = localStorage.getItem('sh.lang');
const browser = navigator.language.toLowerCase().startsWith('zh') ? 'zh-CN' : 'en';

void i18n.use(initReactI18next).init({
  resources: {
    'zh-CN': { translation: zhCN },
    en: { translation: en }
  },
  lng: stored ?? browser,
  fallbackLng: 'zh-CN',
  interpolation: { escapeValue: false }
});

export function setLanguage(lang: string) {
  localStorage.setItem('sh.lang', lang);
  void i18n.changeLanguage(lang);
}

export default i18n;
