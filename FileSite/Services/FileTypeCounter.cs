using FileSite.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace FileSite.Services
{
    public class FileTypeCounter : IHostedService, IDisposable
    {
        public Dictionary<string, int> Dict = new Dictionary<string, int>();
        private Timer? _timer;
        private ILogger<FileTypeCounter> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        public FileTypeCounter(ILogger<FileTypeCounter> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public void CountFileExtensions(object? state)
        {
            var scope = _scopeFactory.CreateScope();
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            int K = 0;
            Dict.Clear();
            foreach (var i in context.FileDatas)
            {
                FileInfo fileInfo = new(i.Location);
                Dict.TryGetValue(fileInfo.Extension, out K);
                Dict[fileInfo.Extension] = ++K;

            }
            
            scope.Dispose();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {   if(_timer != null) { _timer.Dispose(); }
            _logger.LogInformation(eventId:100,"FileTypeCounter is running");
            _timer = new(CountFileExtensions, null, TimeSpan.Zero, TimeSpan.FromHours(6.0));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(eventId:-100,"FileTypeCounter has stopped");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose() { _timer?.Dispose(); }
    }
}
