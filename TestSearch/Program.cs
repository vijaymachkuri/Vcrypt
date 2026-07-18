
using System;
using System.IO;
using System.Linq;

class Program {
    static void Main() {
        var files = Directory.GetFiles(@"c:\Users\vijay\Desktop\securepro", "*.cs", SearchOption.AllDirectories);
        foreach(var f in files) {
            if(File.ReadAllText(f).Contains("Signature file missing")) {
                Console.WriteLine(f);
            }
        }
    }
}

