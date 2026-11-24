using System.Diagnostics;
using Dusk;

// Runs on any OS: dotnet run --project check
var id = Warmth.Ramp(6500, 1);
Trace.Assert(id[255] == 65535 && id[767] == 65535, "6500K should be be identity");
var (r, g, b) = Warmth.Rgb(500);
Trace.Assert(r == 1 && g < 0.3 && b == 0, "500K should be deep red with no blue");
foreach (var k in new[] { 6000, 5000, 3400, 2700, 1900, 1200 })
    Trace.Assert(Warmth.Rgb(k).b < Warmth.Rgb(k + 100).b + 1e-9, $"blue should not rise as it warms ({k}K)");
var dim = Warmth.Ramp(3400, 0.05);
Trace.Assert(dim[255] < 3400 && dim[767] < dim[255], "5% dim should be dark and warm");
Trace.Assert(Warmth.Matches(id, id) && !Warmth.Matches(id, dim), "readback match");
var half = Warmth.Blend(dim, 0.5);
Trace.Assert(Warmth.Matches(Warmth.Blend(dim, 0), id) && Warmth.Matches(Warmth.Blend(dim, 1), dim) && half[767] > dim[767] && half[767] < id[767], "blend toward neutral");
Console.WriteLine("ok");
