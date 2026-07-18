
using System;
using System.IO;

class Program {
    static void Main() {
        string dl = "F:";
        var sigPath = Path.Combine(dl + "\\", ".Vcrypt");
        Console.WriteLine($"sigPath: {sigPath}");
        Console.WriteLine($"Exists: {File.Exists(sigPath)}");
    }
}

