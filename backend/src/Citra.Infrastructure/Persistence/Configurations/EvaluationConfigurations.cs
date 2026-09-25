using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Evaluation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Citra.Infrastructure.Persistence.Configurations;

internal sealed class EvalSetConfiguration : IEntityTypeConfiguration<EvalSet>
{
    public void Configure(EntityTypeBuilder<EvalSet> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(EvalSet.NameMaxLength).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(EvalSet.DescriptionMaxLength);

        builder.HasOne<Bot>()
            .WithMany()
            .HasForeignKey(s => s.BotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Questions)
            .WithOne()
            .HasForeignKey(q => q.EvalSetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Questions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(s => s.TenantId);
    }
}

internal sealed class EvalQuestionConfiguration : IEntityTypeConfiguration<EvalQuestion>
{
    public void Configure(EntityTypeBuilder<EvalQuestion> builder)
    {
        builder.Property(q => q.Question).HasMaxLength(EvalQuestion.QuestionMaxLength).IsRequired();
        builder.Property(q => q.ExpectedAnswer).HasMaxLength(EvalQuestion.ExpectedAnswerMaxLength).IsRequired();

        // If the expected document is deleted, the question stays (answer grading still works).
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(q => q.ExpectedDocumentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class EvalRunConfiguration : IEntityTypeConfiguration<EvalRun>
{
    public void Configure(EntityTypeBuilder<EvalRun> builder)
    {
        builder.Property(r => r.Status).IsEnumText();
        builder.Property(r => r.Model).HasMaxLength(EvalRun.ModelMaxLength).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(EvalRun.NotesMaxLength);
        builder.Property(r => r.Error).HasMaxLength(EvalRun.ErrorMaxLength);
        builder.Property(r => r.TotalCostUsd).IsMoney();

        builder.HasOne<EvalSet>()
            .WithMany()
            .HasForeignKey(r => r.EvalSetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Results)
            .WithOne()
            .HasForeignKey(x => x.EvalRunId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Results).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Run history per eval set, newest first (also covers the eval_set_id foreign key).
        builder.HasIndex(r => new { r.EvalSetId, r.CreatedAt });

        builder.HasIndex(r => r.TenantId);
    }
}

internal sealed class EvalResultConfiguration : IEntityTypeConfiguration<EvalResult>
{
    public void Configure(EntityTypeBuilder<EvalResult> builder)
    {
        builder.Property(r => r.ActualAnswer).HasMaxLength(EvalResult.ActualAnswerMaxLength).IsRequired();
        builder.Property(r => r.Grade).IsEnumText();
        builder.Property(r => r.JudgeReason).HasMaxLength(EvalResult.JudgeReasonMaxLength).IsRequired();
        builder.Property(r => r.CostUsd).IsMoney();

        builder.HasOne<EvalQuestion>()
            .WithMany()
            .HasForeignKey(r => r.EvalQuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Each question is scored once per run (also covers the eval_run_id foreign key).
        builder.HasIndex(r => new { r.EvalRunId, r.EvalQuestionId }).IsUnique();
    }
}
