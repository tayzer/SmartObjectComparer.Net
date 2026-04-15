using System;
using System.IO;
using System.Linq;
using System.Reflection;

static void Dump(string path)
{
    var asm = Assembly.LoadFrom(path);
    Console.WriteLine($"Assembly: {Path.GetFileName(path)}");
    var routeAttrName = "Microsoft.AspNetCore.Components.RouteAttribute";
    var hits = asm.GetTypes()
        .Select(t => new
        {
            Type = t,
            Routes = t.GetCustomAttributesData()
                .Where(a => a.AttributeType.FullName == routeAttrName)
                .Select(a => a.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? "<null>")
                .ToList()
        })
        .Where(x => x.Routes.Count > 0)
        .OrderBy(x => x.Type.FullName)
        .ToList();

    foreach (var hit in hits)
    {
        Console.WriteLine($"  {hit.Type.FullName}: {string.Join(", ", hit.Routes)}");
    }

    if (hits.Count == 0)
    {
        Console.WriteLine("  <no RouteAttribute types>");
    }

    Console.WriteLine();
}

Dump(@"C:\Dev\GitMain\ComparisonTool\ComparisonTool.UI\bin\Release\net10.0\ComparisonTool.UI.dll");
Dump(@"C:\Dev\GitMain\ComparisonTool\ComparisonTool.Report\bin\Release\net10.0\ComparisonTool.Report.dll");
