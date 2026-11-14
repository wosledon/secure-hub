using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureHub.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Apps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditAnchors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditAnchors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEventDisputes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisputeType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEventDisputes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActorLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AppId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestId = table.Column<string>(type: "TEXT", nullable: true),
                    ModelAlias = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpstreamModel = table.Column<string>(type: "TEXT", nullable: true),
                    ModelGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModelMemberId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    PolicyId = table.Column<string>(type: "TEXT", nullable: true),
                    PolicyVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    RuleId = table.Column<string>(type: "TEXT", nullable: true),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    InputTokensOriginal = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    CostUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: true),
                    ContentMissing = table.Column<bool>(type: "INTEGER", nullable: false),
                    BillingOwner = table.Column<int>(type: "INTEGER", nullable: false),
                    PrevHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Subject = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Period = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TokenLimit = table.Column<long>(type: "INTEGER", nullable: true),
                    CostLimit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    TokensUsed = table.Column<long>(type: "INTEGER", nullable: false),
                    CostUsed = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    PeriodStart = table.Column<long>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogModels",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Family = table.Column<string>(type: "TEXT", nullable: true),
                    SupportsTools = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsReasoning = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsStructuredOutput = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsAttachment = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContextWindow = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    InputPricePerMillion = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    OutputPricePerMillion = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    CanonicalModelId = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentBlobs",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    Variant = table.Column<int>(type: "INTEGER", nullable: false),
                    AppId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CipherText = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Nonce = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Tag = table.Column<byte[]>(type: "BLOB", nullable: false),
                    DekId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<int>(type: "INTEGER", nullable: false),
                    Truncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentBlobs", x => x.ContentId);
                });

            migrationBuilder.CreateTable(
                name: "ContentDeks",
                columns: table => new
                {
                    DekId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    WrappedDek = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RetiredAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentDeks", x => x.DekId);
                });

            migrationBuilder.CreateTable(
                name: "Entitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectType = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModelsJson = table.Column<string>(type: "TEXT", nullable: true),
                    TokenQuota = table.Column<long>(type: "INTEGER", nullable: true),
                    CostQuota = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    TokensUsed = table.Column<long>(type: "INTEGER", nullable: true),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entitlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelGroupMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpstreamModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Weight = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelGroupMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsAuto = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowCrossScope = table.Column<bool>(type: "INTEGER", nullable: false),
                    Strategy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CipherText = table.Column<byte[]>(type: "BLOB", nullable: false),
                    KeyHint = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RetiredAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderOfferings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpstreamModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CanonicalModelId = table.Column<string>(type: "TEXT", nullable: true),
                    GroupKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SupportsTools = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsReasoning = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsStructuredOutput = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContextWindow = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    InputPricePerMillion = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    OutputPricePerMillion = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderOfferings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Providers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    HealthStatus = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Providers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PolicyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActiveVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityPolicyVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PolicyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    ScopeJson = table.Column<string>(type: "TEXT", nullable: false),
                    WhenJson = table.Column<string>(type: "TEXT", nullable: false),
                    ThenJson = table.Column<string>(type: "TEXT", nullable: false),
                    OnError = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RolloutJson = table.Column<string>(type: "TEXT", nullable: true),
                    ChangeReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityPolicyVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    AppId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VirtualKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModelGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModelMemberId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ModelScope = table.Column<int>(type: "INTEGER", nullable: false),
                    BillingOwner = table.Column<int>(type: "INTEGER", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UpstreamModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    InputTokensOriginal = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CostUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Estimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    TotpSecret = table.Column<string>(type: "TEXT", nullable: true),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RolesJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VirtualKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KeyId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SecretHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OwnerType = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScopesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ConstraintsJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsRevoked = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RotatedFromId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Apps_Name",
                table: "Apps",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Apps_TeamId",
                table: "Apps",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditAnchors_Sequence",
                table: "AuditAnchors",
                column: "Sequence");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEventDisputes_EventId",
                table: "AuditEventDisputes",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_AppId_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "AppId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_BillingOwner",
                table: "AuditEvents",
                column: "BillingOwner");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAt",
                table: "AuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_PolicyId_RuleId",
                table: "AuditEvents",
                columns: new[] { "PolicyId", "RuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_RequestId",
                table: "AuditEvents",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Sequence",
                table: "AuditEvents",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_Subject_SubjectId",
                table: "Budgets",
                columns: new[] { "Subject", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogModels_ProviderKey",
                table: "CatalogModels",
                column: "ProviderKey");

            migrationBuilder.CreateIndex(
                name: "IX_ContentBlobs_AppId_ExpiresAt",
                table: "ContentBlobs",
                columns: new[] { "AppId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentBlobs_ExpiresAt",
                table: "ContentBlobs",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ContentBlobs_RequestId_Direction_Variant",
                table: "ContentBlobs",
                columns: new[] { "RequestId", "Direction", "Variant" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_ProviderId",
                table: "Entitlements",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Entitlements_SubjectType_SubjectId",
                table: "Entitlements",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelGroupMembers_GroupId_Priority",
                table: "ModelGroupMembers",
                columns: new[] { "GroupId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelGroups_Name",
                table: "ModelGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCredentials_OwnerUserId",
                table: "ProviderCredentials",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCredentials_ProviderId",
                table: "ProviderCredentials",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderOfferings_GroupKey",
                table: "ProviderOfferings",
                column: "GroupKey");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderOfferings_ProviderId_UpstreamModel",
                table: "ProviderOfferings",
                columns: new[] { "ProviderId", "UpstreamModel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Providers_Alias",
                table: "Providers",
                column: "Alias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Providers_Scope_OwnerUserId",
                table: "Providers",
                columns: new[] { "Scope", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityPolicies_PolicyKey",
                table: "SecurityPolicies",
                column: "PolicyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityPolicyVersions_PolicyId_Version",
                table: "SecurityPolicyVersions",
                columns: new[] { "PolicyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_AppId_OccurredAt",
                table: "UsageEvents",
                columns: new[] { "AppId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_BillingOwner",
                table: "UsageEvents",
                column: "BillingOwner");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_OccurredAt",
                table: "UsageEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_ProviderId",
                table: "UsageEvents",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VirtualKeys_KeyId",
                table: "VirtualKeys",
                column: "KeyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VirtualKeys_OwnerType_OwnerId",
                table: "VirtualKeys",
                columns: new[] { "OwnerType", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Apps");

            migrationBuilder.DropTable(
                name: "AuditAnchors");

            migrationBuilder.DropTable(
                name: "AuditEventDisputes");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "CatalogModels");

            migrationBuilder.DropTable(
                name: "ContentBlobs");

            migrationBuilder.DropTable(
                name: "ContentDeks");

            migrationBuilder.DropTable(
                name: "Entitlements");

            migrationBuilder.DropTable(
                name: "ModelGroupMembers");

            migrationBuilder.DropTable(
                name: "ModelGroups");

            migrationBuilder.DropTable(
                name: "ProviderCredentials");

            migrationBuilder.DropTable(
                name: "ProviderOfferings");

            migrationBuilder.DropTable(
                name: "Providers");

            migrationBuilder.DropTable(
                name: "SecurityPolicies");

            migrationBuilder.DropTable(
                name: "SecurityPolicyVersions");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "UsageEvents");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "VirtualKeys");
        }
    }
}
