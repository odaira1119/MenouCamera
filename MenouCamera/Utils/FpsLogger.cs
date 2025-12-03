using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MenouCamera.Utils
{
    public sealed class FpsLogger : IDisposable
    {
        private readonly List<double> _samples = new(capacity: 4096);
        private readonly object _lock = new();
        private readonly string? _csvPath;

        public FpsLogger(string? csvPath = null) { _csvPath = csvPath; }

        public void OnTick(double fps)
        {
            lock (_lock) { _samples.Add(fps); }
        }

        public (double Avg, double Min, double Max, double P50, double P90, double P99) SnapshotStats()
        {
            lock (_lock)
            {
                if (_samples.Count == 0) return (0, 0, 0, 0, 0, 0);
                var arr = _samples.OrderBy(x => x).ToArray();
                double P(double q) => arr[(int)Math.Clamp(Math.Round((arr.Length - 1) * q), 0, arr.Length - 1)];
                var avg = _samples.Average();
                return (avg, arr.First(), arr.Last(), P(0.50), P(0.90), P(0.99));
            }
        }

        public void SaveCsvIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_csvPath)) return;
            lock (_lock)
            {
                using var sw = new System.IO.StreamWriter(_csvPath, append: false);
                sw.WriteLine("Timestamp,FPS");
                // 単純化のため時刻は等間隔扱いにせず、書き込み時刻を入れる
                foreach (var v in _samples)
                    sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},{v:F3}");
            }
        }

        public void Dispose() { SaveCsvIfNeeded(); }
    }

}
