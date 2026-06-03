using FileSite.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileSite.Data.Configurations
{
    public class FileDataConfiguration : IEntityTypeConfiguration<FileData>
    {
        public void Configure(EntityTypeBuilder<FileData> builder)
        {
            builder.HasKey(fd => fd.Id);

            builder.Property(fd => fd.Name)
                .IsRequired();

            builder.Property(fd => fd.Location)
                .IsRequired();

            builder.Property(fd => fd.hash)
                .IsRequired();

            builder.Property(fd => fd.LifeTime)
                .IsRequired();

            builder.HasOne(fd => fd.Owner)
                .WithMany()
                .HasForeignKey(fd => fd.OwnerId);
        }
    }
}