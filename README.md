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

| 能力        | 说明                                                                                                                  |
| ----------- | --------------------------------------------------------------------------------------------------------------------- |
| 虚拟密钥    | `sk-sh-{env}-{keyId}-{secret}`，只存 SHA-256，常量时间比对，可吊销                                                    |
| 会话登录    | Cookie 会话 + RBAC（五角色 + EndUser 个人范围）                                                                       |
| 三级模型 ID | `[scope/]provider/model`，model 段允许含 `/`；歧义报错并给候选                                                        |
| 双账        | `billingOwner = org / user`，按**实际命中成员**记账                                                                   |
| 护栏        | 身份证（mod 11-2）、手机、银行卡（Luhn）、邮箱、PEM、JWT、高熵密钥                                                    |
| 脱敏        | 三种模式：`mask`（部分遮蔽）、`placeholder`（占位符）、`tokenize`（可逆令牌，响应自动还原）                           |
| 压缩        | 空白/Unicode 规范化、样板文本剥离、重复行折叠 + 完整性校验回退                                                        |
| 审计        | 链式哈希 + 逐条校验接口，时间截断到毫秒以保证可复验                                                                   |
| 内容存储    | AES-256-GCM，AAD 绑定 `requestId · direction · variant`，按变体留存                                                   |
| 供应商      | 企业级 / 个人 BYOK，凭据 DataProtection 加密；支持从 models.dev 目录选择供应商与模型后接入                          |
| 供应商生命周期 | 编辑端点、停用、凭据轮换与连通性测试（企业 + 个人）                                                                  |
| 组织管理    | 用户/角色/团队/应用 CRUD；停用即时撤销会话，最后一名平台管理员保护，密码只写不读                                     |
| 访问授权    | 按全体/团队/用户分配供应商与模型，路由与 `/v1/models` 均按授权过滤                                                    |
| 密钥约束    | 模型/供应商/IP 白名单、RPM/TPM/并发上限与过期时间，在数据面强制执行；个人密钥可吊销/轮换                              |
| 可观测性    | P50/P95/P99 延迟、成功率、错误码、按模型/供应商/小时分布                                                              |
| 系统基线    | 审计链、密钥文件、初始密码、HTTPS、留存与凭据一致性自检，展示生效配置                                                 |
| 运行期配置  | 留存/压缩/护栏/BYOK 设置写入数据库后立即生效，无需重启                                                               |
| MCP 网关    | `POST /mcp` JSON-RPC：initialize / tools/list / tools/call；只读审计、用量、模型、健康工具，按密钥归属收窄数据        |
| 原生直通    | `/v1/native/{providerAlias}/{**path}` 保留上游协议原貌，仍执行认证、授权、约束、限流、预算与审计                      |
| 协议面      | `POST /v1/chat/completions`（含流式）、`GET /v1/models`、`POST /v1/embeddings`、`POST /mcp`、`/v1/native/*`             |
| 控制台      | 概览、供应商、模型目录、组织管理、路由、密钥、策略、用量预算、审计、个人工作台、审计助手                              |
| i18n        | 中/英切换；后端 `Accept-Language` 本地化错误文案，审计只存 code                                                       |
| 策略引擎    | 结构化条件（any/all + detector）、scope 通配、优先级、灰度分桶、版本发布与回滚、低置信度 dry-run                      |
| 策略导入    | JSON/YAML 导入导出、观察模式（只告警）、检测器命中阈值 `minMatches`                                                  |
| 预算与配额  | 组织/团队/应用维度，按日/周/月滚动，超限 429 并写审计                                                                 |
| 显式模型组  | 具名组优先于同名 offering，成员优先级/权重/健康态驱动，可选跨 scope                                                   |
| 目录同步    | 每 24h 拉取 models.dev（约 9000+ 模型与 236 个供应商端点元数据），失败保留上一份快照，可手动触发                      |
| TOTP        | 可选启用：`enable` 返回 otpauth URI，`activate` 验证后登录需动态码                                                    |
| 审计导出    | 按时间范围流式导出 NDJSON                                                                                             |
| 审计深度    | 事件详情、关联正文解密（需 `ContentRead`）、requestId 调用链、到期内容清理                                            |
| 误报申诉    | 独立追加表记录，不回写审计行                                                                                          |
| 审计助手    | MAF 1.28 `ChatClientAgent`；**8 个只读工具**、多轮会话持久化、SSE 流式输出；工具轮数有上限，每次工具调用写审计        |
| 助手会话    | `AssistantSession` / `AssistantMessage` 落库，历史回放上限可配；会话只属本人，管理员不可读                            |
| EF 迁移     | **两个 Provider 各一套迁移**（`Migrations/Sqlite`、`Migrations/Postgres`），同一 `DbContext` 基类 + Provider 绑定子类 |
| 员工审计    | 员工流量（含 BYOK）**强制记录正文**，与全局 `content.persist` 解耦，不可配降级                                        |

