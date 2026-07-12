using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.AccountTitle)
            .HasColumnName("account_title")
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(a => a.AccountNumber)
            .HasColumnName("account_number")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(a => a.NormalBalance)
            .HasColumnName("normal_balance")
            .HasMaxLength(10);

        builder.Property(a => a.Description)
            .HasColumnName("description");  // nvarchar(max), nullable

        builder.Property(a => a.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(a => a.ExpenseClass)
            .HasColumnName("expense_class")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(a => a.DefaultNature)
            .HasColumnName("default_nature")
            .HasMaxLength(20);

        builder.Property(a => a.DefaultApplyReserve)
            .HasColumnName("default_apply_reserve")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(a => a.AccountNumber)
            .IsUnique()
            .HasDatabaseName("IX_accounts_number");

        builder.HasIndex(a => a.AccountTitle)
            .HasDatabaseName("IX_accounts_title");
    }
}
