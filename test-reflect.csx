using System.Reflection;

// Load the assembly
var asm = Assembly.LoadFrom(@"src/Spanner.InMemoryEmulator/bin/Debug/net8.0/Google.Cloud.Spanner.Data.dll");
var spannerEx = asm.GetType("Google.Cloud.Spanner.Data.SpannerException");
if (spannerEx == null) { Console.WriteLine("Type not found"); return; }
foreach (var p in spannerEx.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine($"{p.PropertyType.Name} {p.Name}");
