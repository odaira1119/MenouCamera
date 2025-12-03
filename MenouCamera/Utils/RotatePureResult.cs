using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MenouCamera.Utils
{
    public struct RotatePureResult
    {
        public int Angle;
        public int Iterations;
        public double AvgMs;
        public double P95Ms;
        public double MaxMs;
        public double EstimatedFPS;
        public override string ToString()
            => $"{Angle}deg N={Iterations} avg={AvgMs:F3}ms p95={P95Ms:F3}ms max={MaxMs:F3}ms -> ~{EstimatedFPS:F1} FPS";
    }
}
