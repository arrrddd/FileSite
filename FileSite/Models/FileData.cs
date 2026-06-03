using FileSite.Data.Enums;

namespace FileSite.Models
{
    public class FileData
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public string hash { get; set; }
        public FileFileTimeEnum LifeTime { get; set; }
        public long CreationDate { get; set; }
        public long Size { get; set; }
        public string? OwnerId { get; set; }
        public AppUser? Owner { get; set; }
    }
}