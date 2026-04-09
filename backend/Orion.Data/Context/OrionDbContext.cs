using Microsoft.EntityFrameworkCore;
using Orion.Core.Entities;

namespace Orion.Data.Context;

public class OrionDbContext : DbContext
{
    public OrionDbContext(DbContextOptions<OrionDbContext> options) : base(options)
    {
    }

    public DbSet<Conversation> Conversations { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<MemoryVector> MemoryVectors { get; set; } = null!;
    public DbSet<UserProfile> UserProfiles { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<BehaviorPattern> BehaviorPatterns { get; set; } = null!;
    public DbSet<ToolExecution> ToolExecutions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Conversations
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("conversations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Type).HasConversion<string>();
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.LlmProvider).HasConversion<string>();
            entity.Property(e => e.LlmProvider).HasColumnName("llm_provider");
            entity.Property(e => e.StartedAt).HasColumnType("timestamptz");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.EndedAt).HasColumnType("timestamptz");
            entity.Property(e => e.EndedAt).HasColumnName("ended_at");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.HasMany(e => e.Messages).WithOne(m => m.Conversation).HasForeignKey(m => m.ConversationId);
        });

        // Messages
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id");
            entity.Property(e => e.Role).HasConversion<string>();
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ToolName).HasColumnName("tool_name");
            entity.Property(e => e.ToolInput).HasColumnType("jsonb");
            entity.Property(e => e.ToolInput).HasColumnName("tool_input");
            entity.Property(e => e.ToolResult).HasColumnType("jsonb");
            entity.Property(e => e.ToolResult).HasColumnName("tool_result");
        });

        // MemoryVectors - EF Core ignores embedding column (pgvector type)
        // Embedding operations handled via raw SQL in MemoryRepository
        modelBuilder.Entity<MemoryVector>(entity =>
        {
            entity.ToTable("memory_vectors");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Content).IsRequired().HasColumnName("content");
            entity.Property(e => e.Source).HasColumnName("source");
            entity.Property(e => e.Importance).HasColumnName("importance");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").HasColumnName("created_at");
            entity.Property(e => e.LastAccessed).HasColumnType("timestamptz").HasColumnName("last_accessed");
            // Embedding column excluded from EF Core - use raw SQL for vector operations
            entity.Ignore(e => e.Embedding);
            entity.Ignore(e => e.UpdatedAt);
        });

        // UserProfile
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profile");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Value).IsRequired().HasColumnName("value");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz").HasColumnName("updated_at");
        });

        // AuditLog
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(100).HasColumnName("entity_type");
            entity.Property(e => e.EntityId).IsRequired().HasMaxLength(100).HasColumnName("entity_id");
            entity.Property(e => e.Action).IsRequired().HasMaxLength(50).HasColumnName("action");
            entity.Property(e => e.UserId).HasMaxLength(100).HasColumnName("user_id");
            entity.Property(e => e.UserName).HasMaxLength(200).HasColumnName("user_name");
            entity.Property(e => e.OldValues).HasColumnType("jsonb").HasColumnName("old_values");
            entity.Property(e => e.NewValues).HasColumnType("jsonb").HasColumnName("new_values");
            entity.Property(e => e.Metadata).HasColumnType("jsonb").HasColumnName("metadata");
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000).HasColumnName("error_message");
            entity.Property(e => e.Timestamp).HasColumnType("timestamptz").HasColumnName("timestamp");
            entity.Property(e => e.CorrelationId).HasMaxLength(100).HasColumnName("correlation_id");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.Success).HasColumnName("success");
            
            // Index pour performances
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Action);
            entity.HasIndex(e => e.CorrelationId);
        });

        // BehaviorPattern
        modelBuilder.Entity<BehaviorPattern>(entity =>
        {
            entity.ToTable("behavior_patterns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PatternType).IsRequired().HasMaxLength(100).HasColumnName("pattern_type");
            entity.Property(e => e.ObservedAt).HasColumnType("timestamptz").HasColumnName("observed_at");
            entity.Property(e => e.Context).HasColumnType("text").HasColumnName("context");
            entity.Property(e => e.OrionResponse).HasColumnType("text").HasColumnName("orion_response");
            
            entity.HasIndex(e => e.PatternType);
            entity.HasIndex(e => e.ObservedAt);
        });

        // ToolExecution
        modelBuilder.Entity<ToolExecution>(entity =>
        {
            entity.ToTable("tool_executions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.ToolName).IsRequired().HasColumnName("tool_name");
            entity.Property(e => e.Input).HasColumnType("jsonb").HasColumnName("input");
            entity.Property(e => e.Result).HasColumnType("jsonb").HasColumnName("result");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.ExecutedAt).HasColumnType("timestamptz").HasColumnName("executed_at");
            
            entity.HasOne(e => e.Message)
                .WithMany()
                .HasForeignKey(e => e.MessageId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
