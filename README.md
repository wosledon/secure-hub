# Secure Hub

企业自托管的 AI 流量统一出入口：安全管控、模型路由、供应商接入（models.dev）、能力矩阵、成本计量与审计。
同时支持**员工自带供应商 Key（BYOK）**——谁的 Key 谁付费，但流量与审计口径统一。

- 需求文档：[`docs/PRD.md`](docs/PRD.md)
- 安全设计：[`docs/design/security.md`](docs/design/security.md)

---

## 快速开始

```powershell
# 1. 后端（.NET 10）
dotnet run --project src/SecureHub.Api --urls http://127.0.0.1:5080

# 2. 前端（开发模式，可选）
cd frontend
npm install
npm run dev          # http://127.0.0.1:5173，已配置代理到 5080

# 2'. 或构建后由后端直接托管
npm run build        # 产物写入 src/SecureHub.Api/wwwroot
```

首次启动会：

1. 在 `./data` 创建 SQLite 数据库与加密密钥；
2. 生成一次性管理员密码，打印到控制台并写入 `./data/initial-admin-password.txt`。

访问 <http://127.0.0.1:5080>，用 `admin` + 该密码登录。**登录后删除密码文件。**

---

## 目录结构

```
src/
  SecureHub.Core/        领域模型、模型 ID 解析、负载均衡、护栏、压缩、RBAC、i18n
  SecureHub.Data/        EF Core 上下文（SQLite / PostgreSQL）、启动引导与种子
  SecureHub.Api/         ASP.NET Core：数据面、管理 API、用户端 API、静态托管
tests/
  SecureHub.Core.Tests/  单元测试
frontend/                React + TypeScript + Material Design 3 控制台
docs/                    PRD 与设计文档
```

---

## 已实现

| 能力        | 说明                                                                                             |
| ----------- | ------------------------------------------------------------------------------------------------ |
| 虚拟密钥    | `sk-sh-{env}-{keyId}-{secret}`，只存 SHA-256，常量时间比对，可吊销                               |
| 会话登录    | Cookie 会话 + RBAC（五角色 + EndUser 个人范围）                                                  |
| 数据面      | `POST /v1/chat/completions`（含流式透传）、`GET /v1/models`                                      |
| 三级模型 ID | `[scope/]provider/model`，model 段允许含 `/`；歧义报错并给候选                                   |
| 双账        | `billingOwner = org / user`，按**实际命中成员**记账                                              |
| 护栏        | 身份证（mod 11-2）、手机、银行卡（Luhn）、邮箱、PEM、JWT、高熵密钥                               |
| 脱敏        | `mask` 模式原地改写，命中值以哈希留痕                                                            |
| 压缩        | 空白/Unicode 规范化、样板文本剥离、重复行折叠 + 完整性校验回退                                   |
| 审计        | 链式哈希 + 逐条校验接口，时间截断到毫秒以保证可复验                                              |
| 内容存储    | AES-256-GCM，AAD 绑定 `requestId · direction · variant`，按变体留存                              |
| 供应商      | 企业级 / 个人 BYOK，凭据 DataProtection 加密                                                     |
| 控制台      | 概览、供应商、密钥、审计（含链校验）、个人工作台                                                 |
| i18n        | 中/英切换；后端 `Accept-Language` 本地化错误文案，审计只存 code                                  |
| 策略引擎    | 结构化条件（any/all + detector）、scope 通配、优先级、灰度分桶、版本发布与回滚、低置信度 dry-run |
| 预算与配额  | 组织/团队/应用维度，按日/周/月滚动，超限 429 并写审计                                            |
| 显式模型组  | 具名组优先于同名 offering，成员优先级/权重/健康态驱动，可选跨 scope                              |
| 目录同步    | 每 24h 拉取 models.dev（约 9000+ 模型），失败保留上一份快照，可手动触发                          |
| TOTP        | 可选启用：`enable` 返回 otpauth URI，`activate` 验证后登录需动态码                               |
| 审计导出    | 按时间范围流式导出 NDJSON                                                                        |
| 误报申诉    | 独立追加表记录，不回写审计行                                                                     |

## 未实现

| 能力                     | 状态                                                      |
| ------------------------ | --------------------------------------------------------- |
| 内建 AI 助手             | 未实现（依赖 `Microsoft.Agents.AI`，版本仍在演进）        |
| PostgreSQL 迁移脚本      | 可切换 Provider，但未生成迁移（当前使用 `EnsureCreated`） |
| 语义缓存 / 语义路由      | 按 PRD 决定不做（T14）                                    |
| 内容 `tokenize` 可逆脱敏 | 未实现（当前只有 mask / replace）                         |

---

## 关键设计

**三级模型 ID**：`org/azure/gpt-4o`、`me/openai/gpt-4o`。
同一个 `gpt-4o` 在 Azure 与 OpenAI 官方是不同 offering（价格、限额、数据驻留不同），
所以全限定是常态；简写仅在唯一时可解析，且 `/v1/models` **永不返回简写**。

**审计链**：`hash = SHA256(prevHash + "\n" + canonicalJson)`。
`OccurredAt` 入库前截断到毫秒——否则 SQLite 读回精度变化会让链校验失败。

**内容加密**：SQLite 无静态加密，因此正文在应用层用 AES-256-GCM 加密，
AAD 绑定 `requestId|direction|variant`，防止密文行被挪到别的请求。

**i18n 边界**：只本地化面向人的文案。错误 `code`、审计 `eventType`、`reasonCode`
永远存英文标识符，切换语言不会污染历史数据。

---

## 测试

```powershell
dotnet test
```

覆盖：模型 ID 解析（含 model 内斜杠、非法形式）、负载均衡（优先级/权重/降级）、
密钥格式、RBAC 矩阵、i18n 协商、PII 校验位、熵检测、脱敏、压缩与完整性回退。

---

## 配置

`src/SecureHub.Api/appsettings.json` → `SecureHub` 段：

| 键                          | 说明                              |
| --------------------------- | --------------------------------- |
| `Database.Provider`         | `Sqlite` / `Postgres`             |
| `Database.ConnectionString` | SQLite 文件或 PG 连接串           |
| `Security.DataDirectory`    | 数据库、密钥、日志目录            |
| `Content.*RetentionDays`    | 原文 / 脱敏后 / 外发内容留存      |
| `Guardrails.OnError`        | `block`（fail-closed）/ `allow`   |
| `Compression.Enabled`       | 是否启用文本级压缩                |
| `LoadBalancing.Enabled`     | 是否启用同名模型分流              |
| `PersonalProviders.*`       | BYOK 开关、供应商白名单、审计粒度 |

---

## 部署注意

- 单机版，**无多租户**；管理入口应只绑内网/loopback（见 PRD §7.7）。
- 生产环境请在首次登录后删除 `data/initial-admin-password.txt`。
- 备份 `data/content.key`：丢失后已存正文永久不可解。
- 生产建议切换 PostgreSQL 并生成迁移；当前 SQLite 使用 `EnsureCreated`，不适合演进式 schema 变更。
- 网络层拦截不在本产品范围内：由企业 DNS / 内网网关阻断 AI 域名，只放行本服务。
