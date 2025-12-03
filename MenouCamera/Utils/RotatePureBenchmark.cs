using MenouCamera.Models.Services.Implementations;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MenouCamera.Utils
{
    
/// <summary>
/// 純コスト（回転のみ）ベンチ：平均 / p95 / 最大 と FPS換算を返す
/// </summary>
public static class RotatePureBenchmark
    {
        public static RotatePureResult Measure(Mat src, int angle, int warmup = 30, int iterations = 1000)
        {
            using var dst = new Mat(); // 使い回し
            var sw = new Stopwatch();

            // ウォームアップ
            for (int i = 0; i < warmup; i++)
                OpenCvCameraService.RotateRightAngle(src, angle, dst);

            var samples = new double[iterations];
            for (int i = 0; i < iterations; i++)
            {
                sw.Restart();
                OpenCvCameraService.RotateRightAngle(src, angle, dst);
                sw.Stop();
                samples[i] = sw.Elapsed.TotalMilliseconds;
            }

            Array.Sort(samples);
            double avg = samples.Average();
            double p95 = samples[(int)Math.Floor(0.95 * (samples.Length - 1))];
            double max = samples[^1];
            double fps = avg > 0 ? 1000.0 / avg : double.PositiveInfinity;

            return new RotatePureResult
            {
                Angle = angle,
                Iterations = iterations,
                AvgMs = avg,
                P95Ms = p95,
                MaxMs = max,
                EstimatedFPS = fps
            };
        }
    }
}
