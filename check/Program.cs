using System.Diagnostics;
using Dusk;

// Runs on any OS: dotnet run --project check
var id = Color.Ramp(6500, 1);
Trace.Assert(id[255] == 65535 && id[767] == 65535, "6500K should be be identity");
var (r, g, b) = Color.Rgb(500);
Trace.Assert(r == 1 && g < 0.3 && b == 0, "500K should be deep red with no blue");
foreach (var k in new[] { 6000, 5000, 3400, 2700, 1900, 1200 })
    Trace.Assert(Color.Rgb(k).b < Color.Rgb(k + 100).b + 1e-9, $"blue should not rise as it warms ({k}K)");
var dim = Color.Ramp(3400, 0.05);
Trace.Assert(dim[255] < 3400 && dim[767] < dim[255], "5% dim should be dark and warm");
Trace.Assert(Color.Matches(id, id) && !Color.Matches(id, dim), "readback match");
Console.WriteLine("ok");
