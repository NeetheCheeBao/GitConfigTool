using System;
using System.IO;
using System.Windows.Threading;

namespace GitConfigTool.Helpers
{
    /// <summary>
    /// 监听目录变化，把短时间内的多次事件合并成一次回调，回调在 UI 线程上执行。
    /// 目录不存在或无法监听时静默降级，不影响主流程。
    /// </summary>
    public sealed class FolderWatcher : IDisposable
    {
        /// <summary>去抖间隔：编辑器保存、临时文件改名等会连发多个事件</summary>
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

        private FileSystemWatcher _watcher;
        private readonly DispatcherTimer _timer;
        private readonly Action _onChanged;
        private bool _disposed;

        public FolderWatcher(string directory, string filter, Action onChanged)
        {
            _onChanged = onChanged;

            _timer = new DispatcherTimer { Interval = Debounce };
            _timer.Tick += (sender, e) =>
            {
                _timer.Stop();
                if (!_disposed) _onChanged();
            };

            try
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

                _watcher = new FileSystemWatcher(directory, filter)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _watcher.Created += OnFileEvent;
                _watcher.Changed += OnFileEvent;
                _watcher.Deleted += OnFileEvent;
                _watcher.Renamed += OnFileEvent;
                _watcher.Error += OnWatcherError;
            }
            catch (Exception)
            {
                // 路径不可用（权限、长度等）：放弃监听，不影响程序其他功能
                _watcher = null;
            }
        }

        /// <summary>文件事件来自后台线程，切回 UI 线程再重启去抖计时器</summary>
        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            _timer.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                _timer.Stop();
                _timer.Start();
            }));
        }

        /// <summary>监听出错（例如目录被删）时停掉，避免反复抛异常</summary>
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            if (_watcher == null) return;
            try { _watcher.EnableRaisingEvents = false; }
            catch (Exception) { /* 已失效，忽略 */ }
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Stop();

            if (_watcher != null)
            {
                try
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                }
                catch (Exception) { /* 已失效，忽略 */ }
                _watcher = null;
            }
        }
    }
}
