using System;
using System.IO;
using System.Text;

class Program {
    static void Main() {
        var file = "test_bom.txt";
        File.WriteAllText(file, "F", new UTF8Encoding(true)); // with BOM
        var text = File.ReadAllText(file);
        Console.WriteLine($"Length: {text.Length}, Char: {(int)text[0]}");
    }
}