## 未实现

| 能力                | 状态                                       |
| ------------------- | ------------------------------------------ |
| 语义缓存 / 语义路由 | 按 PRD 决定不做（T14）                     |
| 助手图/多代理编排   | v1 仅单 agent + 工具，未引入 workflow 编排 |
| 分布式会话/集群     | 单机版，会话与限流均为进程内               |
| MCP 服务端推送      | 仅 JSON-RPC 请求/响应；未实现 SSE 服务端推送 |
| 原生直通流式改写    | 原样透传上游响应；不做协议转换或内容改写   |
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

| 键                               | 说明                                                      |
| -------------------------------- | --------------------------------------------------------- |
| `Database.Provider`              | `Sqlite` / `Postgres`                                     |
| `Database.ConnectionString`      | SQLite 文件或 PG 连接串                                   |
| `Security.DataDirectory`         | 数据库、密钥、日志目录                                    |
| `Content.*RetentionDays`         | 原文 / 脱敏后 / 外发内容留存                              |
| `Guardrails.OnError`             | `block`（fail-closed）/ `allow`                           |
| `Compression.Enabled`            | 是否启用文本级压缩                                        |
| `LoadBalancing.Enabled`          | 是否启用同名模型分流                                      |
| `PersonalProviders.*`            | BYOK 开关、供应商白名单、禁用用户（审计粒度固定，无开关） |
| `Assistant.DefaultModel`         | 助手默认模型（三级 ID 或别名）                            |
| `Assistant.MaxRounds`            | 工具调用轮数上限（1–12，默认 6）                          |
| `Assistant.MaxHistoryMessages`   | 上下文回放的消息条数（默认 12）                           |
| `Assistant.AllowContentRead`     | 是否允许 `read_content` 工具（默认 `false`）              |
| `Guardrails.RedactionMode`       | `mask` / `placeholder` / `tokenize`（默认 `mask`）        |
| `Guardrails.TokenRetentionHours` | tokenize 令牌存活期（默认 24h）                           |

---

## 部署注意

- 单机版，**无多租户**；管理入口应只绑内网/loopback（见 PRD §7.7）。
- 生产环境请在首次登录后删除 `data/initial-admin-password.txt`。
- 备份 `data/content.key`：丢失后已存正文永久不可解。
- **数据库迁移**：SQLite 与 PostgreSQL 各有一套独立迁移。首次启动自动 `Migrate()`；新增实体后在仓库根执行：

  ```powershell
  dotnet ef migrations add <Name> -c SqliteSecureHubDbContext  -o Migrations/Sqlite  -p src/SecureHub.Data
  dotnet ef migrations add <Name> -c PostgresSecureHubDbContext -o Migrations/Postgres -p src/SecureHub.Data
  ```

  两个都要生成——只生成一个会让另一侧在启动时因 pending 迁移缺失而建表不全。
- 网络层拦截不在本产品范围内：由企业 DNS / 内网网关阻断 AI 域名，只放行本服务。
